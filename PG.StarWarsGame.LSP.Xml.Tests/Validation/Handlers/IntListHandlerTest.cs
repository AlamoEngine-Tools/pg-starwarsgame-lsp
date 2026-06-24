// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class IntListHandlerTest
{
    private static readonly IntListHandler Sut = new();
    private static readonly XmlTagDefinition Tag = XmlHandlerTestFixtures.MakeTag("Group_Ids", XmlValueType.IntList);

    [Theory]
    [InlineData("1")]
    [InlineData("1 2 3")]
    [InlineData("-5 0 42")]
    public void Valid_int_list_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc def")]
    [InlineData("1 abc 3")]
    public void Non_numeric_tokens_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Theory]
    [InlineData("1.5 2.5", "1 2")]
    [InlineData("1.0f 3.7", "1 3")]
    public void Float_tokens_return_warning_with_truncated_corrected_list(string value, string expectedFix)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(expectedFix, d.SuggestedFix);
    }

    [Theory]
    [InlineData("1, 2, 3,")]
    [InlineData("10 20 ")]
    public void Trailing_separator_is_tolerated(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Non_int_list_tag_returns_no_diagnostics()
    {
        var intTag = XmlHandlerTestFixtures.MakeTag("Count", XmlValueType.Int);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(intTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}