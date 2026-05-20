// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Parsing;
using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Server;

public static class ServerConfigurator
{
    public static LanguageServerOptions Apply(LanguageServerOptions options) => options
        .ConfigureLogging(x => x
            .AddLanguageProtocolLogging()
            .SetMinimumLevel(LogLevel.Information))
        .WithHandler<XmlTextDocumentSyncHandler>()
        .WithHandler<XmlHoverHandler>()
        .WithHandler<XmlCompletionHandler>()
        .WithServices(services =>
        {
            services.AddSingleton<ILspConfigurationProvider, LspConfigurationProvider>();
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<SchemaHttpCache>();

            services.AddSingleton<ISchemaProvider>(sp =>
            {
                var config = sp.GetRequiredService<ILspConfigurationProvider>();
                var src = config.Current.SchemaSource;
                if (src.Type == SchemaSourceType.Local && !string.IsNullOrWhiteSpace(src.LocalPath))
                    return new LocalFileSchemaProvider(src.LocalPath,
                        sp.GetRequiredService<ILogger<LocalFileSchemaProvider>>());
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpSchemaProvider));
                return new HttpSchemaProvider(http, src.Url,
                    sp.GetRequiredService<SchemaHttpCache>(),
                    sp.GetRequiredService<ILogger<HttpSchemaProvider>>());
            });

            services.AddSingleton<IGameWorkspaceHost, GameWorkspaceHost>();
            services.AddSingleton<IGameDocumentParser, XmlGameDocumentParser>();
            services.AddSingleton<IGameIndexService, GameIndexService>();
            services.AddSingleton<IFileTypeRegistry, FileTypeRegistry>();
            services.AddSingleton<WorkspaceScanner>();

            services.AddSingleton<BaselineLoader>(sp =>
                new BaselineLoader(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BaselineLoader)),
                    sp.GetRequiredService<IFileSystem>(),
                    sp.GetRequiredService<ILogger<BaselineLoader>>()));

            services.AddHttpClient(nameof(HttpSchemaProvider));
            services.AddHttpClient(nameof(BaselineLoader));

            services.AddXmlLanguageServices();
        })
        .OnInitialize(async (server, request, ct) =>
        {
            var logger = server.Services.GetRequiredService<ILogger<LspConfigurationProvider>>();
            var configProvider = server.Services.GetRequiredService<ILspConfigurationProvider>();
            configProvider.LoadFrom(request.InitializationOptions);
            logger.LogInformation("Loaded configuration: {@Config}", configProvider.Current);

            var schemaProvider = server.Services.GetRequiredService<ISchemaProvider>();
            switch (schemaProvider)
            {
                case HttpSchemaProvider http:
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await http.LoadAsync(CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "HTTP schema load failed");
                        }
                    });
                    break;
                case LocalFileSchemaProvider local:
                    try
                    {
                        local.Load();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Local schema load failed");
                    }

                    break;
            }
        })
        .OnInitialized(async (server, request, response, ct) =>
        {
            server.Services.GetRequiredService<XmlDiagnosticsPublisher>();

            var initLogger = server.Services.GetRequiredService<ILogger<LspConfigurationProvider>>();
            var config = server.Services.GetRequiredService<ILspConfigurationProvider>();
            var indexService = server.Services.GetRequiredService<IGameIndexService>();
            var baselineLoader = server.Services.GetRequiredService<BaselineLoader>();
            BaselineIndex baseline;
            try
            {
                baseline = await baselineLoader.LoadAsync(config.Current.BaselineSource, ct);
            }
            catch (Exception ex)
            {
                initLogger.LogError(ex, "Baseline load failed; using empty baseline");
                baseline = BaselineIndex.Empty;
            }

            indexService.ApplyBaseline(baseline);

            var folders = request.WorkspaceFolders?.Select(f => f.Uri.GetFileSystemPath()).ToList()
                          ?? (request.RootUri is not null ? [request.RootUri.GetFileSystemPath()] : []);
            if (folders.Count > 0)
            {
                var scanner = server.Services.GetRequiredService<WorkspaceScanner>();
                _ = Task.Run(() => scanner.ScanAsync(folders, CancellationToken.None), CancellationToken.None);
            }

            await Task.CompletedTask;
        });
}
