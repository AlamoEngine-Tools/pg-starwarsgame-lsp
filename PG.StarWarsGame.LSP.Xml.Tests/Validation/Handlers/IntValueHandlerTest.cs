// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class IntValueHandlerTest
{
    private static readonly IntValueHandler Sut = new();
    private static readonly XmlTagDefinition IntTag = XmlHandlerTestFixtures.MakeTag("Priority", XmlValueType.Int);

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("-100")]
    [InlineData("2147483647")]
    public void Valid_int_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(IntTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("9999999999999")]
    public void Non_numeric_or_overflow_returns_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(IntTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Theory]
    [InlineData("1.5", "1")]
    [InlineData("1.5f", "1")]
    [InlineData("-2.9", "-2")]
    [InlineData("0.9", "0")]
    public void Float_in_int_field_returns_warning_with_truncated_fix(string value, string expectedFix)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(IntTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(expectedFix, d.SuggestedFix);
    }

    [Fact]
    public void Non_int_tag_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}