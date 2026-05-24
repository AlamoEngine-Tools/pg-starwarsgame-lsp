// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class SfxCountHandlerTest
{
    private static readonly SfxCountHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Max_Sound_Count", XmlValueType.SfxCount);

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("10")]
    public void Valid_sfx_count_values_return_no_diagnostics(string value)
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
    [InlineData("-2", "-1")]
    [InlineData("-1.5f", "-1")]
    [InlineData("-5", "-1")]
    public void Below_minus_one_returns_warning_with_clamped_fix(string value, string expectedFix)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(expectedFix, d.SuggestedFix);
    }

    [Fact]
    public void Non_sfx_count_tag_returns_no_diagnostics()
    {
        var intTag = XmlHandlerTestFixtures.MakeTag("Count", XmlValueType.Int);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(intTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}
