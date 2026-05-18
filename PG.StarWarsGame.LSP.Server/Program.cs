using System.Diagnostics;
using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Server;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Parsing;

if (args.Contains("--wait-for-debugger") || Environment.GetEnvironmentVariable("LSP_WAIT_DEBUGGER") == "1")
{
    Console.Error.WriteLine($"[LSP] Waiting for debugger — PID {Environment.ProcessId}");
    while (!Debugger.IsAttached)
        Thread.Sleep(100);
    Console.Error.WriteLine("[LSP] Debugger attached, continuing startup.");
}

var server = await LanguageServer.From(options => options
    .WithInput(Console.OpenStandardInput())
    .WithOutput(Console.OpenStandardOutput())
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

        // Schema layer — switched to local at runtime if configured
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

        // Index architecture
        services.AddSingleton<IGameWorkspaceHost, GameWorkspaceHost>();
        services.AddSingleton<IGameDocumentParser, XmlGameDocumentParser>();
        services.AddSingleton<IGameIndexService, GameIndexService>();
        services.AddSingleton<WorkspaceScanner>();

        services.AddHttpClient(nameof(HttpSchemaProvider));

        services.AddXmlLanguageServices();
    })
    .OnInitialize(async (server, request, ct) =>
    {
        var logger = server.Services.GetRequiredService<ILogger<LspConfigurationProvider>>();
        var configProvider = server.Services.GetRequiredService<ILspConfigurationProvider>();
        configProvider.LoadFrom(request.InitializationOptions);
        logger.LogInformation("Loaded configuration: {@Config}", configProvider.Current);

        // Load schema in background — non-blocking
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
        // Ensure the diagnostics publisher is constructed and subscribed to IndexChanged.
        server.Services.GetRequiredService<XmlDiagnosticsPublisher>();

        // Apply empty baseline — Phase G will replace this with snapshot deserialization.
        var indexService = server.Services.GetRequiredService<IGameIndexService>();
        indexService.ApplyBaseline(BaselineIndex.Empty);

        // Scan workspace folders in the background — non-blocking.
        var folders = request.WorkspaceFolders?.Select(f => f.Uri.GetFileSystemPath()).ToList()
                      ?? (request.RootUri is not null ? [request.RootUri.GetFileSystemPath()] : []);
        if (folders.Count > 0)
        {
            var scanner = server.Services.GetRequiredService<WorkspaceScanner>();
            _ = Task.Run(() => scanner.ScanAsync(folders, CancellationToken.None), CancellationToken.None);
        }

        await Task.CompletedTask;
    })
);

await server.WaitForExit;