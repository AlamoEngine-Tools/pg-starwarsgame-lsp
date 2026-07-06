// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class GetLocalisationProjectsHandlerTest
{
    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LocalisationFlagOff_ReturnsEmptyProjects()
    {
        var registry = new LocalisationProjectRegistry();
        registry.Set([new LocProjectInfo("a.csv", "/mod/a.csv", "Csv", "Root", 0)]);
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var handler = new GetLocalisationProjectsHandler(registry, config);

        var result = await handler.Handle(new GetLocalisationProjectsParams(), CancellationToken.None);

        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task Handle_RegistryEmpty_ReturnsEmptyProjects()
    {
        var registry = new LocalisationProjectRegistry();
        var handler = new GetLocalisationProjectsHandler(registry, new FakeLspConfigurationProvider());

        var result = await handler.Handle(new GetLocalisationProjectsParams(), CancellationToken.None);

        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task Handle_RegistryHasProjects_ReturnsAllProjects()
    {
        var registry = new LocalisationProjectRegistry();
        registry.Set([
            new LocProjectInfo("a.csv", "/mod/a.csv", "Csv", "Root", 0),
            new LocProjectInfo("b.csv", "/mod/b.csv", "Csv", "Root", 0)
        ]);
        var handler = new GetLocalisationProjectsHandler(registry, new FakeLspConfigurationProvider());

        var result = await handler.Handle(new GetLocalisationProjectsParams(), CancellationToken.None);

        Assert.Equal(2, result.Projects.Count);
        Assert.Equal("a.csv", result.Projects[0].Label);
        Assert.Equal("/mod/b.csv", result.Projects[1].FilePath);
    }

    [Fact]
    public async Task Handle_RegistryUpdated_ReturnsLatestProjects()
    {
        var registry = new LocalisationProjectRegistry();
        registry.Set([new LocProjectInfo("first.csv", "/mod/first.csv", "Csv", "Root", 0)]);
        var handler = new GetLocalisationProjectsHandler(registry, new FakeLspConfigurationProvider());

        // Update registry after handler is created
        registry.Set([
            new LocProjectInfo("second.csv", "/mod/second.csv", "Nls", "Root", 0),
            new LocProjectInfo("third.csv", "/mod/third.csv", "Nls", "Root", 0)
        ]);

        var result = await handler.Handle(new GetLocalisationProjectsParams(), CancellationToken.None);

        Assert.Equal(2, result.Projects.Count);
        Assert.Equal("second.csv", result.Projects[0].Label);
    }
}