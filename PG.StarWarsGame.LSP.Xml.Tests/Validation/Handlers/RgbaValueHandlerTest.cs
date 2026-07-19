// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class RgbaValueHandlerTest
{
    private static readonly RgbaValueHandler Sut = new();
    private static readonly XmlTagDefinition Tag = XmlHandlerTestFixtures.MakeTag("Color", XmlValueType.RGBA);

    [Theory]
    [InlineData("255 128 0")]
    [InlineData("255 128 0 255")]
    [InlineData("0, 0, 0")]
    [InlineData("0, 0, 0, 255")]
    public void Valid_rgba_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("255 128")]
    [InlineData("255 128 0 255 0")]
    [InlineData("-1 0 0")]
    [InlineData("256 0 0")]
    [InlineData("abc def ghi")]
    [InlineData("")]
    public void Invalid_rgba_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_rgba_tag_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void FloatComponents_ReturnWarningWithCorrectedColor()
    {
        // All number types accept a float where an integer is expected, with a Warning.
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "255.0, 128, 0, 255"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("255 128 0 255", d.SuggestedFix);
    }

    [Fact]
    public void FloatComponentOutOfRange_StillError()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "300.0, 128, 0"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }
}