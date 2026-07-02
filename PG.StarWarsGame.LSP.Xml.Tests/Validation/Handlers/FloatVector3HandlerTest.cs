// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class FloatVector3HandlerTest
{
    private static readonly FloatVector3Handler Sut = new();

    private static readonly XmlTagDefinition
        Tag = XmlHandlerTestFixtures.MakeTag("Position", XmlValueType.FloatVector3);

    [Theory]
    [InlineData("1.0 2.0 3.0")]
    [InlineData("1.0, 2.0, 3.0")]
    [InlineData("-1.5 3.0f 0.0")]
    public void Valid_float3_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("1.0 2.0")]
    [InlineData("1.0 2.0 3.0 4.0")]
    [InlineData("abc def ghi")]
    [InlineData("")]
    public void Invalid_float3_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Theory]
    [InlineData("1.0, 2.0, 3.0,")]
    [InlineData("1.0 2.0 3.0 ")]
    public void Trailing_separator_is_tolerated(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Non_float3_tag_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}