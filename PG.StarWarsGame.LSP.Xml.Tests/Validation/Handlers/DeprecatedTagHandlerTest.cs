// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DeprecatedTagHandlerTest
{
    private static readonly DeprecatedTagHandler Sut = new();

    private static XmlTagDefinition DeprecatedTag(string name)
    {
        return new XmlTagDefinition
        {
            Tag = name, ValueType = XmlValueType.Float, Deprecated = true
        };
    }

    private static XmlTagDefinition ActiveTag(string name)
    {
        return new XmlTagDefinition
        {
            Tag = name, ValueType = XmlValueType.Float, Deprecated = false
        };
    }

    [Fact]
    public void Deprecated_tag_emits_warning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(DeprecatedTag("Old_Field"), "1.0");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Old_Field", d.Message);
    }

    [Fact]
    public void Non_deprecated_tag_emits_no_diagnostics()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(ActiveTag("Current_Field"), "1.0");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(XmlValueType.Float)]
    [InlineData(XmlValueType.Int)]
    [InlineData(XmlValueType.NameReference)]
    [InlineData(XmlValueType.Boolean)]
    public void Deprecated_tag_emits_warning_regardless_of_value_type(XmlValueType type)
    {
        var tag = new XmlTagDefinition { Tag = "Old", ValueType = type, Deprecated = true };
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "value");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
    }
}