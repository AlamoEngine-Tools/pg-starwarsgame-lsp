// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class FloatVector4HandlerTest
{
    private static readonly FloatVector4Handler Sut = new();
    private static readonly XmlTagDefinition Tag = XmlHandlerTestFixtures.MakeTag("Color", XmlValueType.FloatVector4);

    [Theory]
    [InlineData("1.0 0.5 0.0 1.0")]
    [InlineData("1.0, 0.5, 0.0, 1.0")]
    [InlineData("0.0 0.0f 0.0F 1.0")]
    public void Valid_float4_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("1.0 2.0 3.0")]
    [InlineData("1.0 2.0 3.0 4.0 5.0")]
    [InlineData("abc def ghi jkl")]
    [InlineData("")]
    public void Invalid_float4_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Theory]
    [InlineData("1.0, 0.5, 0.0, 1.0,")]
    [InlineData("1.0 0.5 0.0 1.0 ")]
    public void Trailing_separator_is_tolerated(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Non_float4_tag_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}