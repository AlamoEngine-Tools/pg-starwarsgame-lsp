// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Assets.ShipNames;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Encyclopedia;
using PG.StarWarsGame.LSP.Server.ShipNames;

namespace PG.StarWarsGame.LSP.Server.Tests.Encyclopedia;

/// <summary>
///     Objects wired into GameConstants' <c>ShipNameTextFiles</c> show an individual ship name where
///     other objects show their class.
/// </summary>
public sealed class GetEncyclopediaEntryShipNamesTest
{
    private const string ObjectId = "STAR_DESTROYER";

    private sealed class StubShipNames(ShipNameCatalog catalog) : IShipNameCatalogProvider
    {
        public string? LastTagValue { get; private set; }

        public ShipNameCatalog Get(string projectRoot, string? rawTagValue)
        {
            LastTagValue = rawTagValue;
            // Honours the real provider's contract: no wiring, no pools. Returning the catalog
            // regardless would let a test pass that the production path could not.
            return string.IsNullOrWhiteSpace(rawTagValue) ? ShipNameCatalog.Empty : catalog;
        }
    }

    private sealed class ThrowingShipNames : IShipNameCatalogProvider
    {
        public ShipNameCatalog Get(string projectRoot, string? rawTagValue)
            => throw new IOException("name file unreadable");
    }

    private static GameSymbol Sym(string id, string type) =>
        new(id, GameSymbolKind.XmlObject, type, new FileOrigin("file:///u.xml", 0, 0), null, null);

    private static VariantTag Tag(string name, string value) =>
        new(name, value, $"<{name}>{value}</{name}>", 0);

    private static ShipNameCatalog CatalogWith(params string[] names)
    {
        return ShipNameCatalog.Build(
            $"{ObjectId}, Data\\SD.txt",
            _ => System.Text.Encoding.Unicode.GetPreamble()
                .Concat(System.Text.Encoding.Unicode.GetBytes(string.Join("\r\n", names)))
                .ToArray());
    }

    private static async Task<GetEncyclopediaEntryResult> Run(
        IShipNameCatalogProvider? shipNames, string? shipNameTagValue = "STAR_DESTROYER, Data\\SD.txt")
    {
        var source = new FakeVariantTagSource()
            .With(ObjectId, Tag("Encyclopedia_Text", "TEXT_A"), Tag("Encyclopedia_Unit_Class", "Class: Capital"));

        if (shipNameTagValue is not null)
            source.With("GameConstants", Tag("ShipNameTextFiles", shipNameTagValue));

        var defs = new[] { Sym(ObjectId, "SpaceUnit"), Sym("GameConstants", "GameConstants") }
            .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase);

        var config = new FakeLspConfigurationProvider();
        config.Current = config.Current with { WorkspaceRoot = @"C:\mod" };

        var handler = new GetEncyclopediaEntryHandler(
            new FakeGameIndexService(GameIndex.Empty with { WorkspaceDefinitions = defs }),
            new NullSchemaProvider(), source, config, null, null, shipNames);

