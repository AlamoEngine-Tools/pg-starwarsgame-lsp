// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class IntFloatTupleListHandlerTest
{
    private static readonly IntFloatTupleListHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Range_Table", XmlValueType.IntFloatTupleList);

    [Theory]
    [InlineData("1 0.5")]
    [InlineData("1, 0.5, 2, 1.0")]
    [InlineData("10 2.5 20 3.0")]
    public void Valid_int_float_pairs_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1 2 3")]
    [InlineData("not_an_int 0.5")]
    [InlineData("1 not_a_float")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Theory]
    [InlineData("1, 0.5,")]
    [InlineData("1 0.5 2 1.0 ")]
    public void Trailing_separator_is_tolerated(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    // ── float-in-int-slot policy: accept with Warning (the game truncates) ──

    [Fact]
    public void FloatInIntSlot_ReturnsWarningAtThatToken_WithSuggestedFix()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "10.5, 0.8, 20, 0.9"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("10.5", d.Message);
        Assert.Equal("10", d.SuggestedFix);
        Assert.Equal(0, d.OverrideColumn);
        Assert.Equal(4, d.OverrideLength);
    }

    [Fact]
    public void NonNumericIntSlot_ReturnsErrorAtThatToken()
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "10, 0.8, abc, 0.9"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("abc", d.Message);
        Assert.Equal(9, d.OverrideColumn);
    }
}