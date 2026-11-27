// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Preview.Tests;

/// <summary>
///     A land unit's model per map environment, from <c>Land_Terrain_Model_Mapping</c>.
/// </summary>
/// <remarks>
///     The rule as the schema records it: each pair is <c>Environment, Model</c>; the first pair for
///     the map's environment replaces <c>Land_Model_Name</c>, and any other environment keeps it.
///     Repeats append, and a variant's pairs replace the base's. Three units in each shipped game
///     map environments - the Imperial stormtrooper, the Rebel infantry and the plex soldier.
/// </remarks>
public sealed class PreviewTerrainModelTest
{
    private static readonly XmlTagDefinition Mapping = new()
    {
        Tag = "Land_Terrain_Model_Mapping",
        ValueType = XmlValueType.TupleList,
        MultipleAllowed = true,
        RepeatMode = RepeatMode.ADD,
        Slots =
        [
            new TupleSlotDefinition { Label = "Environment", ReferenceKind = ReferenceKind.Enum },
            new TupleSlotDefinition { Label = "Model", ReferenceKind = ReferenceKind.ModelFile }
        ]
    };

    [Fact]
    public void BuildForObject_ListsEachMappedEnvironmentOnceWithItsFirstModel()
    {
        var scene = Trooper(
        [
            Tag("Land_Model_Name", "trooper.alo"),
            Tag("Land_Terrain_Model_Mapping",
                "Temperate, trooper.alo, Arctic, trooper_snow.alo, Volcanic, trooper_sand.alo, " +
                "arctic, trooper_other.alo,")
        ]);

        Assert.Equal(
        [
            ("Temperate", "trooper.alo"),
            ("Arctic", "trooper_snow.alo"),
            ("Volcanic", "trooper_sand.alo")
        ], Pairs(scene));
    }

    [Fact]
    public void BuildForObject_WithoutAnEnvironmentAssemblesTheLandModel()
    {
        var scene = Trooper(
            [Tag("Land_Model_Name", "trooper.alo"), Tag("Land_Terrain_Model_Mapping", "Arctic, trooper_snow.alo")]);

        Assert.Equal("trooper.alo", Hull(scene));
        Assert.Null(scene.Environment);
    }

    [Fact]
    public void BuildForObject_AssemblesTheFirstModelTheEnvironmentMapsTo()
    {
        // The engine matches the environment ignoring case and surrounding spaces.
        var scene = Trooper(
            [
                Tag("Land_Model_Name", "trooper.alo"),
                Tag("Land_Terrain_Model_Mapping", "Arctic, trooper_snow.alo, Arctic, trooper_other.alo")
            ],
            " arctic ");

        Assert.Equal("trooper_snow.alo", Hull(scene));
        Assert.Equal("Arctic", scene.Environment);
    }

    [Fact]
    public void BuildForObject_KeepsTheLandModelOnAnEnvironmentTheUnitDoesNotMap()
    {
        var scene = Trooper(
            [Tag("Land_Model_Name", "trooper.alo"), Tag("Land_Terrain_Model_Mapping", "Arctic, trooper_snow.alo")],
            "Desert");

        Assert.Equal("trooper.alo", Hull(scene));
        Assert.Null(scene.Environment);
    }

    [Fact]
    public void BuildForObject_ListsNoEnvironmentForAUnitWithoutAMapping()
    {
        var scene = Trooper([Tag("Land_Model_Name", "trooper.alo")], "Arctic");

        Assert.Empty(scene.TerrainModels);
        Assert.Equal("trooper.alo", Hull(scene));
        Assert.Null(scene.Environment);
    }

    [Fact]
    public void BuildForObject_AppendsTheTagWrittenTwice()
    {
        var scene = Trooper(
            [
                Tag("Land_Model_Name", "trooper.alo"),
                Tag("Land_Terrain_Model_Mapping", "Arctic, trooper_snow.alo"),
                Tag("Land_Terrain_Model_Mapping", "Arctic, trooper_other.alo, Desert, trooper_sand.alo")
            ],
            "Desert");

        Assert.Equal([("Arctic", "trooper_snow.alo"), ("Desert", "trooper_sand.alo")], Pairs(scene));
        Assert.Equal("trooper_sand.alo", Hull(scene));
    }

