// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class GetRootLocalisationConfigHandlerTest
{
    [Fact]
    public async Task Handle_RootLayerConfigured_ReturnsConfiguredTrueWithTypeAndDirectory()
    {
        var rootLayer = new ProjectLayer(0, "Root", [], [], ["/mod/data/text"], [], "Csv", "/mod/mymod.pgproj");
        var reload = new StubReloadService
        {
            LastWorkspaceConfig = WorkspaceConfiguration.Empty with { Layers = [rootLayer] }
        };
        var handler = new GetRootLocalisationConfigHandler(reload);

        var result = await handler.Handle(new GetRootLocalisationConfigParams(), CancellationToken.None);

        Assert.True(result.Configured);
        Assert.Equal("Csv", result.Type);
        Assert.Equal("/mod/data/text", result.Directory);
    }

    [Fact]
    public async Task Handle_RootLayerNotConfigured_ReturnsNotConfigured()
    {
        var rootLayer = new ProjectLayer(0, "Root", [], [], [], [], null, "/mod/mymod.pgproj");
        var reload = new StubReloadService
        {
            LastWorkspaceConfig = WorkspaceConfiguration.Empty with { Layers = [rootLayer] }
        };
        var handler = new GetRootLocalisationConfigHandler(reload);

        var result = await handler.Handle(new GetRootLocalisationConfigParams(), CancellationToken.None);

        Assert.False(result.Configured);
        Assert.Null(result.Type);
        Assert.Null(result.Directory);
    }

    [Fact]
    public async Task Handle_NoWorkspaceConfigYet_ReturnsNotConfigured()
    {
        var reload = new StubReloadService { LastWorkspaceConfig = null };
        var handler = new GetRootLocalisationConfigHandler(reload);

        var result = await handler.Handle(new GetRootLocalisationConfigParams(), CancellationToken.None);

        Assert.False(result.Configured);
    }

    [Fact]
    public async Task Handle_WithDependency_UsesHighestRankedLayer()
    {
        var depLayer = new ProjectLayer(0, "Dep", [], [], ["/dep/text"], [], "Xml", "/dep/dep.pgproj");
        var rootLayer = new ProjectLayer(1, "Root", [], [], ["/mod/data/text"], [], "Csv", "/mod/mymod.pgproj");
        var reload = new StubReloadService
        {
            LastWorkspaceConfig = WorkspaceConfiguration.Empty with { Layers = [depLayer, rootLayer] }
        };
        var handler = new GetRootLocalisationConfigHandler(reload);

        var result = await handler.Handle(new GetRootLocalisationConfigParams(), CancellationToken.None);

        Assert.Equal("Csv", result.Type);
        Assert.Equal("/mod/data/text", result.Directory);
    }

    private sealed class StubReloadService : IModProjectReloadService
    {
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; init; }
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
