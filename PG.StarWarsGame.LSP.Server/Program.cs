using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.LSP.Assets;
using PG.StarWarsGame.LSP.Assets.Baseline;
using PG.StarWarsGame.LSP.Assets.Cache;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Server;
using PG.StarWarsGame.LSP.Xml;

var server = await LanguageServer.From(options => options
    .WithInput(Console.OpenStandardInput())
    .WithOutput(Console.OpenStandardOutput())
    .ConfigureLogging(x => x
        .AddLanguageProtocolLogging()
        .SetMinimumLevel(LogLevel.Debug))
    .WithHandler<XmlTextDocumentSyncHandler>()
    .WithHandler<XmlHoverHandler>()
    .WithHandler<XmlCompletionHandler>()
    .WithServices(services =>
    {
        services.AddSingleton<ILspConfigurationProvider, LspConfigurationProvider>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<SchemaHttpCache>();
        services.AddSingleton<BaselineHttpCache>();

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

        // Symbol index
        services.AddSingleton<SymbolIndex>();
        services.AddSingleton<ISymbolIndex>(sp => sp.GetRequiredService<SymbolIndex>());

        // Baseline providers
        services.AddSingleton<IBaselineProvider>(sp =>
        {
            var config = sp.GetRequiredService<ILspConfigurationProvider>();
            return config.Current.BaselineSource.Type switch
            {
                BaselineSourceType.Local => new LocalBaselineProvider(config,
                    sp.GetRequiredService<ILogger<LocalBaselineProvider>>()),
                BaselineSourceType.None => new NullBaselineProvider(),
                _ => new HttpBaselineProvider(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpBaselineProvider)),
                    config,
                    sp.GetRequiredService<BaselineHttpCache>(),
                    sp.GetRequiredService<ILogger<HttpBaselineProvider>>())
            };
        });
        services.AddSingleton<BaselinePopulator>();
        services.AddSingleton<BaselineDataService>();

        services.AddHttpClient(nameof(HttpSchemaProvider));
        services.AddHttpClient(nameof(HttpBaselineProvider));

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

        await Task.CompletedTask;
    })
    .OnInitialized(async (server, request, response, ct) =>
    {
        _ = Task.Run(async () =>
        {
            var logger = server.Services.GetRequiredService<ILogger<BaselineDataService>>();
            try
            {
                var config = server.Services.GetRequiredService<ILspConfigurationProvider>().Current;
                var populator = server.Services.GetRequiredService<BaselinePopulator>();
                var baselineProvider = server.Services.GetRequiredService<IBaselineProvider>();

                var baseline = await baselineProvider.LoadAsync(CancellationToken.None);
                if (baseline is not null)
                {
                    populator.PopulateFromBaseline(
                        server.Services.GetRequiredService<ISymbolIndex>(), baseline);
                }
                else if (config.GamePath is not null)
                {
                    var dataService = server.Services.GetRequiredService<BaselineDataService>();
                    await dataService.InitializeAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Baseline initialisation failed");
            }
        });
    })
);

await server.WaitForExit;