// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Workspace;
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

    // ── dependency-aware "inherited" merge ───────────────────────────────────

    [Fact]
    public async Task Handle_WithProjectFilePath_IncludesLowerLayerTranslations()
    {
        var (handler, projectRegistry, layerRegistry, factory, langService) = BuildHandlerWithRegistries();
        var english = langService.Default;

        var depDb = factory.CreateKeyed([english]);
        depDb.SetTranslation("TEXT_FROM_DEP", english, "From Dependency");
        projectRegistry.Set([
            new LocProjectInfo("MasterTextFile.csv", "/root/text/MasterTextFile.csv", "Csv", "Root", 1),
            new LocProjectInfo("core.csv", "/dep/text/core.csv", "Csv", "Dep", 0)
        ]);
        layerRegistry.Set([
            new LocalisationLayerEntry(new ProjectLayer(0, "Dep", [], [], ["/dep/text"], [], "Csv"), depDb)
        ]);

        var result = await handler.Handle(
            new GetBaselineEntriesParams("/root/text/MasterTextFile.csv"), CancellationToken.None);

        var entry = Assert.Single(result.Entries, e => e.Key == "TEXT_FROM_DEP");
        Assert.Equal("From Dependency", entry.Translations["ENGLISH"]);
    }

    [Fact]
    public async Task Handle_WithoutProjectFilePath_ExcludesLayerTranslations()
    {
        var (handler, projectRegistry, layerRegistry, factory, langService) = BuildHandlerWithRegistries();
        var english = langService.Default;

        var depDb = factory.CreateKeyed([english]);
        depDb.SetTranslation("TEXT_FROM_DEP", english, "From Dependency");
        projectRegistry.Set([
            new LocProjectInfo("MasterTextFile.csv", "/root/text/MasterTextFile.csv", "Csv", "Root", 1),
            new LocProjectInfo("core.csv", "/dep/text/core.csv", "Csv", "Dep", 0)
        ]);
        layerRegistry.Set([
            new LocalisationLayerEntry(new ProjectLayer(0, "Dep", [], [], ["/dep/text"], [], "Csv"), depDb)
        ]);

        var result = await handler.Handle(new GetBaselineEntriesParams(), CancellationToken.None);

        Assert.DoesNotContain(result.Entries, e => e.Key == "TEXT_FROM_DEP");
    }

    [Fact]
    public async Task Handle_ProjectFilePathIsTheDependencyItself_DoesNotIncludeOwnLayer()
    {
        // Querying with the dependency's own file (the lowest-ranked layer) must not merge in its
        // own database — only baseline, since there's nothing below rank 0.
        var (handler, projectRegistry, layerRegistry, factory, langService) = BuildHandlerWithRegistries();
        var english = langService.Default;

        var depDb = factory.CreateKeyed([english]);
        depDb.SetTranslation("TEXT_FROM_DEP", english, "From Dependency");
        projectRegistry.Set([
            new LocProjectInfo("core.csv", "/dep/text/core.csv", "Csv", "Dep", 0)
        ]);
        layerRegistry.Set([
            new LocalisationLayerEntry(new ProjectLayer(0, "Dep", [], [], ["/dep/text"], [], "Csv"), depDb)
        ]);

        var result = await handler.Handle(
            new GetBaselineEntriesParams("/dep/text/core.csv"), CancellationToken.None);

        Assert.DoesNotContain(result.Entries, e => e.Key == "TEXT_FROM_DEP");
    }

    [Fact]
    public async Task Handle_UnknownProjectFilePath_FallsBackToBaselineOnly()
    {
        var (handler, projectRegistry, layerRegistry, factory, langService) = BuildHandlerWithRegistries();
        var english = langService.Default;

        var depDb = factory.CreateKeyed([english]);
        depDb.SetTranslation("TEXT_FROM_DEP", english, "From Dependency");
        layerRegistry.Set([
            new LocalisationLayerEntry(new ProjectLayer(0, "Dep", [], [], ["/dep/text"], [], "Csv"), depDb)
        ]);

        var result = await handler.Handle(
            new GetBaselineEntriesParams("/not/a/registered/file.csv"), CancellationToken.None);

        Assert.DoesNotContain(result.Entries, e => e.Key == "TEXT_FROM_DEP");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GetBaselineEntriesHandler BuildHandler()
    {
        var (handler, _, _, _, _) = BuildHandlerWithRegistries();
        return handler;
    }

    private static (GetBaselineEntriesHandler Handler, LocalisationProjectRegistry ProjectRegistry,
        LocalisationLayerRegistry LayerRegistry, ITranslationDatabaseFactory Factory,
        ILanguageService LangService) BuildHandlerWithRegistries()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        var projectRegistry = new LocalisationProjectRegistry();
        var layerRegistry = new LocalisationLayerRegistry();
        var factory = sp.GetRequiredService<ITranslationDatabaseFactory>();
        var langService = sp.GetRequiredService<ILanguageService>();

        var handler = new GetBaselineEntriesHandler(
            sp.GetRequiredService<IBaselineTranslationProvider>(),
            langService,
            factory,
            projectRegistry,
            layerRegistry);

        return (handler, projectRegistry, layerRegistry, factory, langService);
    }
}