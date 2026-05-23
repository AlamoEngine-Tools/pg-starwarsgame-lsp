// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class FloatValueHandlerTest
{
    private static readonly FloatValueHandler Sut = new();
    private static readonly XmlTagDefinition FloatTag = XmlHandlerTestFixtures.MakeTag("Max_Speed", XmlValueType.Float);

    [Theory]
    [InlineData("1.23")]
    [InlineData("1.23f")]
    [InlineData("1.23F")]
    [InlineData("-5")]
    [InlineData("1500")]
    [InlineData("0.0")]
    [InlineData(".5")]
    [InlineData("-40.0")]
    public void Valid_float_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(FloatTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("")]
    [InlineData("1.0ff")]
    public void Invalid_float_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(FloatTag, value), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Non_float_tag_returns_no_diagnostics()
    {
        var intTag = XmlHandlerTestFixtures.MakeTag("Count", XmlValueType.Int);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(intTag, "abc"), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }
}