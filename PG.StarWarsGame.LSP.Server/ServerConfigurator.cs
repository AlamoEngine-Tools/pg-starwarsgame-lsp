// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Parsing;
using Serilog;

namespace PG.StarWarsGame.LSP.Server;

public static class ServerConfigurator
{
    public static LanguageServerOptions Apply(LanguageServerOptions options)
    {
        return options
            .ConfigureLogging(x => x
                .SetMinimumLevel(LogLevel.Information)
                .AddLanguageProtocolLogging()
#if DEBUG
                .AddSerilog(dispose: true)
#endif
                .AddSentry(o =>
                {
                    o.Dsn = GetSentryDsn();
                    o.Debug = true;
                    o.AutoSessionTracking = true;
                    o.TracesSampleRate = 1.0;
                    o.EnableLogs = true;
                }))
            .WithHandler<XmlTextDocumentSyncHandler>()
            .WithHandler<XmlHoverHandler>()
            .WithHandler<XmlCompletionHandler>()
            .WithHandler<XmlDefinitionHandler>()
            .WithHandler<XmlReferencesHandler>()
            .WithHandler<XmlRenameHandler>()
            .WithServices(services =>
            {
                services.AddSingleton<ILspConfigurationProvider, LspConfigurationProvider>();
                services.AddSingleton<IFileSystem, FileSystem>();
                services.AddSingleton<IFileHelper, FileHelper>();
                services.AddSingleton<SchemaHttpCache>();

                // Late-binding proxy: OmniSharp resolves ISchemaProvider at handler-registration time
                // (before OnInitialize). The proxy starts empty; Configure() is called in OnInitialize
                // once the schema source is known from initializationOptions.
                services.AddSingleton<SchemaProviderProxy>();
                services.AddSingleton<ISchemaProvider>(sp => sp.GetRequiredService<SchemaProviderProxy>());

                services.AddSingleton<EaWXmlContext>();
                services.AddSingleton<IEaWXmlContext>(sp => sp.GetRequiredService<EaWXmlContext>());

                services.AddSingleton<IGameWorkspaceHost, GameWorkspaceHost>();
                services.AddSingleton<IGameDocumentParser, XmlGameDocumentParser>();
                services.AddSingleton<IGameIndexService, GameIndexService>();
                services.AddSingleton<IFileTypeRegistry, FileTypeRegistry>();
                services.AddSingleton<WorkspaceScanner>();

                services.AddSingleton<BaselineLoader>(sp =>
                    new BaselineLoader(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BaselineLoader)),
                        sp.GetRequiredService<IFileHelper>(),
                        sp.GetRequiredService<ILogger<BaselineLoader>>()));

                services.AddHttpClient(nameof(HttpSchemaProvider));
                services.AddHttpClient(nameof(BaselineLoader));

                services.AddXmlLanguageServices();
            })
            .OnInitialize(async (server, request, ct) =>
            {
                var tx = SentrySdk.StartTransaction("lsp.initialize", "server.lifecycle");
                try
                {
                    var logger = server.Services.GetRequiredService<ILogger<LspConfigurationProvider>>();
                    var configProvider = server.Services.GetRequiredService<ILspConfigurationProvider>();

                    var configSpan = tx.StartChild("config.load", "Load initialization options");
                    configProvider.LoadFrom(request.InitializationOptions);
                    logger.LogInformation("Loaded configuration: {@Config}", configProvider.Current);
                    configSpan.Finish(SpanStatus.Ok);

                    var eaWXmlContext = server.Services.GetRequiredService<EaWXmlContext>();
                    foreach (var dir in configProvider.Current.XmlDirectories)
                        eaWXmlContext.AddDirectory(dir);

                    var schemaSpan = tx.StartChild("schema.setup", "Configure schema provider");
                    var src = configProvider.Current.SchemaSource;
                    ISchemaProvider realProvider;
                    if (src.Type == SchemaSourceType.Local && !string.IsNullOrWhiteSpace(src.LocalPath))
                    {
                        realProvider = new LocalFileSchemaProvider(src.LocalPath,
                            server.Services.GetRequiredService<ILogger<LocalFileSchemaProvider>>());
                    }
                    else
                    {
                        var http = server.Services.GetRequiredService<IHttpClientFactory>()
                            .CreateClient(nameof(HttpSchemaProvider));
                        realProvider = new HttpSchemaProvider(http, src.Url,
                            server.Services.GetRequiredService<SchemaHttpCache>(),
                            server.Services.GetRequiredService<ILogger<HttpSchemaProvider>>());
                    }

                    var proxy = server.Services.GetRequiredService<SchemaProviderProxy>();
                    proxy.Configure(realProvider);
                    schemaSpan.Finish(SpanStatus.Ok);

                    if (realProvider is HttpSchemaProvider httpProvider)
                        _ = Task.Run(async () =>
                        {
                            var fetchTx = SentrySdk.StartTransaction("lsp.schema.http-fetch", "schema.load");
                            try
                            {
                                await httpProvider.LoadAsync(CancellationToken.None);
                                fetchTx.Finish(SpanStatus.Ok);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "HTTP schema load failed");
                                fetchTx.Finish(SpanStatus.InternalError);
                            }
                        });
                    // LocalFileSchemaProvider already loads synchronously in its constructor.

                    tx.Finish(SpanStatus.Ok);
                }
                catch (Exception)
                {
                    tx.Finish(SpanStatus.InternalError);
                    throw;
                }

                await Task.CompletedTask;
            })
            .OnInitialized(async (server, request, response, ct) =>
            {
                var tx = SentrySdk.StartTransaction("lsp.initialized", "server.lifecycle");
                try
                {
                    server.Services.GetRequiredService<XmlDiagnosticsPublisher>();

                    var initLogger = server.Services.GetRequiredService<ILogger<LspConfigurationProvider>>();
                    var config = server.Services.GetRequiredService<ILspConfigurationProvider>();
                    var indexService = server.Services.GetRequiredService<IGameIndexService>();
                    var baselineLoader = server.Services.GetRequiredService<BaselineLoader>();

                    var baselineSpan = tx.StartChild("baseline.load", "Load baseline index");
                    BaselineIndex baseline;
                    try
                    {
                        baseline = await baselineLoader.LoadAsync(config.Current.BaselineSource, ct);
                        baselineSpan.Finish(SpanStatus.Ok);
                    }
                    catch (Exception ex)
                    {
                        initLogger.LogError(ex, "Baseline load failed; using empty baseline");
                        baselineSpan.Finish(SpanStatus.InternalError);
                        baseline = BaselineIndex.Empty;
                    }

                    indexService.ApplyBaseline(baseline);

                    var workspaceFolderPaths = request.WorkspaceFolders
                        ?.Select(f => f.Uri.GetFileSystemPath())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                    var folders = workspaceFolderPaths is { Count: > 0 }
                        ? workspaceFolderPaths
                        : request.RootUri is not null
                            ? [request.RootUri.GetFileSystemPath()]
                            : [];
                    if (folders.Count > 0)
                    {
                        var scanner = server.Services.GetRequiredService<WorkspaceScanner>();
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await scanner.ScanAsync(folders, CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                initLogger.LogError(ex, "Workspace scan failed");
                            }
                            finally
                            {
                                server.SendNotification("$/workspaceScanComplete");
                            }
                        }, CancellationToken.None);
                    }

                    tx.Finish(SpanStatus.Ok);
                }
                catch (Exception)
                {
                    tx.Finish(SpanStatus.InternalError);
                    throw;
                }

                await Task.CompletedTask;
            });
    }

    private static string? GetSentryDsn()
    {
        // Preferred: baked in at publish time via -p:SentryDsn=<CI secret>
        var fromMetadata = typeof(ServerConfigurator).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SentryDsn")?.Value;
        if (!string.IsNullOrEmpty(fromMetadata))
            return fromMetadata;

        // Local dev fallback: set via SENTRY_DSN in .env.sentry loaded by the run configuration
        var fromEnv = Environment.GetEnvironmentVariable("SENTRY_DSN");
        return string.IsNullOrEmpty(fromEnv) ? "" : fromEnv;
    }
}