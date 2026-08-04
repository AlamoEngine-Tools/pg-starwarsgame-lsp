// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The <see cref="IGameIndexService" /> the handlers inject. Each project owns a real
///     <see cref="GameIndexService" />; this routes each call to the right one (or ones):
///     <list type="bullet">
///         <item>document operations go to every project that owns the file - a shared dependency is
///         indexed into each closure that contains it, because it can be valid in one and broken in
///         another;</item>
///         <item>base-game applies (baseline, localisation, assets, bones, dynamic enums) fan out to
///         every project - that data is identical everywhere and is the reason this is one server
///         rather than one per folder;</item>
///         <item>reads answer from the project owning the file in the request.</item>
///     </list>
/// </summary>
public sealed class RoutedGameIndexService : IGameIndexService
{
    private readonly object _gate = new();
    private readonly IProjectRegistry _registry;

    // The workspaces this instance currently has event handlers attached to. Tracked explicitly so
    // a project set replacement detaches from the old ones instead of leaking subscriptions.
    private IReadOnlyList<ProjectWorkspace> _attached = [];

    public RoutedGameIndexService(IProjectRegistry registry)
    {
        _registry = registry;
        Attach(registry.All);
        registry.ProjectsChanged += Attach;
    }

    /// <summary>
    ///     The primary project's index. Correct for whole-session questions and for any single-project
    ///     workspace; a call site that has a file in hand should use <see cref="For" /> instead.
    /// </summary>
    public GameIndex Current => _registry.Primary.Index.Current;

    public event Action<GameIndex>? IndexChanged;
    public event Action<ILocalisationIndex>? LocalisationChanged;
    public event Action<GameIndex>? DynamicEnumChanged;

    public GameIndex For(string uri)
    {
        return _registry.ResolvePrimary(uri).Index.Current;
    }

    public IReadOnlyList<GameIndex> AllIndices
    {
        get { return _registry.All.Select(w => w.Index.Current).ToList(); }
    }

    public async Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        foreach (var workspace in Owners(uri))
            await workspace.Index.UpdateDocumentAsync(uri, text, version, ct).ConfigureAwait(false);
    }

    public async Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        foreach (var workspace in Owners(uri))
            await workspace.Index.OpenDocumentAsync(uri, text, version, ct).ConfigureAwait(false);
    }

    public void InjectDocument(DocumentIndex document)
    {
        foreach (var workspace in Owners(document.DocumentUri))
            workspace.Index.InjectDocument(document);
    }

    public void RemoveDocument(string uri)
    {
        // Removed everywhere rather than from the owners only: a file that has just been deleted no
        // longer routes to the directories it used to live under.
        foreach (var workspace in _registry.All)
            workspace.Index.RemoveDocument(uri);
    }

    public void ApplyBaseline(BaselineIndex baseline)
    {
        foreach (var workspace in _registry.All)
            workspace.Index.ApplyBaseline(baseline);
    }

    public void ApplyLocalisation(ILocalisationIndex index)
    {
        foreach (var workspace in _registry.All)
            workspace.Index.ApplyLocalisation(index);
    }

    public void ApplyAssetFiles(IAssetFileIndex index)
    {
        foreach (var workspace in _registry.All)
            workspace.Index.ApplyAssetFiles(index);
    }

    public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
    {
        foreach (var workspace in _registry.All)
            workspace.Index.ApplyModelBones(bones);
    }

    public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
    {
        foreach (var workspace in _registry.All)
            workspace.Index.ApplyWorkspaceDynamicEnumValues(values);
    }

    public void ApplyWorkspaceEnumValueDefinitions(
        ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
    {
        foreach (var workspace in _registry.All)
            workspace.Index.ApplyWorkspaceEnumValueDefinitions(definitions);
    }

    /// <summary>
    ///     Opens a bulk scope on every project that exists now. A project created while the scope is
    ///     open is not covered by it - the workspace scan sets the project set up front, so that does
    ///     not arise in practice, and the cost of missing it is only extra diagnostic republishing.
    /// </summary>
    public IDisposable BeginBulkUpdate()
    {
        var scopes = _registry.All.Select(w => w.Index.BeginBulkUpdate()).ToList();
        return new CompositeScope(scopes);
    }

    private IReadOnlyList<ProjectWorkspace> Owners(string uri)
    {
        var owners = _registry.Resolve(uri);
        // A file outside every project still has to land somewhere, or an ad-hoc opened document
        // would silently lose every language feature. Matches the routing fallback elsewhere.
        return owners.Count > 0 ? owners : [_registry.Primary];
    }

    private void Attach(IReadOnlyList<ProjectWorkspace> workspaces)
    {
        lock (_gate)
        {
            foreach (var workspace in _attached)
            {
                workspace.Index.IndexChanged -= OnIndexChanged;
                workspace.Index.LocalisationChanged -= OnLocalisationChanged;
                workspace.Index.DynamicEnumChanged -= OnDynamicEnumChanged;
            }

            foreach (var workspace in workspaces)
            {
                workspace.Index.IndexChanged += OnIndexChanged;
                workspace.Index.LocalisationChanged += OnLocalisationChanged;
                workspace.Index.DynamicEnumChanged += OnDynamicEnumChanged;
            }

            _attached = workspaces;
        }
    }

    private void OnIndexChanged(GameIndex index)
    {
        IndexChanged?.Invoke(index);
    }

    private void OnLocalisationChanged(ILocalisationIndex index)
    {
        LocalisationChanged?.Invoke(index);
    }

    private void OnDynamicEnumChanged(GameIndex index)
    {
        DynamicEnumChanged?.Invoke(index);
    }

    private sealed class CompositeScope : IDisposable
    {
        private readonly IReadOnlyList<IDisposable> _scopes;

        public CompositeScope(IReadOnlyList<IDisposable> scopes)
        {
            _scopes = scopes;
        }

        public void Dispose()
        {
            foreach (var scope in _scopes)
                scope.Dispose();
        }
    }
}
