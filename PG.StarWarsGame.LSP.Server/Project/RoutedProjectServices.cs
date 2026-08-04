// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Dialog;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     The <see cref="IStoryDialogScope" /> injected at the LSP boundary. Scope is a per-project
///     question - a .txt is a dialog script for the project whose <c>storyDialog</c> directories
///     contain it - and every method already carries the file URI, so it routes without the call
///     sites knowing projects exist.
/// </summary>
public sealed class RoutedStoryDialogScope : IStoryDialogScope
{
    private readonly IProjectRegistry _registry;

    public RoutedStoryDialogScope(IProjectRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Active when any project has the dialog language enabled and declares dialog roots.</summary>
    public bool Enabled => _registry.All.Any(w => w.Service<IStoryDialogScope>().Enabled);

    public string? ResolveDialogFile(string dialogName)
    {
        // Highest-layer-wins within a project; across projects the first that resolves it wins,
        // matching how a name is resolved to exactly one file by the engine.
        foreach (var workspace in _registry.All)
            if (workspace.Service<IStoryDialogScope>().ResolveDialogFile(dialogName) is { } uri)
                return uri;
        return null;
    }

    public bool IsInScope(string fileUri)
    {
        // Any owner claiming it is enough: the file is a dialog script in that project's terms.
        foreach (var workspace in Owners(fileUri))
            if (workspace.Service<IStoryDialogScope>().IsInScope(fileUri))
                return true;
        return false;
    }

    public IReadOnlyCollection<int> GetChapters(string fileUri)
    {
        foreach (var workspace in Owners(fileUri))
        {
            var chapters = workspace.Service<IStoryDialogScope>().GetChapters(fileUri);
            if (chapters.Count > 0) return chapters;
        }

        return [];
    }

    private IReadOnlyList<ProjectWorkspace> Owners(string fileUri)
    {
        var owners = _registry.Resolve(fileUri);
        return owners.Count > 0 ? owners : [_registry.Primary];
    }
}

/// <summary>
///     The <see cref="IGlobalSuppressionStore" /> injected at the LSP boundary. Suppressions live in
///     a project's own <c>.aetswg/suppressions.json</c>, so a document is filtered by the rules of
///     the project that owns it - a suppression added in one mod must not silence another's
///     diagnostics.
/// </summary>
public sealed class RoutedGlobalSuppressionStore : IGlobalSuppressionStore
{
    private readonly IProjectRegistry _registry;

    public RoutedGlobalSuppressionStore(IProjectRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    ///     The union across projects. Reads have no file context of their own - the diagnostics
    ///     pipeline asks once per publish run - and a matcher is a diagnostic id, so unioning can
    ///     only suppress something a project already chose to suppress somewhere. Writes are what
    ///     must not leak, and they do not: they land in the project owning the file.
    /// </summary>
    public IReadOnlyList<SuppressionMatcher> GetAll()
    {
        var all = _registry.All;
        if (all.Count == 1) return all[0].Service<IGlobalSuppressionStore>().GetAll();

        var merged = new List<SuppressionMatcher>();
        foreach (var workspace in all)
            merged.AddRange(workspace.Service<IGlobalSuppressionStore>().GetAll());
        return merged;
    }

    public void Add(SuppressionMatcher matcher, string? reason = null)
    {
        _registry.Primary.Service<IGlobalSuppressionStore>().Add(matcher, reason);
    }

    /// <summary>
    ///     Adds to the project owning <paramref name="fileUri" /> - the document the quick fix was
    ///     invoked from. Suppressing in one mod must not silence another's diagnostics.
    /// </summary>
    public void AddFor(string fileUri, SuppressionMatcher matcher, string? reason = null)
    {
        _registry.ResolvePrimary(fileUri).Service<IGlobalSuppressionStore>().Add(matcher, reason);
    }

    public void Remove(SuppressionMatcher matcher)
    {
        foreach (var workspace in _registry.All)
            workspace.Service<IGlobalSuppressionStore>().Remove(matcher);
    }
}

/// <summary>
///     The <see cref="ILocalisationProjectRegistry" /> injected at the LSP boundary. Each project
///     declares its own localisation projects; the union is exposed because the localisation
///     navigator shows the whole window, with each entry already carrying its own paths.
/// </summary>
public sealed class RoutedLocalisationProjectRegistry : ILocalisationProjectRegistry
{
    private readonly IProjectRegistry _registry;

    public RoutedLocalisationProjectRegistry(IProjectRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<LocProjectInfo> Projects
    {
        get
        {
            var all = _registry.All;
            if (all.Count == 1) return all[0].Service<ILocalisationProjectRegistry>().Projects;

            var merged = new List<LocProjectInfo>();
            foreach (var workspace in all)
                merged.AddRange(workspace.Service<ILocalisationProjectRegistry>().Projects);
            return merged;
        }
    }
}

/// <summary>
///     The <see cref="ILocalisationLayerRegistry" /> injected at the LSP boundary. Layers are
///     per-project (ranks only mean something within one project), unioned for the window-wide views.
/// </summary>
public sealed class RoutedLocalisationLayerRegistry : ILocalisationLayerRegistry
{
    private readonly IProjectRegistry _registry;

