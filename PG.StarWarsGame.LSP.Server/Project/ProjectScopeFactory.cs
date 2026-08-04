// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Suppression;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Builds one service provider per project. This is where multi-root separation is decided, and
///     it is deliberately the only place: a service is shared across projects if and only if it
///     appears in <see cref="Shared" />, and is per-project otherwise.
///     <para>
///         The default is therefore <em>fail-safe</em>. Adding a service to the server container does
///         not silently share it - a per-project consumer resolves it from here and gets its own
///         instance unless someone deliberately adds it to the shared list. That is the property the
///         old "one singleton per service, route it if you remember" arrangement did not have.
///     </para>
/// </summary>
public sealed class ProjectScopeFactory
{
    private readonly SharedServices _shared;

    public ProjectScopeFactory(SharedServices shared)
    {
        _shared = shared;
    }

    /// <summary>
    ///     The services every project shares. Each one is here because it is either immutable
    ///     base-game data (loaded once, identical for every mod - the reason this is one server and
    ///     not one per folder) or genuinely window-global (there is one editor, one set of open
    ///     buffers, one client connection).
    ///     <para>
    ///         <b>Adding a member here shares state across every open mod.</b> Do it only when the
    ///         state cannot differ per project, and say why in a comment.
    ///     </para>
    /// </summary>
    public sealed class SharedServices
    {
        /// <summary>Stateless path/URI helpers over one file system.</summary>
        public required IFileHelper FileHelper { get; init; }

        /// <summary>The game's tag/enum schema. Immutable once loaded, identical for every mod.</summary>
        public required ISchemaProvider Schema { get; init; }

        /// <summary>Session configuration (game paths, locale, feature flags) - window-wide by design.</summary>
        public required ILspConfigurationProvider Config { get; init; }

        /// <summary>Open editor buffers. One editor, one buffer per file, regardless of project.</summary>
        public required IDocumentTextSource TextSource { get; init; }

        /// <summary>Logging. No state of its own.</summary>
        public required ILoggerFactory LoggerFactory { get; init; }

        /// <summary>Client-facing notifications. There is one client connection.</summary>
        public required IUserNotifier Notifier { get; init; }
    }

    public IServiceProvider Create(ProjectWorkspace workspace)
    {
        var services = new ServiceCollection();

        // ── shared: the allowlist above, and nothing else ────────────────────
        services.AddSingleton(_shared.FileHelper);
        services.AddSingleton(_shared.Schema);
        services.AddSingleton(_shared.Config);
        services.AddSingleton(_shared.TextSource);
        services.AddSingleton(_shared.LoggerFactory);
        services.AddSingleton(_shared.Notifier);
        services.AddLogging();

        // ── this project ─────────────────────────────────────────────────────
        // The workspace is its own IProjectContext, so everything below persists under, and is
        // scoped by, this project's .pgproj rather than whichever project happened to load first.
        services.AddSingleton<IProjectContext>(workspace);
        services.AddSingleton(workspace);
        services.AddSingleton<IGameIndexService>(workspace.Index);
        services.AddSingleton<IEaWXmlContext>(workspace.XmlContext);
        services.AddSingleton<IProjectLayerMap>(workspace.LayerMap);
        services.AddSingleton<IFileTypeRegistry>(workspace.FileTypes);

        services.AddSingleton<IStoryLayoutStore, StoryLayoutStore>();
        services.AddSingleton<IWorkspaceSettingsStore, WorkspaceSettingsStore>();
        services.AddSingleton<IGlobalSuppressionStore, GlobalSuppressionStore>();
        services.AddSingleton<IStoryDialogScope, StoryDialogScopeService>();
        services.AddSingleton<IStoryModelService, StoryModelService>();
        services.AddSingleton<LocalisationProjectRegistry>();
        services.AddSingleton<ILocalisationProjectRegistry>(sp =>
            sp.GetRequiredService<LocalisationProjectRegistry>());
        services.AddSingleton<LocalisationLayerRegistry>();
        services.AddSingleton<ILocalisationLayerRegistry>(sp =>
            sp.GetRequiredService<LocalisationLayerRegistry>());

        return services.BuildServiceProvider();
    }
}
