// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class Type38HandlerTest
{
    private static readonly Type38Handler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Ambient_SFXEvent_Intermittent", XmlValueType.Type38);

    [Theory]
    [InlineData("SFX_Ambient_Crickets")]
    [InlineData("SFX_Ambient_Wind")]
    public void NonEmpty_ReturnsNoDiagnostics(string value)
    {
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_ReturnsError(string value)
    {
        var d = Assert.Single(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx));
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void WrongType_ReturnsNoDiagnostics()
    {
        var other = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(other, ""), XmlHandlerTestFixtures.EmptyCtx));
    }
}