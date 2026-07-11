// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using PG.StarWarsGame.LSP.Schema;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Server.Caching;
using PG.StarWarsGame.LSP.Server.Commands;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Variants;
using PG.StarWarsGame.LSP.Story.Dialog;
using PG.StarWarsGame.LSP.Story.Dialog.Handlers;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Commands;
using PG.StarWarsGame.LSP.Xml.Parsing;
using Serilog;
using CoreServerOptions = PG.StarWarsGame.LSP.Core.Configuration.ServerOptions;

namespace PG.StarWarsGame.LSP.Server;

public static class ServerConfigurator
{
    public static LanguageServerOptions Apply(LanguageServerOptions options,
        CoreServerOptions? serverOptions = null)
    {
        options.ServerInfo = new ServerInfo
        {
            Name = "PG.StarWarsGame.LSP",
            // InformationalVersion carries SemVer build metadata ("+<gitHash>") appended by the
            // SDK. Strip it so the client version check gets a clean "major.minor.patch" string.
            Version = (typeof(ServerConfigurator).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown").Split('+')[0]
        };

        return options
            .ConfigureLogging(x => x
                .SetMinimumLevel(LogLevel.Information)
                .AddLanguageProtocolLogging()
#if DEBUG
                .AddSerilog(dispose: true)
#endif
                )
            // ── Two wiring conventions coexist below, deliberately ──────────────────
            // Most capabilities (Sync, Completion, Definition, CodeAction, CodeLens,
            // InlayHint) are "dual-registered": the Xml* and Lua* handler for a capability
            // are both passed to .WithHandler<>(), and OmniSharp picks the right one per
            // request using each handler's own DocumentSelector (ForLanguage("xml"/"lua")).
            //
            // Hover and Rename instead go through a single Game*Handler "router" (below)
            // that holds both language providers and dispatches by file extension; the
            // providers are registered as interfaces only (see WithServices) so DryIoc never
            // sees two competing IHoverHandler/IRenameHandler registrations.
            //
            // These are not meant to converge. The router shape exists only because hover
            // registration specifically produced a DryIoc conflict historically. An E2E test
            // (DualRegistrationRoutingSmokeTest, added 2026-07-02) opens an XML and a Lua
            // document in the same live server and proves Completion/Definition route to the
            // correct language handler with no crosstalk under dual-registration — so that
            // convention is equally safe for the capabilities that use it. If either
            // convention needs to change, extend that test first as the regression gate.
            .WithHandler<LuaTextDocumentSyncHandler>()
            .WithHandler<DialogTextDocumentSyncHandler>()
            .WithHandler<LuaCompletionHandler>()
            .WithHandler<LuaCodeActionHandler>()
            .WithHandler<LuaDefinitionHandler>()
            .WithHandler<LuaCodeLensHandler>()
            .WithHandler<XmlTextDocumentSyncHandler>()
            .WithHandler<GameHoverHandler>()
            .WithHandler<XmlCompletionHandler>()
            .WithHandler<XmlDefinitionHandler>()
            .WithHandler<XmlReferencesHandler>()
            .WithHandler<GameRenameHandler>()
            .WithHandler<GamePrepareRenameHandler>()
            .WithHandler<GameDidChangeWatchedFilesHandler>()
            .WithHandler<XmlCodeActionHandler>()
            .WithHandler<XmlCodeLensHandler>()
            .WithHandler<XmlLinkedEditingRangeHandler>()
            .WithHandler<XmlInlayHintHandler>()
            .WithHandler<LuaInlayHintHandler>()
            .WithHandler<RevalidateWorkspaceCommandHandler>()
            .WithHandler<RevalidateDocumentCommandHandler>()
            .WithHandler<ReloadProjectCommandHandler>()
            .WithHandler<NewModProjectCommandHandler>()
            .WithHandler<InitLocalisationProjectCommandHandler>()
            .WithHandler<ImportLocalisationProjectCommandHandler>()
            .WithHandler<CreateLocalisationKeyCommandHandler>()
            .WithHandler<GetLocalisationProjectsHandler>()
            .WithHandler<GetRootLocalisationConfigHandler>()
            .WithHandler<GetBaselineEntriesHandler>()
            .WithHandler<GetLanguagesHandler>()
            .WithHandler<ExportLocalisationToDatHandler>()
            .WithHandler<GetLocalisationEntriesHandler>()
            .WithHandler<SetLocalisationEntryHandler>()
            .WithHandler<DeleteLocalisationEntryHandler>()
            .WithHandler<AddLocalisationLanguageHandler>()
            .WithHandler<GetEffectiveObjectHandler>()
            .WithServices(services =>
            {
                services.AddSingleton(serverOptions ?? CoreServerOptions.Default);
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
                services.AddSingleton<IProjectLayerMap, ProjectLayerMap>();

                services.AddSingleton<IGameWorkspaceHost, GameWorkspaceHost>();
                services.AddSingleton<IDocumentTextSource, DocumentTextSource>();
                services.AddSingleton<IGameDocumentParser, XmlGameDocumentParser>();
                services.AddSingleton<IGameIndexService, GameIndexService>();
                services.AddSingleton<IFileTypeRegistry, FileTypeRegistry>();
                services.AddSingleton<IStoryChainProblemStore, StoryChainProblemStore>();

                // Story campaign models (per-campaign threads + graph) and their diagnostics.
                services.AddSingleton<IStoryModelService, StoryModelService>();
                services.AddSingleton<IStoryGraphDiagnosticsSource, StoryGraphDiagnosticsService>();

                // Story-dialog (.txt) language service, scoped by the pgproj storyDialog node.
                services.AddSingleton<IStoryDialogScope, StoryDialogScopeService>();
                services.AddSingleton<DialogFactProducer>();
                services.AddSingleton<IDialogDiagnosticsHandler, UnknownDialogCommandHandler>();
                services.AddSingleton<IDialogDiagnosticsHandler, DialogCommandArityHandler>();
                services.AddSingleton<IDialogDiagnosticsHandler, DialogArgValueHandler>();
                services.AddSingleton<IDialogDiagnosticsHandler, UntestedDialogCommandHandler>();
                services.AddSingleton<IDialogDiagnosticsHandler, DialogArgReferenceHandler>();
                services.AddSingleton<DialogDiagnosticsHandlerRegistry>();
                services.AddSingleton<DialogDiagnosticsPublisher>();
                services.AddSingleton<IDialogDiagnosticsRevalidator>(sp =>
                    sp.GetRequiredService<DialogDiagnosticsPublisher>());

                // The inbound event gate: buffers client notifications while the linear startup
                // pipeline runs, then drains them in order. Replaces the old PreOpenBuffer race.
                services.AddSingleton<IStartupGate, StartupGate>();

                services.AddSingleton<IProjectIndexCache, ProjectIndexCache>();
                services.AddSingleton<WorkspaceIndexer>();
                services.AddSingleton<IWorkspaceIndexer>(sp => sp.GetRequiredService<WorkspaceIndexer>());

                services.AddSingleton<ModProjectLoader>();
                services.AddSingleton<ProjectDependencyGraph>();
                services.AddSingleton<ModProjectResolver>();
                services.AddSingleton<IModProjectDetector, ModProjectDetector>();
                services.AddSingleton<IProjectConfigurationResolver, ProjectConfigurationResolver>();
                services.AddSingleton<IModProjectReloadService, ModProjectReloadService>();
                services.AddSingleton<IModProjectFileWriter, ModProjectFileWriter>();
                services.AddSingleton<ILocalisationSeedFileWriter, LocalisationSeedFileWriter>();
                services.AddSingleton<ILocalisationEntryWriter, LocalisationEntryWriter>();

                // Linear startup pipeline and its stage collaborators.
                services.AddSingleton<ISchemaBootstrapper, SchemaBootstrapper>();
                services.AddSingleton<IBaselineBootstrapper, BaselineBootstrapper>();
                services.AddSingleton<IStartupProgress, StartupProgress>();
                services.AddSingleton<IStartupNotifier, StartupNotifier>();
                services.AddSingleton<IUserNotifier, WindowUserNotifier>();
                services.AddSingleton<StartupPipeline>();

                services.AddSingleton<BaselineLoader>(sp =>
                    new BaselineLoader(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BaselineLoader)),
                        sp.GetRequiredService<IFileHelper>(),
                        sp.GetRequiredService<ILogger<BaselineLoader>>()));

                services.AddHttpClient(nameof(HttpSchemaProvider));
                services.AddHttpClient(nameof(BaselineLoader));
                services.AddHttpClient("LuaSchema");

                services.AddLuaLanguageServices();
                services.AddXmlLanguageServices();
                services.SupportLocalisationBaseline();
                services.AddSingleton<LocalisationProjectRegistry>();
                services.AddSingleton<ILocalisationProjectRegistry>(sp =>
                    sp.GetRequiredService<LocalisationProjectRegistry>());
                services.AddSingleton<LocalisationLayerRegistry>();
                services.AddSingleton<ILocalisationLayerRegistry>(sp =>
                    sp.GetRequiredService<LocalisationLayerRegistry>());
                services.AddSingleton<ILocalisationLoader, LocalisationLoader>();
                services.AddSingleton<LocalisationIndexChangedNotifier>(sp =>
                    new LocalisationIndexChangedNotifier(
                        sp.GetRequiredService<IGameIndexService>(),
                        method => sp.GetRequiredService<ILanguageServerFacade>().SendNotification(method),
                        sp.GetRequiredService<ILogger<LocalisationIndexChangedNotifier>>()));

                // GameHoverHandler routes by extension to one of these providers. Registered as
                // interfaces only so DryIoc does not see them as competing IHoverHandler
                // registrations — see the router-vs-dual-registration note above .WithHandler<>().
                services.AddSingleton<IXmlHoverProvider, XmlHoverHandler>();
                services.AddSingleton<ILuaHoverProvider, LuaHoverHandler>();

                // GameRenameHandler and GamePrepareRenameHandler route by extension to these
                // providers. Same router pattern as hover, for the same reason.
                services.AddSingleton<IXmlRenameProvider, XmlRenameHandler>();
                services.AddSingleton<ILuaRenameProvider, LuaRenameHandler>();
            })
            .OnInitialize(async (server, request, ct) =>
            {
                // Keep this cheap: just load configuration and respond with capabilities so the
                // client sees the server "ready" immediately. All heavy work (schema, baseline,
                // indexing) runs in the StartupPipeline launched from OnInitialized, while the
                // StartupGate buffers any client notifications that arrive in the meantime.
                var logger = server.Services.GetRequiredService<ILogger<LspConfigurationProvider>>();

                logger.LogInformation("[initialize] RootUri={RootUri} RootPath={RootPath}",
                    request.RootUri, request.RootPath);
                logger.LogInformation("[initialize] WorkspaceFolders={Folders}",
                    request.WorkspaceFolders is null
                        ? "<null>"
                        : string.Join(", ", request.WorkspaceFolders.Select(f => f.Uri.ToString())));
                logger.LogInformation("[initialize] InitializationOptions={Options}",
                    JsonConvert.SerializeObject(request.InitializationOptions, Formatting.None));

                var configProvider = server.Services.GetRequiredService<ILspConfigurationProvider>();
                configProvider.LoadFrom(request.InitializationOptions);
                logger.LogInformation("Loaded configuration: {@Config}", configProvider.Current);

                await Task.CompletedTask;
            })
            .OnInitialized(async (server, request, response, ct) =>
            {
                var initLogger = server.Services.GetRequiredService<ILogger<LspConfigurationProvider>>();
                var config = server.Services.GetRequiredService<ILspConfigurationProvider>();

                var scanRoots = ComputeScanRoots(request, config.Current.WorkspaceRoot);

                // Log whether a .pgproj was found under the scan roots — useful startup breadcrumb only.
                // Not authoritative: the real resolution (with a user-facing notification on failure,
                // e.g. multiple .pgproj files found) happens moments later via
                // ProjectConfigurationResolver inside the Task.Run below, so any exception here
                // (detector.TryFind can throw) must not crash this handler before that ever runs.
                var detector = server.Services.GetRequiredService<IModProjectDetector>();
                var hasPgproj = false;
                string? pgprojPath = null;
                try
                {
                    hasPgproj = detector.TryFind(scanRoots, out pgprojPath);
                }
                catch (Exception ex)
                {
                    initLogger.LogWarning(ex, "Could not determine .pgproj presence for the startup breadcrumb log.");
                }

                initLogger.LogInformation(
                    "[initialized] scanRoots={Roots} | .pgproj present={Found} path={Path}",
                    string.Join(", ", scanRoots), hasPgproj, pgprojPath ?? "<none>");

                // Heavy startup runs on a background task while the StartupGate buffers any client
                // notifications that arrive in the meantime. The gate is opened (draining the
                // buffer in order) once the pipeline finishes, and the client is told the scan
                // completed. Diagnostics publishers are resolved eagerly so they subscribe to
                // IndexChanged before the index is populated.
                server.Services.GetRequiredService<XmlDiagnosticsPublisher>();
                server.Services.GetRequiredService<LuaDiagnosticsPublisher>();
                server.Services.GetRequiredService<LocalisationIndexChangedNotifier>();
                var schema = server.Services.GetRequiredService<ISchemaBootstrapper>();
                var baseline = server.Services.GetRequiredService<IBaselineBootstrapper>();
                var reload = server.Services.GetRequiredService<IModProjectReloadService>();
                var notifier = server.Services.GetRequiredService<IStartupNotifier>();
                var gate = server.Services.GetRequiredService<IStartupGate>();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await schema.LoadAsync(CancellationToken.None);
                        await baseline.LoadAsync(CancellationToken.None);
                        await reload.LoadAsync(scanRoots, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        initLogger.LogError(ex, "Startup pipeline failed");
                    }
                    finally
                    {
                        await gate.OpenAsync();
                        notifier.NotifyScanComplete();
                    }
                }, CancellationToken.None);

                await Task.CompletedTask;
            });
    }

    // Builds the scan roots: start from protocol-level workspace folders or RootUri, then always
    // add the configured workspaceRoot if not already covered. The extension sends
    // workspaceRoot = the game data directory, which may be a subdirectory of the VS Code
    // workspace root — so it must always be included, not used as a mere fallback.
    private static IReadOnlyList<string> ComputeScanRoots(InitializeParams request, string? configWorkspaceRoot)
    {
        var folders = new List<string>();

        var workspaceFolderPaths = request.WorkspaceFolders
            ?.Select(f => f.Uri.GetFileSystemPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (workspaceFolderPaths is { Count: > 0 })
        {
            folders.AddRange(workspaceFolderPaths!);
        }
        else if (request.RootUri is not null)
        {
            var rootPath = request.RootUri.GetFileSystemPath();
            if (!string.IsNullOrEmpty(rootPath))
                folders.Add(rootPath);
        }

        if (configWorkspaceRoot is not null &&
            !folders.Any(f => string.Equals(f, configWorkspaceRoot, StringComparison.OrdinalIgnoreCase)))
            folders.Add(configWorkspaceRoot);

        return folders;
    }
}