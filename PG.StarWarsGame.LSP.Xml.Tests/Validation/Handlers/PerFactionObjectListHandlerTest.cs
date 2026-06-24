// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class PerFactionObjectListHandlerTest
{
    private static readonly PerFactionObjectListHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Starting_Forces", XmlValueType.PerFactionObjectList);

    private static DiagnosticsContext CtxWithIndex(params GameSymbol[] symbols)
    {
        var baseline = new BaselineIndex(
            symbols.ToImmutableDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);
        var index = GameIndex.Empty with { Baseline = baseline };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    private static GameSymbol Faction(string name) =>
        new(name, GameSymbolKind.XmlObject, "Faction", new FileOrigin("file:///factions.xml", 0, null), null);

    private static GameSymbol Obj(string name) =>
        new(name, GameSymbolKind.XmlObject, "GroundBuildable", new FileOrigin("file:///units.xml", 0, null), null);

    [Theory]
    [InlineData("REBEL, X_Wing")]
    [InlineData("EMPIRE, TIE_Fighter, Star_Destroyer")]
    [InlineData("NEUTRAL,Unit_A,Unit_B,Unit_C")]
    [InlineData("Hutts, UC_Hutt_Grenade_Mortar,\n\t\t\tUC_Hutt_Rapid_Fire_Laser_Turret,")]
    public void Valid_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Empty_value_returns_error()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, ""), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    // ── semantic validation (requires non-empty baseline) ─────────────────────

    [Fact]
    public void Known_faction_and_objects_return_no_diagnostics()
    {
        var ctx = CtxWithIndex(Faction("Rebel"), Obj("X_Wing"), Obj("Y_Wing"));
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Rebel, X_Wing, Y_Wing"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_faction_returns_error()
    {
        var ctx = CtxWithIndex(Obj("X_Wing"));
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Rebel, X_Wing"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Rebel", d.Message);
    }

    [Fact]
    public void Faction_identity_error_squiggle_covers_the_faction_token_only()
    {
        var ctx = CtxWithIndex(Obj("X_Wing"));
        // fact.Length = whole-value length (21); OverrideLength must be the token length (13)
        var fact = XmlHandlerTestFixtures.MakeFact(Tag, "NOT_A_FACTION, X_Wing");
        var results = Sut.Handle(fact, ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal("NOT_A_FACTION".Length, d.OverrideLength);
    }

    [Fact]
    public void Multi_faction_map_all_known_returns_no_diagnostics()
    {
        var ctx = CtxWithIndex(Faction("Rebel"), Faction("Empire"), Obj("X_Wing"), Obj("TIE_Fighter"));
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Rebel, X_Wing, Empire, TIE_Fighter"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Faction_with_no_objects_is_valid()
    {
        var ctx = CtxWithIndex(Faction("Rebel"), Faction("Empire"), Obj("X_Wing"));
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Rebel, X_Wing, Empire"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_game_object_is_not_validated_by_handler()
    {
        // Existence of game objects is the reference pipeline's responsibility; handler is silent.
        var ctx = CtxWithIndex(Faction("Rebel"));
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Rebel, TYPO_UNIT"), ctx).ToList();
        Assert.Empty(results);
    }
}