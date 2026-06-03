// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class NonEmptyReferenceHandlerBaseTest
{
    private static readonly TestReferenceHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("X", XmlValueType.FactionReference);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_value_emits_error_with_noun(string value)
    {
        var d = Assert.Single(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx));
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal("'' is not a valid widget reference for <X>.", d.Message);
    }

    [Theory]
    [InlineData("REBEL")]
    [InlineData("  REBEL  ")]
    public void Non_empty_value_returns_no_diagnostics(string value)
    {
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Wrong_value_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx));
    }

    private sealed class TestReferenceHandler : NonEmptyReferenceHandlerBase
    {
        protected override XmlValueType TargetType => XmlValueType.FactionReference;
        protected override string ReferenceNoun => "widget reference";
    }
}