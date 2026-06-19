// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class CrossTypeShadowHandlerTest
{
    private static readonly CrossTypeShadowHandler Sut = new();

    [Fact]
    public void Handle_EmitsWarningWithBothTypeNames()
    {
        var fact = new XmlCrossTypeShadowFact("file:///leaf.xml", 0, 0, 0, "REBEL", "Unit", "Faction");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("REBEL", d.Message);
        Assert.Contains("Unit", d.Message);
        Assert.Contains("Faction", d.Message);
    }

    [Fact]
    public void Handle_MessageContainsSuppressionHint()
    {
        var fact = new XmlCrossTypeShadowFact("file:///leaf.xml", 0, 0, 0, "MY_SYMBOL", "TypeA", "TypeB");
        var result = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Contains("Override", result.Message);
        Assert.Contains("MY_SYMBOL", result.Message);
    }
}