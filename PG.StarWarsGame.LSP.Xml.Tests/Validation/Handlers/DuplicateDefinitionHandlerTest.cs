// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A name defined twice: where the registry's rule places both, the definition the game drops
///     is told which one it keeps and why; where nothing orders them, both are told of the other.
/// </summary>
public sealed class DuplicateDefinitionHandlerTest
{
    private static readonly DuplicateDefinitionHandler Sut = new();

    private static GameSymbol MakeSymbol(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(uri, line, 0), null);
    }

    private static XmlSymbolFact Ruled(string id, GameSymbol self, GameSymbol kept, DuplicateNameRule rule)
    {
        return new XmlSymbolFact(((FileOrigin)self.Origin).Uri, ((FileOrigin)self.Origin).Line, 0, 0, id,
            [self, kept], kept, rule, "gameobjectfiles.xml");
    }

    // ── the registry's rule places both ──────────────────────────────────────

    [Theory]
    [InlineData(DuplicateNameRule.FIRST_WINS, "first")]
    [InlineData(DuplicateNameRule.LAST_WINS, "last")]
    public void ADroppedDefinition_IsNotUsed_AndNamesTheRuleAndTheKeptFile(DuplicateNameRule rule, string word)
    {
        var self = MakeSymbol("X_Wing", "file:///c:/mod/data/xml/mod_units.xml", 4);
        var kept = MakeSymbol("X_Wing", "DATA\\XML\\Units.xml", 9);

        var d = Assert.Single(Sut.Handle(Ruled("X_Wing", self, kept, rule), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(DiagnosticIds.DefinitionNotUsed, d.Id);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("X_Wing", d.Message);
        Assert.Contains("Units.xml", d.Message);
        Assert.Contains("gameobjectfiles.xml", d.Message);
        Assert.Contains(word, d.Message);
    }

    // Both factions stay in the list; only a lookup by name misses the second.
    [Fact]
    public void ASecondFactionOfOneName_IsNotFoundByName()
    {
        var self = MakeSymbol("Rebel", "file:///c:/mod/data/xml/b.xml", 1);
        var kept = MakeSymbol("Rebel", "file:///c:/mod/data/xml/a.xml", 1);

        var d = Assert.Single(Sut.Handle(Ruled("Rebel", self, kept, DuplicateNameRule.FIRST_BY_NAME),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(DiagnosticIds.DefinitionNotUsed, d.Id);
        Assert.Contains("by name", d.Message);
    }

    // A second targeting set is not lost: the game parses it into the first.
    [Fact]
    public void ASecondTargetingSet_IsMerged_AsInformation()
    {
        var self = MakeSymbol("Set", "file:///c:/mod/data/xml/b.xml", 1);
        var kept = MakeSymbol("Set", "file:///c:/mod/data/xml/a.xml", 1);

        var d = Assert.Single(Sut.Handle(Ruled("Set", self, kept, DuplicateNameRule.MERGE),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(DiagnosticIds.DefinitionMerged, d.Id);
        Assert.Equal(XmlDiagnosticSeverity.Information, d.Severity);
        Assert.Contains("a.xml", d.Message);
    }

    [Fact]
    public void TheKeptDefinition_RidesAlongAsARelatedLocation_WhenItCanBeOpened()
    {
        var self = MakeSymbol("X1", "file:///c:/mod/data/xml/b.xml", 2);
        var kept = MakeSymbol("X1", "file:///c:/mod/data/xml/a.xml", 5);

        var d = Assert.Single(Sut.Handle(Ruled("X1", self, kept, DuplicateNameRule.FIRST_WINS),
            XmlHandlerTestFixtures.EmptyCtx));

        var loc = Assert.Single(d.RelatedLocations!);
        Assert.Equal("file:///c:/mod/data/xml/a.xml", loc.Uri);
        Assert.Equal(5, loc.Line);
    }

    [Fact]
    public void AShippedKeptDefinition_ProducesNoRelatedLocation()
    {
        var self = MakeSymbol("X1", "file:///c:/mod/data/xml/b.xml", 2);
        var kept = MakeSymbol("X1", "DATA\\XML\\UNITS.XML", 5);

        var d = Assert.Single(Sut.Handle(Ruled("X1", self, kept, DuplicateNameRule.FIRST_WINS),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.True(d.RelatedLocations is null || d.RelatedLocations.Count == 0);
    }

    // ── nothing orders them ──────────────────────────────────────────────────

    [Fact]
    public void AnUnorderedDuplicate_IsADuplicateDefinition_ListingTheOthers()
    {
        var sym1 = MakeSymbol("X1", "file:///a.xml", 2);
        var sym2 = MakeSymbol("X1", "file:///b.xml", 5);
        var sym3 = MakeSymbol("X1", "file:///c.xml", 8);
        var fact = new XmlSymbolFact("file:///a.xml", 2, 0, 0, "X1", [sym1, sym2, sym3]);

        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(DiagnosticIds.DuplicateDefinition, d.Id);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("b.xml", d.Message);
        Assert.Contains("c.xml", d.Message);
        Assert.Equal(2, d.RelatedLocations!.Count);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("NULL")]
    [InlineData("None")]
    public void APlaceholderName_IsOnlyInformation(string id)
    {
        var sym1 = MakeSymbol(id, "file:///a.xml", 0);
        var sym2 = MakeSymbol(id, "file:///b.xml", 0);

        var unordered = Assert.Single(Sut.Handle(new XmlSymbolFact("file:///a.xml", 0, 0, 0, id, [sym1, sym2]),
            XmlHandlerTestFixtures.EmptyCtx));
        var ruled = Assert.Single(Sut.Handle(Ruled(id, sym2, sym1, DuplicateNameRule.FIRST_WINS),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Information, unordered.Severity);
        Assert.Equal(XmlDiagnosticSeverity.Information, ruled.Severity);
    }
}