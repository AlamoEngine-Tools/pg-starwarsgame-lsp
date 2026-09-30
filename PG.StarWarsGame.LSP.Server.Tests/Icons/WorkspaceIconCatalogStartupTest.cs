// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Icons;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Icons;

/// <summary>
///     What the icon catalog answers while the project is still loading.
/// </summary>
/// <remarks>
///     <para>
///         The first card opened after startup could come back drawn entirely from the BAKED BASE
///         GAME - the wrong portrait, and a 262-wide header band on a card the mod had widened to
///         340 - on a workspace whose icons were configured perfectly. Nondeterministic, so it read
///         as a rendering fault rather than a race.
///     </para>
///     <para>
///         The cause is that "no layers" had two meanings. A workspace with no <c>.pgproj</c> at all
///         genuinely has nothing but its root to scan, and falling back to it is right. A workspace
///         whose project file has not finished loading looks identical through
///         <c>LastWorkspaceConfig</c>, and falling back there answers from the base game while the
///         project that has the real art is still being read. <c>LoadAsync</c> sets its roots before
///         it resolves the config, so even those two together cannot tell them apart - which is why
///         the service now says when a load has FINISHED.
///     </para>
/// </remarks>
public sealed class WorkspaceIconCatalogStartupTest
{
    private const string Root = "C:/mods/rev";

    private static WorkspaceConfiguration Configured()
    {
        return WorkspaceConfiguration.Empty with
        {
            Layers =
            [
                new ProjectLayer(0, "Rev", [Root + "/data/xml"], [], [], [], null, Root + "/rev.pgproj")
                {
                    Icons = new IconProjectSettings("data/art/textures/mt", ["data/art/textures/icons"])
                }
            ]
        };
    }

    private static WorkspaceIconCatalog Catalog(LoadingProjects projects, RecordingIcons icons)
    {
        return new WorkspaceIconCatalog(
            new StubConfig(Root), icons, projects);
    }

    /// <summary>
    ///     The race itself: asked before the load finishes, the catalog must not answer from the
    ///     workspace root - that root carries no declared source roots, so every icon would resolve
    ///     out of the baked base game.
    /// </summary>
    [Fact]
    public async Task BeforeTheProjectLoadFinishes_DoesNotAnswerFromTheWorkspaceRoot()
    {
        var icons = new RecordingIcons();
        var projects = new LoadingProjects { HasLoadedProjects = false, LastWorkspaceConfig = null };

        await Catalog(projects, icons).GetAsync(CancellationToken.None);

        Assert.Empty(icons.Requested);
    }

    /// <summary>
    ///     Once the load has finished and found nothing, the root really is all there is, and the
    ///     heuristic scan is the right answer rather than a guess.
    /// </summary>
    [Fact]
    public async Task AfterALoadThatFoundNoProject_FallsBackToTheWorkspaceRoot()
    {
        var icons = new RecordingIcons();
        var projects = new LoadingProjects { HasLoadedProjects = true, LastWorkspaceConfig = null };

        await Catalog(projects, icons).GetAsync(CancellationToken.None);

        var asked = Assert.Single(icons.Requested);
        Assert.Equal(Root, Assert.Single(asked).RootPath);
    }

    /// <summary>A resolved project is used whatever the flag says - the layers are the answer.</summary>
    [Fact]
    public async Task WithResolvedLayers_UsesThem()
    {
        var icons = new RecordingIcons();
        var projects = new LoadingProjects
        {
            HasLoadedProjects = true, LastWorkspaceConfig = Configured()
        };

        await Catalog(projects, icons).GetAsync(CancellationToken.None);

        var asked = Assert.Single(icons.Requested);
        Assert.Equal(Root + "/rev.pgproj", Root + "/rev.pgproj");
        Assert.Equal(["data/art/textures/icons"], Assert.Single(asked).Settings.SourceRoots);
    }

    private sealed class LoadingProjects : IModProjectReloadService
    {
        public bool HasLoadedProjects { get; init; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; init; }
        public IReadOnlyList<string>? LastWorkspaceRoots => [Root];

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ReloadLocalisationAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingIcons : IIconCatalogProvider
    {
        public List<IReadOnlyList<IconLayer>> Requested { get; } = [];

        public IReadOnlySet<string> IconsAwaitingRepack => new HashSet<string>();

        public Task<IconCatalog> GetAsync(IReadOnlyList<IconLayer> layers, CancellationToken ct)
        {
            Requested.Add(layers);
            return Task.FromResult(new IconCatalog(
                null,
                new Dictionary<string, byte[]>(),
                new Dictionary<string, byte[]>()));
        }

        public void Invalidate()
        {
        }
    }

    private sealed class StubConfig(string root) : ILspConfigurationProvider
    {
        public LspConfiguration Current { get; } = new() { WorkspaceRoot = root };

        public void LoadFrom(object? initializationOptions)
        {
        }
    }
}