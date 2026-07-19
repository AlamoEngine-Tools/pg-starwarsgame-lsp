// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class UnitSpawnTableHandlerTest
{
    private static readonly UnitSpawnTableHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Spawn_Unit", XmlValueType.UnitSpawnTable);

    [Theory]
    [InlineData("X_Wing, 3")]
    [InlineData("TIE_Fighter,10")]
    [InlineData("Infantry, -1")]
    public void Valid_pair_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("X_Wing")]
    [InlineData(",3")]
    [InlineData("X_Wing, not_an_int")]
    [InlineData("X_Wing, -2")]
    [InlineData("X_Wing,")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
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

    // ── Game object name validation ───────────────────────────────────────────

    private static GameIndex IndexWithObject(string id)
    {
        var sym = new GameSymbol(id, GameSymbolKind.XmlObject, "GameObjectType", new UnknownOrigin("test"), null);
        var defs = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase)
            .Add(id, ImmutableArray.Create(sym));
        return new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    [Fact]
    public void Known_unit_name_returns_no_diagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObject("X_Wing"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "X_Wing, 3"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_unit_name_returns_error()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObject("X_Wing"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Missing_Unit, 3"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Missing_Unit", d.Message);
    }

    [Fact]
    public void Unknown_unit_name_HighlightsOnlyTheNameToken()
    {
        // A broken token must highlight only itself, not the whole tuple value
        // (fact position is line 0, column 0 via MakeFact).
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObject("X_Wing"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Missing_Unit, 3"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(0, d.OverrideLine);
        Assert.Equal(0, d.OverrideColumn);
        Assert.Equal("Missing_Unit".Length, d.OverrideLength);
    }

    [Fact]
    public void Empty_index_skips_name_validation()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Missing_Unit, 3"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Name_lookup_is_case_insensitive()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObject("X_Wing"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "x_wing, 3"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void FloatCount_ReturnsWarningAtCountToken_WithSuggestedFix()
    {
        // All number types accept a float where an integer is expected, with a Warning —
        // the game truncates it.
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "X_Wing, 3.0"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("3", d.SuggestedFix);
        Assert.Equal(8, d.OverrideColumn);
        Assert.Equal(3, d.OverrideLength);
    }
}