    public RoutedLocalisationLayerRegistry(IProjectRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<LocalisationLayerEntry> Layers
    {
        get
        {
            var all = _registry.All;
            if (all.Count == 1) return all[0].Service<ILocalisationLayerRegistry>().Layers;

            var merged = new List<LocalisationLayerEntry>();
            foreach (var workspace in all)
                merged.AddRange(workspace.Service<ILocalisationLayerRegistry>().Layers);
            return merged;
        }
    }
}

/// <summary>
///     The <see cref="IStoryLayoutStore" /> injected at the LSP boundary. Layouts are keyed by
///     campaign, and a campaign belongs to exactly one project (campaign names are a single global
///     namespace in the engine), so the campaign selects the project whose sidecar is read or written.
/// </summary>
public sealed class RoutedStoryLayoutStore : IStoryLayoutStore
{
    private readonly IProjectRegistry _registry;

    public RoutedStoryLayoutStore(IProjectRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<StoryLayoutEntry> Get(string campaign)
    {
        // Read from whichever project already has positions for this campaign; a project that has
        // never seen it returns nothing, so the first non-empty answer is the owning one.
        foreach (var workspace in _registry.All)
        {
            var entries = workspace.Service<IStoryLayoutStore>().Get(campaign);
            if (entries.Count > 0) return entries;
        }

        return [];
    }

    public void Set(string campaign, IReadOnlyList<StoryLayoutEntry> entries)
    {
        OwnerOf(campaign).Service<IStoryLayoutStore>().Set(campaign, entries);
    }

    // The project that already stores this campaign, else the primary one. Writing to the project
    // that owns the campaign is what keeps one mod's layouts out of another's sidecar.
    private ProjectWorkspace OwnerOf(string campaign)
    {
        foreach (var workspace in _registry.All)
            if (workspace.Service<IStoryLayoutStore>().Get(campaign).Count > 0)
                return workspace;
        return _registry.Primary;
    }
}

/// <summary>
///     Resolves the per-project settings store for a request. Settings persist in each project's own
///     <c>.aetswg/settings/</c>, so a preference toggled while working in one mod must not be written
///     into another's file - the request carries a context URI to say which project it means.
/// </summary>
public sealed class RoutedWorkspaceSettingsStore : IProjectScopedWorkspaceSettings
{
    private readonly IProjectRegistry _registry;

    public RoutedWorkspaceSettingsStore(IProjectRegistry registry)
    {
        _registry = registry;
    }

    public IWorkspaceSettingsStore For(string? contextUri, string? campaign = null)
    {
        return Target(contextUri, campaign).Service<IWorkspaceSettingsStore>();
    }

    private ProjectWorkspace Target(string? contextUri, string? campaign)
    {
        // A campaign names exactly one project, so it is the more specific discriminator and wins.
        if (campaign is { Length: > 0 })
            foreach (var workspace in _registry.All)
                if (workspace.Service<IStoryModelService>().GetCampaignNames()
                    .Contains(campaign, StringComparer.OrdinalIgnoreCase))
                    return workspace;

        if (contextUri is { Length: > 0 })
            return _registry.ResolvePrimary(contextUri);

        // Neither given: the primary project, which is exactly the single-project case.
        return _registry.Primary;
    }
}

/// <summary>
///     The <see cref="IStoryModelService" /> injected at the LSP boundary. Each project models only
///     its own campaigns, so this presents the window-wide view by asking each project in turn -
///     never by letting one project's chain scan read another's files.
/// </summary>
public sealed class RoutedStoryModelService : IStoryModelService
{
    private readonly IProjectRegistry _registry;

    public RoutedStoryModelService(IProjectRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<string> GetCampaignNames()
    {
        var all = _registry.All;
        if (all.Count == 1) return all[0].Service<IStoryModelService>().GetCampaignNames();

        // Campaign names are one global namespace in the engine, so a duplicate across projects
        // means the mods conflict in game too - de-duplicated rather than shown twice.
        var names = new List<string>();
        foreach (var workspace in all)
            names.AddRange(workspace.Service<IStoryModelService>().GetCampaignNames());
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public StoryCampaignModel? GetCampaignModel(string campaignName)
    {
        foreach (var workspace in _registry.All)
            if (workspace.Service<IStoryModelService>().GetCampaignModel(campaignName) is { } model)
                return model;
        return null;
    }

    public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
    {
        // Only the projects owning the file are asked: another project cannot contain it.
        var owners = _registry.Resolve(canonicalUri);
        if (owners.Count == 0) owners = [_registry.Primary];

        var models = new List<StoryCampaignModel>();
        foreach (var workspace in owners)
            models.AddRange(workspace.Service<IStoryModelService>().GetModelsContaining(canonicalUri));
        return models;
    }

    public StoryChainScanResult GetChainResult()
    {
        // Chain results are per project and not mergeable (they carry per-project file associations);
        // the primary project's is returned, which is the single-project answer unchanged.
        return _registry.Primary.Service<IStoryModelService>().GetChainResult();
    }

    public IReadOnlyList<string> GetInvalidatedCampaigns()
    {
        var invalidated = new List<string>();
        foreach (var workspace in _registry.All)
            invalidated.AddRange(workspace.Service<IStoryModelService>().GetInvalidatedCampaigns());
        return invalidated.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
