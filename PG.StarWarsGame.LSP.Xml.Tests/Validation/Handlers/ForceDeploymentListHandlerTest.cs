// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class ForceDeploymentListHandlerTest
{
    private static readonly ForceDeploymentListHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Starting_Force", XmlValueType.ForceDeploymentList);

    [Theory]
    [InlineData("REBEL, Yavin4, X_Wing")]
    [InlineData("EMPIRE,Coruscant,TIE_Fighter")]
    public void Valid_values_return_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void ExtraTokensInOneTag_AreAWarningThatOnlyTheFirstEntryIsRead()
    {
        // The engine's force reader takes three tokens per tag and drops the rest: Infantry_B is
        // never placed. One unit per tag is the only shape that works.
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "NEUTRAL, Tatooine, Infantry_A, Infantry_B"),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Infantry_B", d.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("REBEL")]
    [InlineData("REBEL, Yavin4")]
    [InlineData(",Yavin4,X_Wing")]
    [InlineData("REBEL,,X_Wing")]
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
}