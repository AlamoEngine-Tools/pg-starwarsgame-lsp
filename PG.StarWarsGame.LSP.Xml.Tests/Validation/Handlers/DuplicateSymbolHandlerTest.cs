// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DuplicateSymbolHandlerTest
{
    private static readonly DuplicateSymbolHandler Sut = new();

    private static GameSymbol MakeSymbol(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(uri, line, 0), null);
    }

    [Fact]
    public void Duplicate_in_another_file_emits_error_with_id_and_other_uri()
    {
        var sym1 = MakeSymbol("X1", "file:///a.xml", 2);
        var sym2 = MakeSymbol("X1", "file:///b.xml", 5);
        var fact = new XmlSymbolFact("file:///a.xml", 2, 0, 0, "X1", [sym1, sym2]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("X1", d.Message);
        Assert.Contains("b.xml", d.Message);
    }

    // ── Placeholder name downgrade ───────────────────────────────────────────

    [Theory]
    [InlineData("Default")]
    [InlineData("DEFAULT")]
    [InlineData("default")]
    [InlineData("Null")]
    [InlineData("NULL")]
    [InlineData("None")]
    [InlineData("NONE")]
    public void Placeholder_name_duplicate_emits_information_not_error(string id)
    {
        var sym1 = MakeSymbol(id, "file:///a.xml", 0);
        var sym2 = MakeSymbol(id, "file:///b.xml", 0);
        var fact = new XmlSymbolFact("file:///a.xml", 0, 0, 0, id, [sym1, sym2]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Information, d.Severity);
    }

    [Fact]
    public void Non_placeholder_name_still_emits_error()
    {
        var sym1 = MakeSymbol("X_Wing", "file:///a.xml", 0);
        var sym2 = MakeSymbol("X_Wing", "file:///b.xml", 0);
        var fact = new XmlSymbolFact("file:///a.xml", 0, 0, 0, "X_Wing", [sym1, sym2]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    // ── navigable related locations ──────────────────────────────────────────

    [Fact]
    public void Duplicate_emits_related_location_for_other_definition()
    {
        // The other definition's exact position must ride along as a related location so the
        // editor renders it as a clickable link - "also defined in <file>" alone forces the user
        // to hunt for the line manually.
        var sym1 = MakeSymbol("X1", "file:///a.xml", 2);
        var sym2 = MakeSymbol("X1", "file:///b.xml", 5);
        var fact = new XmlSymbolFact("file:///a.xml", 2, 0, 0, "X1", [sym1, sym2]);

        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.NotNull(d.RelatedLocations);
        var loc = Assert.Single(d.RelatedLocations!);
        Assert.Equal("file:///b.xml", loc.Uri);
        Assert.Equal(5, loc.Line);
        Assert.Contains("X1", loc.Message);
    }

    [Fact]
    public void NonNavigableOtherDefinition_ProducesNoRelatedLocation()
    {
        // Baseline symbols carry game-relative paths the editor cannot open - a related location
        // would render as a dead link.
        var sym1 = MakeSymbol("X1", "file:///a.xml", 2);
        var sym2 = MakeSymbol("X1", "DATA\\XML\\UNITS.XML", 5);
        var fact = new XmlSymbolFact("file:///a.xml", 2, 0, 0, "X1", [sym1, sym2]);

        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.True(d.RelatedLocations is null || d.RelatedLocations.Count == 0);
    }

    [Fact]
    public void Multiple_other_definitions_all_listed()
    {
        var sym1 = MakeSymbol("X1", "file:///a.xml", 2);
        var sym2 = MakeSymbol("X1", "file:///b.xml", 5);
        var sym3 = MakeSymbol("X1", "file:///c.xml", 8);
        var fact = new XmlSymbolFact("file:///a.xml", 2, 0, 0, "X1", [sym1, sym2, sym3]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Contains("b.xml", d.Message);
        Assert.Contains("c.xml", d.Message);
    }
}