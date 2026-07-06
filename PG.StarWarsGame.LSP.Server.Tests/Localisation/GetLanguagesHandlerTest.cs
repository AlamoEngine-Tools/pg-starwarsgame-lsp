// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class GetLanguagesHandlerTest
{
    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LocalisationFlagOff_ReturnsEmptyLanguages()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var handler = BuildHandler(config);

        var result = await handler.Handle(new GetLanguagesParams(), CancellationToken.None);

        Assert.Empty(result.Languages);
    }

    [Fact]
    public async Task Handle_Always_ReturnsNonEmptyLanguageList()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetLanguagesParams(), CancellationToken.None);

        Assert.NotEmpty(result.Languages);
    }

    [Fact]
    public async Task Handle_Always_ContainsEnglish()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetLanguagesParams(), CancellationToken.None);

        Assert.Contains("ENGLISH", result.Languages, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_Always_AllIdentifiersAreNonEmpty()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetLanguagesParams(), CancellationToken.None);

        Assert.All(result.Languages, l => Assert.NotEmpty(l));
    }

    [Fact]
    public async Task Handle_Always_AllIdentifiersAreUppercase()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetLanguagesParams(), CancellationToken.None);

        Assert.All(result.Languages, l => Assert.Equal(l.ToUpperInvariant(), l));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GetLanguagesHandler BuildHandler(ILspConfigurationProvider? config = null)
    {
        var services = new ServiceCollection();
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();
        return new GetLanguagesHandler(
            sp.GetRequiredService<ILanguageService>(), config ?? new FakeLspConfigurationProvider());
    }
}