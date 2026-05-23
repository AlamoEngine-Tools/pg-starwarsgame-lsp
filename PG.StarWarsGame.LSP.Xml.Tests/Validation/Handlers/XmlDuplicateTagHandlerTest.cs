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
    public void Single_duplicate_emits_error_mentioning_other_line()
    {
        var fact = new XmlDuplicateTagFact("file:///test.xml", 2, 0, 11, Tag, [4]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Max_Speed", d.Message);
        Assert.Contains("4", d.Message);
    }

    [Fact]
    public void Multiple_duplicates_emits_error_mentioning_all_lines()
    {
        var fact = new XmlDuplicateTagFact("file:///test.xml", 2, 0, 11, Tag, [4, 6, 8]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("4", d.Message);
        Assert.Contains("6", d.Message);
        Assert.Contains("8", d.Message);
    }
}