        return await handler.Handle(new GetEncyclopediaEntryParams { ObjectId = ObjectId }, default);
    }

    [Fact]
    public async Task Handle_RegisteredObject_ReportsThePoolAndLeavesTheClassLineAlone()
    {
        var result = await Run(new StubShipNames(CatalogWith("Allecto", "Devastator")));

        // The server reports the pool and does NOT pick - the panel does, so the card keeps one
        // name for as long as it is open. UnitClass therefore stays the real class line.
        Assert.NotNull(result.ShipNames);
        Assert.Equal(["Allecto", "Devastator"], result.ShipNames.Names);
    }

    [Fact]
    public async Task Handle_ReportsTheWholePoolNotJustTheDrawnName()
    {
        // The panel shows what else the object could have been called, so the whole list travels.
        var result = await Run(new StubShipNames(CatalogWith("Allecto", "Devastator", "Tyrant")));

        Assert.Equal(["Allecto", "Devastator", "Tyrant"], result.ShipNames!.Names);
        Assert.True(result.ShipNames.FileFound);
        Assert.Equal("Data\\SD.txt", result.ShipNames.SourcePath);
    }

    /// <summary>
    ///     The wiring comes from the GameConstants SINGLETON, resolved through the normal layered
    ///     index - which is the whole reason singletons are indexed at all.
    /// </summary>
    [Fact]
    public async Task Handle_ReadsTheWiringFromTheGameConstantsSingleton()
    {
        var stub = new StubShipNames(CatalogWith("Allecto"));

        await Run(stub, "STAR_DESTROYER, Data\\Custom.txt");

        Assert.Equal("STAR_DESTROYER, Data\\Custom.txt", stub.LastTagValue);
    }

    [Fact]
    public async Task Handle_UnregisteredObject_KeepsItsClassLine()
    {
        var result = await Run(new StubShipNames(ShipNameCatalog.Empty));

        Assert.Null(result.ShipNames);
        Assert.Null(result.UnitClass); // untouched: the class line is a loca key and
                                       // this fixture loads no translations
    }

    /// <summary>
    ///     The wiring resolves from the BASELINE when the workspace ships no GameConstants.xml of
    ///     its own - which is the normal case for a mod, and the gap that made singleton support in
    ///     the baseline projector necessary.
    /// </summary>
    [Fact]
    public async Task Handle_GameConstantsOnlyInTheBaseline_StillResolvesTheWiring()
    {
        var stub = new StubShipNames(CatalogWith("Allecto"));

        // Workspace defines only the unit; GameConstants exists solely as a baseline symbol+tags.
        var source = new FakeVariantTagSource()
            .With(ObjectId, Tag("Encyclopedia_Text", "TEXT_A"));

        var baseline = BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [KeyValuePair.Create("GameConstants", Sym("GameConstants", "GameConstants"))]),
            ObjectTags = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [
                    KeyValuePair.Create("GameConstants", ImmutableArray.Create(
                        new BaselineTag("ShipNameTextFiles", "STAR_DESTROYER, Data\\SD.txt", "", 0)))
                ])
        };

        var defs = new[] { Sym(ObjectId, "SpaceUnit") }
            .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase);

        var config = new FakeLspConfigurationProvider();
        config.Current = config.Current with { WorkspaceRoot = @"C:\mod" };

        var handler = new GetEncyclopediaEntryHandler(
            new FakeGameIndexService(GameIndex.Empty with
            {
                WorkspaceDefinitions = defs, Baseline = baseline
            }),
            new NullSchemaProvider(), source, config, null, null, stub);

        var result = await handler.Handle(new GetEncyclopediaEntryParams { ObjectId = ObjectId }, default);

        Assert.Equal("STAR_DESTROYER, Data\\SD.txt", stub.LastTagValue);
        Assert.NotNull(result.ShipNames);
        Assert.Equal(["Allecto"], result.ShipNames.Names);
    }

    [Fact]
    public async Task Handle_NoGameConstantsIndexed_KeepsTheClassLine()
    {
        // A workspace that ships no GameConstants.xml of its own has no wiring to read.
        var result = await Run(new StubShipNames(CatalogWith("Allecto")), null);

        Assert.Null(result.ShipNames);
        Assert.Null(result.UnitClass); // untouched: the class line is a loca key and
                                       // this fixture loads no translations
    }

    [Fact]
    public async Task Handle_EmptyPool_ReportsItAsEmpty()
    {
        // Wired up, file present, no names in it: showing a blank where the class was would be
        // worse than showing the class.
        var result = await Run(new StubShipNames(CatalogWith()));

        Assert.Empty(result.ShipNames!.Names);
        Assert.Null(result.UnitClass); // untouched: the class line is a loca key and
                                       // this fixture loads no translations
    }

    [Fact]
    public async Task Handle_WithoutTheShipNameService_StillReturnsTheEntry()
    {
        var result = await Run(null);

        Assert.True(result.Found);
        Assert.Null(result.ShipNames);
        Assert.Null(result.UnitClass); // untouched: the class line is a loca key and
                                       // this fixture loads no translations
    }

    [Fact]
    public async Task Handle_ShipNameLookupThrows_StillReturnsTheEntry()
    {
        var result = await Run(new ThrowingShipNames());

        Assert.True(result.Found);
        Assert.Null(result.ShipNames);
        Assert.Null(result.UnitClass); // untouched: the class line is a loca key and
                                       // this fixture loads no translations
    }
}
