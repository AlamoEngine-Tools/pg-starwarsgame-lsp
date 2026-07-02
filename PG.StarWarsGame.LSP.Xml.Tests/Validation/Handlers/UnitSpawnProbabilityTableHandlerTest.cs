// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class UnitSpawnProbabilityTableHandlerTest
{
    private static readonly UnitSpawnProbabilityTableHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Spawn_Probability", XmlValueType.UnitSpawnProbabilityTable);

    [Theory]
    [InlineData("X_Wing, 0.5")]
    [InlineData("TIE_Fighter,1.0")]
    [InlineData("Infantry, 0.0")]
    public void Valid_single_pair_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("Credit_Power_Up, 0.25, Rancor, 0.25")]
    [InlineData("X_Wing, 0.5, Y_Wing, 0.3, TIE_Fighter, 0.2")]
    [InlineData("Unit_A, 0.14, Unit_B, 0.14, Unit_C, 0.14,")]
    public void Valid_multi_pair_content_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("X_Wing")]
    [InlineData(",0.5")]
    [InlineData("X_Wing, 1.5")]
    [InlineData("X_Wing, -0.1")]
    [InlineData("X_Wing, not_a_float")]
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

    private static GameIndex IndexWithObjects(params string[] ids)
    {
        var defs = ImmutableDictionary.CreateBuilder<string, ImmutableArray<GameSymbol>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            defs[id] = ImmutableArray.Create(
                new GameSymbol(id, GameSymbolKind.XmlObject, "GameObjectType", new UnknownOrigin("test"), null));
        return new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            defs.ToImmutable(),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    [Fact]
    public void Known_unit_names_return_no_diagnostics()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObjects("X_Wing", "TIE_Fighter"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "X_Wing, 0.5, TIE_Fighter, 0.3"), ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unknown_unit_name_returns_error()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObjects("X_Wing"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "X_Wing, 0.5, Missing_Unit, 0.3"), ctx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Missing_Unit", d.Message);
    }

    [Fact]
    public void Multiple_unknown_names_return_one_error_each()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObjects("X_Wing"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Unknown_A, 0.5, Unknown_B, 0.3"), ctx).ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, d => Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void Empty_index_skips_name_validation()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Missing_Unit, 0.5"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Name_lookup_is_case_insensitive()
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), IndexWithObjects("X_Wing"),
            "file:///test.xml", "en");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "x_wing, 0.5"), ctx).ToList();
        Assert.Empty(results);
    }
}