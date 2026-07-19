// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class XmlDuplicateTagHandlerTest
{
    private static readonly XmlDuplicateTagHandler Sut = new();
    private static readonly XmlTagDefinition Tag = new() { Tag = "Max_Speed", ValueType = XmlValueType.Float };

    [Fact]
    public void Single_duplicate_emits_warning_mentioning_other_line()
    {
        // Warning, not Error: the game loads top to bottom and the last occurrence wins, so
        // duplicates technically work - they are just bad style.
        var fact = new XmlDuplicateTagFact("file:///test.xml", 2, 0, 11, Tag, [4]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Max_Speed", d.Message);
        Assert.Contains("4", d.Message);
        Assert.Contains("LAST", d.Message);
    }

    [Fact]
    public void Multiple_duplicates_emits_warning_mentioning_all_lines()
    {
        var fact = new XmlDuplicateTagFact("file:///test.xml", 2, 0, 11, Tag, [4, 6, 8]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("4", d.Message);
        Assert.Contains("6", d.Message);
        Assert.Contains("8", d.Message);
    }

    [Fact]
    public void Earlier_occurrence_is_tagged_unnecessary()
    {
        // The engine ignores everything but the last occurrence - grey out the dead ones so the
        // user is prompted to investigate.
        var fact = new XmlDuplicateTagFact("file:///test.xml", 2, 0, 11, Tag, [4], false);
        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
        Assert.NotNull(d.Tags);
        Assert.Contains(XmlDiagnosticTag.Unnecessary, d.Tags!);
    }

    [Fact]
    public void Last_occurrence_is_not_tagged_unnecessary()
    {
        var fact = new XmlDuplicateTagFact("file:///test.xml", 6, 0, 11, Tag, [2, 4], true);
        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
        Assert.True(d.Tags is null || !d.Tags.Contains(XmlDiagnosticTag.Unnecessary));
    }

    [Fact]
    public void Every_occurrence_offers_the_remove_earlier_duplicates_fix()
    {
        var earlier = new XmlDuplicateTagFact("file:///test.xml", 2, 0, 11, Tag, [4], false);
        var last = new XmlDuplicateTagFact("file:///test.xml", 4, 0, 11, Tag, [2], true);

        Assert.True(Assert.Single(Sut.Handle(earlier, XmlHandlerTestFixtures.EmptyCtx)).OfferRemoveEarlierDuplicates);
        Assert.True(Assert.Single(Sut.Handle(last, XmlHandlerTestFixtures.EmptyCtx)).OfferRemoveEarlierDuplicates);
    }
}