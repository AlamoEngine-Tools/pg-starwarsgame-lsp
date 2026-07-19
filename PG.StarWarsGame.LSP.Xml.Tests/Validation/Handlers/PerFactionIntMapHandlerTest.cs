// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class PerFactionIntMapHandlerTest
{
    private static readonly PerFactionIntMapHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Max_Political_Control", XmlValueType.PerFactionIntMap);

    [Theory]
    [InlineData("REBEL, 100")]
    [InlineData("EMPIRE,50")]
    [InlineData("NEUTRAL, -5")]
    public void Valid_pair_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("REBEL")]
    [InlineData(",100")]
    [InlineData("REBEL, not_an_int")]
    [InlineData("REBEL,")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void FloatValue_ReturnsWarningAtValueToken_WithSuggestedFix()
    {
        // All number types accept a float where an integer is expected, with a Warning.
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Empire, 5.0"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("5", d.SuggestedFix);
        Assert.Equal(8, d.OverrideColumn);
    }
}