// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.ModBrowser;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>
///     What the Mod Browser lists (#270): every indexed entry once, by what it is, as the definition
///     the game uses - the highest layer's, else the game's own.
/// </summary>
public sealed class SymbolCatalogTest
{
    private const string Core = "file:///c:/work/core/data/xml/units.xml";
    private const string Mod = "file:///c:/work/mod/data/xml/units.xml";

    private static readonly IReadOnlyList<ObjectKindDefinition> Kinds =
    [
        new() { Kind = "SpaceUnit", Behaviors = ["SPACE_UNIT"] },
        new() { Kind = "Hero", Flags = ["Is_Named_Hero"] }
    ];

    private static GameSymbol Xml(string id, string type, string uri, string[]? behaviors = null,
        string[]? flags = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, type, new FileOrigin(uri, 0, null), null,
            Behaviors: behaviors, Flags: flags);
    }

    private static GameSymbol Shipped(string id, string type)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, type, new UnknownOrigin("baseline"), null);
    }

    /// <summary>
    ///     The game ships a Star Destroyer and an SFX event; the core library overrides the Star
    ///     Destroyer and adds a frigate; the mod overrides the frigate and adds a hero.
    /// </summary>
    private static GameIndex Index()
    {
        GameSymbol[] core =
        [
            Xml("Star_Destroyer", "GameObjectType", Core, ["SPACE_UNIT"]),
            Xml("Core_Frigate", "GameObjectType", Core, ["SPACE_UNIT"])
        ];
        GameSymbol[] mod =
        [
            Xml("Core_Frigate", "GameObjectType", Mod, ["SPACE_UNIT"]),
            Xml("Hero_Ship", "GameObjectType", Mod, ["INFANTRY"], ["Is_Named_Hero"])
        ];
        var baseline = BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary<string, GameSymbol>.Empty
                .Add("Star_Destroyer", Shipped("Star_Destroyer", "GameObjectType"))
                .Add("Unit_Fire_SFX", Shipped("Unit_Fire_SFX", "SFXEvent"))
        };
        var definitions = core.Concat(mod).GroupBy(s => s.Id)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
        return GameIndex.Empty with
        {
            Baseline = baseline,
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Core, new DocumentIndex(Core, 1, [.. core], [], LayerRank: 0, LayerName: "Core"))
                .Add(Mod, new DocumentIndex(Mod, 1, [.. mod], [], LayerRank: 1, LayerName: "Mod")),
            WorkspaceDefinitions = definitions,
            AssetFiles = MergedAssetFileIndex.Merge(
                [
                    "data/art/models/ev_stardestroyer.alo", "data/art/models/p_smoke.alo",
                    "data/art/models/ev_stardestroyer_die.ala"
                ],
                ["data/art/models/core_frigate.alo"]),
            ParticleSystems = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "p_smoke.alo")
        };
    }

    private static SymbolCatalog Catalog()
    {
        return SymbolCatalog.Build(Index(), Kinds, []);
    }

    private static GetSymbolPageResult Page(string page, string? query = null, int offset = 0, int limit = 200,
        IReadOnlyList<string>? kinds = null, IReadOnlyList<string>? origins = null)
    {
        return Catalog().Page(new GetSymbolPageParams(page)
            { Query = query, Offset = offset, Limit = limit, Kinds = kinds, Origins = origins });
    }

    [Fact]
    public void TheGameDataOutline_HasAPagePerType_CountingEachEntryOnce()
    {
        var outline = Catalog().Outline(SymbolOutlines.GameData);

        Assert.Equal(
            [new SymbolPageSummary("xml:GameObjectType", 3), new SymbolPageSummary("xml:SFXEvent", 1)],
            outline);
    }

    [Fact]
    public void ARow_IsTheDefinitionTheGameUses_NamingWhatItOverrides()
    {
        var rows = Page("xml:GameObjectType").Rows.ToDictionary(r => r.Name);

        Assert.Equal(["Core_Frigate", "Hero_Ship", "Star_Destroyer"], rows.Keys);
        Assert.Equal(("layer:1", "Mod"), (rows["Core_Frigate"].Origin, rows["Core_Frigate"].OriginName));
        Assert.Equal(["layer:0"], rows["Core_Frigate"].Overrides);
        Assert.Equal(("layer:0", "Core"), (rows["Star_Destroyer"].Origin, rows["Star_Destroyer"].OriginName));
        Assert.Equal(["game"], rows["Star_Destroyer"].Overrides);
        Assert.Empty(rows["Hero_Ship"].Overrides);
        Assert.Equal("game", Page("xml:SFXEvent").Rows.Single().Origin);
    }

    [Fact]
    public void AGameObjectRow_CarriesTheKindsItIs()
    {
        var rows = Page("xml:GameObjectType").Rows.ToDictionary(r => r.Name);

        Assert.Equal(["SpaceUnit"], rows["Core_Frigate"].Kinds);
        Assert.Equal(["Hero"], rows["Hero_Ship"].Kinds);
    }

    [Fact]
    public void TheNameQuery_PutsPrefixMatchesFirst_ThenTheRest()
    {
        var rows = Page("xml:GameObjectType", "star").Rows.Select(r => r.Name);

        Assert.Equal(["Star_Destroyer"], rows);
        // Alphabetically Hero_Ship comes first; as a prefix match Star_Destroyer does.
        Assert.Equal(["Star_Destroyer", "Hero_Ship"],
            Page("xml:GameObjectType", "s").Rows.Select(r => r.Name));
    }

    [Fact]
    public void APage_IsPagedOverTheFilteredTotal()
    {
        var page = Page("xml:GameObjectType", offset: 1, limit: 1);

        Assert.Equal(3, page.Total);
        Assert.Equal(["Hero_Ship"], page.Rows.Select(r => r.Name));
    }

    // Sorted on the server, before the page is cut: the table holds a page, and sorting only the rows
    // it holds would show a wrong order.
    [Fact]
    public void APage_IsSortedByAColumn_BeforeItIsPaged()
    {
        GetSymbolPageResult Sorted(string column, bool descending, int limit = 200, string? query = null)
        {
            return Catalog().Page(new GetSymbolPageParams("xml:GameObjectType")
                { Sort = column, Descending = descending, Limit = limit, Query = query });
        }

        Assert.Equal(["Star_Destroyer", "Hero_Ship", "Core_Frigate"],
            Sorted("name", true).Rows.Select(r => r.Name));
        var first = Sorted("name", true, 1);
        Assert.Equal(["Star_Destroyer"], first.Rows.Select(r => r.Name));
        Assert.Equal(3, first.Total);
        // By what an entry behaves like, ties by name.
        Assert.Equal(["Hero_Ship", "Core_Frigate", "Star_Destroyer"],
            Sorted("kinds", false).Rows.Select(r => r.Name));
        // From the project's own layer down to the game's, as the panel reads origins.
        Assert.Equal(["Core_Frigate", "Hero_Ship", "Star_Destroyer"],
            Sorted("origin", false).Rows.Select(r => r.Name));
        Assert.Equal(["Star_Destroyer", "Core_Frigate", "Hero_Ship"],
            Sorted("origin", true).Rows.Select(r => r.Name));
        // A column asked for wins over the query's own order.
        Assert.Equal(["Hero_Ship", "Star_Destroyer"], Sorted("name", false, query: "s").Rows.Select(r => r.Name));
    }

    [Fact]
    public void TheKindsAndOriginFilters_Narrow()
    {
        Assert.Equal(["Core_Frigate"],
            Page("xml:GameObjectType", kinds: ["SpaceUnit"], origins: ["layer:1"]).Rows.Select(r => r.Name));
        Assert.Equal(["Unit_Fire_SFX"], Page("xml:SFXEvent", origins: ["game"]).Rows.Select(r => r.Name));
    }

    [Fact]
    public void TheAssetsOutline_KeepsModelsAndParticleSystemsApart()
    {
        var outline = Catalog().Outline(SymbolOutlines.Assets).ToDictionary(p => p.Page, p => p.Count);

        Assert.Equal(2, outline["models"]);
        Assert.Equal(1, outline["particleSystems"]);
        Assert.Equal(1, outline["animations"]);
        Assert.Equal(["core_frigate.alo", "ev_stardestroyer.alo"], Page("models").Rows.Select(r => r.Name));
        Assert.Equal("game", Page("models").Rows.Single(r => r.Name == "ev_stardestroyer.alo").Origin);
        Assert.Equal("project", Page("models").Rows.Single(r => r.Name == "core_frigate.alo").Origin);
    }

    [Fact]
    public void AGameObjectPage_CountsEveryKind_OverTheNameAndOriginFilters_NotTheKindFilter()
    {
        Assert.Equal([new SymbolKindCount("Hero", 1), new SymbolKindCount("SpaceUnit", 2)],
            Page("xml:GameObjectType").Kinds);
        Assert.Equal([new SymbolKindCount("Hero", 0), new SymbolKindCount("SpaceUnit", 1)],
            Page("xml:GameObjectType", "Star").Kinds);
        Assert.Equal([new SymbolKindCount("Hero", 1), new SymbolKindCount("SpaceUnit", 2)],
            Page("xml:GameObjectType", kinds: ["Hero"]).Kinds);
        Assert.Equal([new SymbolKindCount("Hero", 1), new SymbolKindCount("SpaceUnit", 1)],
            Page("xml:GameObjectType", origins: ["layer:1"]).Kinds);
    }

    [Fact]
    public void APageWithoutGameObjects_HasNoKinds()
    {
        Assert.Empty(Page("xml:SFXEvent").Kinds);
        Assert.Empty(Page("models").Kinds);
    }

    [Fact]
    public void ARow_NamesThePageItOpensOn()
    {
        Assert.All(Page("models").Rows, r => Assert.Equal("models", r.Page));
        Assert.All(Page("particleSystems").Rows, r => Assert.Equal("particleSystems", r.Page));
        Assert.All(Page("xml:SFXEvent").Rows, r => Assert.Equal("xml:SFXEvent", r.Page));
    }

    [Fact]
    public void AnUnknownPage_IsEmpty()
    {
        Assert.Equal(0, Page("xml:NoSuchType").Total);
        Assert.Empty(Catalog().Outline("noSuchOutline"));
    }
}