    [Fact]
    public void BuildForObject_TakesAVariantsPairsInsteadOfItsBases()
    {
        var index = Index(Sym("Trooper"), Sym("Elite_Trooper", "Trooper"));
        var tags = new FakeVariantTagSource()
            .With("Trooper", Tag("Land_Model_Name", "trooper.alo"),
                Tag("Land_Terrain_Model_Mapping", "Arctic, trooper_snow.alo, Desert, trooper_sand.alo"))
            .With("Elite_Trooper", Tag("Land_Terrain_Model_Mapping", "Arctic, elite_snow.alo"));

        var scene = Builder(index, tags).BuildForObject("Elite_Trooper", environment: "Arctic");

        Assert.Equal([("Arctic", "elite_snow.alo")], Pairs(scene));
        Assert.Equal("elite_snow.alo", Hull(scene));
    }

    [Fact]
    public void BuildForObject_SaysOfEachMappedModelWhetherItIsFoundAndWhichClipsItCarries()
    {
        // What the background build is asked to convert ahead, so a switch finds the entry a request for
        // that hull would: the model with the clips the scene would then offer.
        var index = Index(Sym("Trooper")) with
        {
            AssetFiles = MergedAssetFileIndex.Merge(["data/art/models/arctic_idle_00.ala"], [])
        };
        var tags = new FakeVariantTagSource().With("Trooper", Tag("Land_Model_Name", "trooper.alo"),
            Tag("Land_Terrain_Model_Mapping", "Arctic, arctic.alo, Desert, missing.alo"));

        var scene = new PreviewSceneBuilder(new FakeGameIndexService(index),
                new SchemaProviderBuilder().WithTag(Mapping).Build(), tags, new FakeAssets("trooper.alo", "arctic.alo"),
                ResxMessageCatalog.Server("en"))
            .BuildForObject("Trooper");

        var arctic = scene.TerrainModels[0];
        Assert.True(arctic.Resolved);
        Assert.Equal(["arctic_idle_00.ala"], arctic.Animations);

        var desert = scene.TerrainModels[1];
        Assert.False(desert.Resolved);
        Assert.Empty(desert.Animations);
    }

    [Fact]
    public void BuildForObject_GivesEveryMappedModelTheClipsOfTheAnimationOverride()
    {
        // The stormtrooper's mapped models all borrow EI_TROOPER's clips, as its hull does.
        var index = Index(Sym("Trooper")) with
        {
            AssetFiles = MergedAssetFileIndex.Merge(
                ["data/art/models/trooper_idle_00.ala", "data/art/models/arctic_idle_00.ala"], [])
        };
        var tags = new FakeVariantTagSource().With("Trooper", Tag("Land_Model_Name", "trooper.alo"),
            Tag("Land_Model_Anim_Override_Name", "trooper.alo"),
            Tag("Land_Terrain_Model_Mapping", "Arctic, arctic.alo"));

        var scene = new PreviewSceneBuilder(new FakeGameIndexService(index),
                new SchemaProviderBuilder().WithTag(Mapping).Build(), tags, new FakeAssets("trooper.alo", "arctic.alo"),
                ResxMessageCatalog.Server("en"))
            .BuildForObject("Trooper");

        Assert.Equal(["trooper_idle_00.ala"], Assert.Single(scene.TerrainModels).Animations);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static IEnumerable<(string, string)> Pairs(PreviewScene scene)
    {
        return scene.TerrainModels.Select(t => (t.Environment, t.Model));
    }

    private static string Hull(PreviewScene scene)
    {
        return Assert.Single(scene.Parts, p => p.Origin == PreviewPartOrigin.Hull).ModelRef;
    }

    private static PreviewScene Trooper(VariantTag[] tags, string? environment = null)
    {
        return Builder(Index(Sym("Trooper")), new FakeVariantTagSource().With("Trooper", tags))
            .BuildForObject("Trooper", environment: environment);
    }

    private static PreviewSceneBuilder Builder(GameIndex index, FakeVariantTagSource tags)
    {
        return new PreviewSceneBuilder(new FakeGameIndexService(index),
            new SchemaProviderBuilder().WithTag(Mapping).Build(), tags,
            new FakeAssets("trooper.alo", "trooper_snow.alo", "trooper_sand.alo", "elite_snow.alo"),
            ResxMessageCatalog.Server("en"));
    }

    private static GameIndex Index(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static GameSymbol Sym(string id, string? variantBaseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin($"file:///{id}.xml", 0, 0), null, variantBaseId);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }
}