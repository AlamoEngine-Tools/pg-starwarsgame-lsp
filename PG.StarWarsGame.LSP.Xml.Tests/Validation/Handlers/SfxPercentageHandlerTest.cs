// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class SfxPercentageHandlerTest
{
    private static readonly SfxPercentageHandler Sut = new();
    private static readonly XmlTagDefinition Tag = XmlHandlerTestFixtures.MakeTag("Chance", XmlValueType.SfxPercentage);

    [Theory]
    [InlineData("0")]
    [InlineData("50")]
    [InlineData("100")]
    public void Valid_sfx_percentage_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void Non_numeric_returns_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Theory]
    [InlineData("-1", "0")]
    [InlineData("101", "100")]
    [InlineData("50.7", "50")]
    public void Out_of_range_or_float_returns_warning_with_clamped_fix(string value, string expectedFix)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(expectedFix, d.SuggestedFix);
    }

    [Fact]
    public void Non_sfx_percentage_tag_returns_no_diagnostics()
    {
        var intTag = XmlHandlerTestFixtures.MakeTag("Count", XmlValueType.Int);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(intTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}