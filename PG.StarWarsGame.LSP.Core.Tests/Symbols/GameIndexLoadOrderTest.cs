// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     The definition a name resolves to, as the game picks it: the files each registry loads, in its
///     list order, and the registry's duplicate-name rule. A shipped definition counts only when the
///     game opens its file.
/// </summary>
public sealed class GameIndexLoadOrderTest
{
    private const string Objects = "data/xml/gameobjectfiles.xml";
    private const string Routes = "data/xml/traderoutefiles.xml";
    private const string Factions = "data/xml/factionfiles.xml";

    private static readonly MetafileDefinition[] Metafiles =
    [
        new(Objects, MetafileType.FileRegistry, ["GameObjectType"]) { DuplicateNames = DuplicateNameRule.FIRST_WINS },
        new(Routes, MetafileType.FileRegistry, ["TradeRoute"]) { DuplicateNames = DuplicateNameRule.LAST_WINS },
        new(Factions, MetafileType.FileRegistry, ["Faction"]) { DuplicateNames = DuplicateNameRule.FIRST_BY_NAME },
        new("data/xml/musicevents.xml", MetafileType.DirectContent, ["MusicEvent"])
    ];

    private static string Mod(string file)
    {
        return "file:///c:/mod/data/xml/" + file;
    }

    private static GameSymbol Shipped(string id, string file, string type = "GameObjectType", int line = 1)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, type, new FileOrigin(@"DATA\XML\" + file, line, null),
            null);
    }

    private static GameSymbol InMod(string id, string file, string type = "GameObjectType", int line = 1)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, type, new FileOrigin(Mod(file), line, null), null);
    }

    private static RegistryWalkEntry ShippedEntry(string key)
    {
        return new RegistryWalkEntry(key, null);
    }

    private static RegistryWalkEntry ModEntry(string key)
    {
        return new RegistryWalkEntry(key, Mod(key));
    }

    private static RegistryWalk Walk(string registry, params RegistryWalkEntry[] entries)
    {
        return new RegistryWalk(registry, null, [.. entries]);
    }

    private static GameIndex Index(LoadOrder order, GameSymbol[] shipped, params GameSymbol[] workspace)
    {
        var documents = workspace
            .Select(s => ((FileOrigin)s.Origin).Uri)
            .Distinct()
            .ToImmutableDictionary(uri => uri,
                uri => new DocumentIndex(uri, 0, [.. workspace.Where(s => ((FileOrigin)s.Origin).Uri == uri)], [],
                    LayerRank: 1));
        return GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with { Symbols = shipped.ToImmutableDictionary(s => s.Id) },
            Documents = documents,
            WorkspaceDefinitions = workspace.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.OrdinalIgnoreCase),
            LoadOrder = order
        };
    }

    private static LoadOrder Order(params RegistryWalk[] walks)
    {
        return LoadOrder.From(walks, Metafiles, []);
    }

    // ── Which shipped definitions count ──────────────────────────────────────

    // The EaW case: the object registry leaves the expansion's files out, so their objects never
    // load, though the baseline holds them.
    [Fact]
    public void AShippedObject_InAFileTheRegistryLeavesOut_ResolvesNothing()
    {
        var index = Index(Order(Walk(Objects, ShippedEntry("units.xml"))),
            [Shipped("Rebel_Trooper", "Units.xml"), Shipped("Executor", "Units_Underworld.xml")]);

        Assert.NotNull(index.Resolve("Rebel_Trooper"));
        Assert.Null(index.Resolve("Executor"));
        Assert.Null(index.ResolveOfType("Executor", "GameObjectType"));
        Assert.DoesNotContain(index.ShippedSymbols.Values, s => s.Id == "Executor");
    }

    // A mod's copy of a shipped file replaces it whole: an object the copy drops is gone.
    [Fact]
    public void AShippedObject_WhoseFileAModCopyReplaces_ResolvesOnlyFromTheCopy()
    {
        var index = Index(Order(Walk(Objects, ModEntry("units.xml"))),
            [Shipped("Rebel_Trooper", "Units.xml"), Shipped("Dropped_Unit", "Units.xml")],
            InMod("Rebel_Trooper", "units.xml"));

        Assert.Equal(Mod("units.xml"), ((FileOrigin)index.Resolve("Rebel_Trooper")!.Origin).Uri);
        Assert.Null(index.Resolve("Dropped_Unit"));
    }

    // A direct-content file the mod ships replaces the shipped one, and its definitions with it.
    [Fact]
    public void AShippedDirectContentDefinition_WhoseFileAModShips_ResolvesNothing()
    {
        var order = LoadOrder.From([], Metafiles, ["musicevents.xml"]);
        var index = Index(order, [Shipped("Theme", "MusicEvents.xml", "MusicEvent")]);

        Assert.Null(index.Resolve("Theme"));
    }

    // With no walk - no project scanned yet, or a baseline older than the ordered lists - every
    // shipped definition counts, as before.
    [Fact]
    public void WithAnUnknownLoadOrder_EveryShippedDefinitionCounts()
    {
        var index = Index(LoadOrder.Unknown, [Shipped("Executor", "Units_Underworld.xml")]);

        Assert.NotNull(index.Resolve("Executor"));
    }

    // ── Which of two definitions wins ────────────────────────────────────────

    // A mod that appends a file redefining a shipped object: the game keeps the shipped one, the
    // first loaded, though the mod is the higher layer.
    [Fact]
    public void FirstWins_AModObjectInAFileListedAfterTheShippedOne_LosesToTheShippedDefinition()
    {
        var shipped = Shipped("Rebel_Trooper", "Units.xml");
        var index = Index(Order(Walk(Objects, ShippedEntry("units.xml"), ModEntry("mod_units.xml"))),
            [shipped], InMod("Rebel_Trooper", "mod_units.xml"));

        Assert.Same(shipped, index.Resolve("Rebel_Trooper"));
        Assert.Same(shipped, index.ResolveOfType("Rebel_Trooper", "GameObjectType"));
    }

    [Fact]
    public void FirstWins_AModObjectInAFileListedBeforeTheShippedOne_Wins()
    {
        var mod = InMod("Rebel_Trooper", "mod_units.xml");
        var index = Index(Order(Walk(Objects, ModEntry("mod_units.xml"), ShippedEntry("units.xml"))),
            [Shipped("Rebel_Trooper", "Units.xml")], mod);

        Assert.Same(mod, index.Resolve("Rebel_Trooper"));
    }

    [Fact]
    public void FirstWins_InOneFile_TheEarlierLineWins()
    {
        var first = InMod("Rebel_Trooper", "units.xml", line: 3);
        var index = Index(Order(Walk(Objects, ModEntry("units.xml"))), [],
            InMod("Rebel_Trooper", "units.xml", line: 9), first);

        Assert.Same(first, index.ResolveOfType("Rebel_Trooper", "GameObjectType"));
    }

    [Fact]
    public void LastWins_TheLaterListedFileWins_WhicheverLayerItIsIn()
    {
        var later = InMod("Route_A", "b.xml", "TradeRoute");
        var index = Index(Order(Walk(Routes, ModEntry("a.xml"), ModEntry("b.xml"))), [],
            InMod("Route_A", "a.xml", "TradeRoute"), later);

        Assert.Same(later, index.ResolveOfType("Route_A", "TradeRoute"));
    }

    [Fact]
    public void LastWins_InOneFile_TheLaterLineWins()
    {
        var later = InMod("Route_A", "a.xml", "TradeRoute", 9);
        var index = Index(Order(Walk(Routes, ModEntry("a.xml"))), [],
            later, InMod("Route_A", "a.xml", "TradeRoute", 3));

        Assert.Same(later, index.ResolveOfType("Route_A", "TradeRoute"));
    }

    // A file listed twice is loaded twice; its second load comes after the file between them.
    [Fact]
    public void LastWins_AFileListedTwice_CountsFromItsLastPlace()
    {
        var repeated = InMod("Route_A", "a.xml", "TradeRoute");
        var index = Index(Order(Walk(Routes, ModEntry("a.xml"), ModEntry("b.xml"), ModEntry("a.xml"))), [],
            repeated, InMod("Route_A", "b.xml", "TradeRoute"));

        Assert.Same(repeated, index.ResolveOfType("Route_A", "TradeRoute"));
    }

    [Fact]
    public void FirstByName_TheEarlierListedFileWins()
    {
        var first = InMod("Rebel", "a.xml", "Faction");
        var index = Index(Order(Walk(Factions, ModEntry("a.xml"), ModEntry("b.xml"))), [],
            InMod("Rebel", "b.xml", "Faction"), first);

        Assert.Same(first, index.ResolveOfType("Rebel", "Faction"));
    }

    // The definition a winner overrides is the one the rule drops next, not the one a lower layer holds.
    [Fact]
    public void ResolveWithShadow_NamesTheDefinitionTheRuleDrops()
    {
        var shipped = Shipped("Rebel_Trooper", "Units.xml");
        var mod = InMod("Rebel_Trooper", "mod_units.xml");
        var index = Index(Order(Walk(Objects, ShippedEntry("units.xml"), ModEntry("mod_units.xml"))),
            [shipped], mod);

        var result = index.ResolveWithShadow("Rebel_Trooper", "GameObjectType");

        Assert.NotNull(result);
        Assert.Same(shipped, result.Value.Winner);
        Assert.Same(mod, result.Value.Shadowed);
    }
}