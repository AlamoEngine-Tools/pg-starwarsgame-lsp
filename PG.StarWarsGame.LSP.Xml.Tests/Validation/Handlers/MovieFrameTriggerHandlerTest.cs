// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class MovieFrameTriggerHandlerTest
{
    private static readonly MovieFrameTriggerHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Caption_Frame", XmlValueType.MovieFrameTrigger);

    [Theory]
    [InlineData("TEXT_INTRO, 100")]
    [InlineData("SFX_EXPLOSION,250")]
    [InlineData("EVENT_KEY, 0")]
    public void Valid_pair_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TEXT_INTRO")]
    [InlineData(",100")]
    [InlineData("TEXT_INTRO, not_an_int")]
    [InlineData("TEXT_INTRO, -1")]
    [InlineData("TEXT_INTRO,")]
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
    public void FloatFrame_ReturnsWarningAtFrameToken_WithSuggestedFix()
    {
        // All number types accept a float where an integer is expected, with a Warning.
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Key_A, 20.0"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("20", d.SuggestedFix);
        Assert.Equal(7, d.OverrideColumn);
    }
}
