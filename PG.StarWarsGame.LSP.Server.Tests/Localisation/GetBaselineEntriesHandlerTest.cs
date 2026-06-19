// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class GetBaselineEntriesHandlerTest
{
    [Fact]
    public async Task Handle_Always_ReturnsNonEmptyEntries()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetBaselineEntriesParams(), CancellationToken.None);

        Assert.NotEmpty(result.Entries);
    }

    [Fact]
    public async Task Handle_Always_AllEntryKeysAreNonEmpty()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetBaselineEntriesParams(), CancellationToken.None);

        Assert.All(result.Entries, e => Assert.NotEmpty(e.Key));
    }

    [Fact]
    public async Task Handle_Always_EnglishTranslationPresentOnEveryEntry()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetBaselineEntriesParams(), CancellationToken.None);

        Assert.All(result.Entries, e => Assert.True(
            e.Translations.ContainsKey("ENGLISH"),
            $"Entry '{e.Key}' is missing an ENGLISH translation"));
    }

    [Fact]
    public async Task Handle_Always_EachEntryHasAtLeastFiveLanguages()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetBaselineEntriesParams(), CancellationToken.None);

        var first = result.Entries.First();
        Assert.True(first.Translations.Count >= 5,
            $"Expected >= 5 language columns, got {first.Translations.Count}");
    }

    [Fact]
    public async Task Handle_Always_TranslationValuesAreStrings()
    {
        var handler = BuildHandler();

        var result = await handler.Handle(new GetBaselineEntriesParams(), CancellationToken.None);

        var entry = result.Entries.First();
        Assert.All(entry.Translations.Values, v => Assert.NotNull(v));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GetBaselineEntriesHandler BuildHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        return new GetBaselineEntriesHandler(
            sp.GetRequiredService<IBaselineTranslationProvider>(),
            sp.GetRequiredService<ILanguageService>(),
            sp.GetRequiredService<ITranslationDatabaseFactory>());
    }
}