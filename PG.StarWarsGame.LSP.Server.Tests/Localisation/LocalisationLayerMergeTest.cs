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

public sealed class LocalisationLayerMergeTest
{
    [Fact]
    public void MergeBaselineAndLowerLayers_NullBelowRank_OnlyBaselineMerged()
    {
        var (factory, langService) = BuildFactory();
        var english = langService.Default;

        var baseline = factory.CreateKeyed([english]);
        baseline.SetTranslation("TEXT_BASELINE", english, "Baseline Value");
        var layerDb = factory.CreateKeyed([english]);
        layerDb.SetTranslation("TEXT_LAYER", english, "Layer Value");
        var layers = new[] { new LocalisationLayerEntry(Layer(0, "Dep"), layerDb) };

        var target = factory.CreateKeyed([english]);
        LocalisationLayerMerge.MergeBaselineAndLowerLayers(target, [baseline], layers, null);

        Assert.True(target.ContainsKey("TEXT_BASELINE"));
        Assert.False(target.ContainsKey("TEXT_LAYER"));
    }

    [Fact]
    public void MergeBaselineAndLowerLayers_BelowRank_IncludesOnlyLowerRankedLayers()
    {
        var (factory, langService) = BuildFactory();
        var english = langService.Default;

        var depDb = factory.CreateKeyed([english]);
        depDb.SetTranslation("TEXT_DEP", english, "Dep Value");
        var rootDb = factory.CreateKeyed([english]);
        rootDb.SetTranslation("TEXT_ROOT", english, "Root Value");
        var layers = new[]
        {
            new LocalisationLayerEntry(Layer(0, "Dep"), depDb),
            new LocalisationLayerEntry(Layer(1, "Root"), rootDb)
        };

        var target = factory.CreateKeyed([english]);
        LocalisationLayerMerge.MergeBaselineAndLowerLayers(target, [], layers, 1);

        Assert.True(target.ContainsKey("TEXT_DEP"));
        Assert.False(target.ContainsKey("TEXT_ROOT"));
    }

    [Fact]
    public void MergeBaselineAndLowerLayers_SharedKey_NearestLowerLayerWinsOverBaseline()
    {
        var (factory, langService) = BuildFactory();
        var english = langService.Default;

        var baseline = factory.CreateKeyed([english]);
        baseline.SetTranslation("TEXT_SHARED", english, "From Baseline");
        var depDb = factory.CreateKeyed([english]);
        depDb.SetTranslation("TEXT_SHARED", english, "From Dep");
        var layers = new[] { new LocalisationLayerEntry(Layer(0, "Dep"), depDb) };

        var target = factory.CreateKeyed([english]);
        LocalisationLayerMerge.MergeBaselineAndLowerLayers(target, [baseline], layers, 1);

        Assert.True(target.TryGetEntry("TEXT_SHARED", out var entry));
        Assert.Equal("From Dep", entry!.Translations[english]);
    }

    [Fact]
    public void MergeBaselineAndLowerLayers_SharedKey_NearerLayerWinsOverFartherLayer()
    {
        var (factory, langService) = BuildFactory();
        var english = langService.Default;

        var farDb = factory.CreateKeyed([english]);
        farDb.SetTranslation("TEXT_SHARED", english, "From Far Dep");
        var nearDb = factory.CreateKeyed([english]);
        nearDb.SetTranslation("TEXT_SHARED", english, "From Near Dep");
        var layers = new[]
        {
            new LocalisationLayerEntry(Layer(0, "Far"), farDb),
            new LocalisationLayerEntry(Layer(1, "Near"), nearDb)
        };

        // belowRank 2: a hypothetical root above both dependencies.
        var target = factory.CreateKeyed([english]);
        LocalisationLayerMerge.MergeBaselineAndLowerLayers(target, [], layers, 2);

        Assert.True(target.TryGetEntry("TEXT_SHARED", out var entry));
        Assert.Equal("From Near Dep", entry!.Translations[english]);
    }

    private static ProjectLayer Layer(int rank, string name)
    {
        return new ProjectLayer(rank, name, [], [], [], [], "Csv");
    }

    private static (ITranslationDatabaseFactory Factory, ILanguageService LangService) BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ITranslationDatabaseFactory>(), sp.GetRequiredService<ILanguageService>());
    }
}
