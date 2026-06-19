// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class CrossLayerShadowHandlerTest
{
    private static readonly CrossLayerShadowHandler Sut = new();

    [Fact]
    public void Handle_EmitsWarningWithSymbolIdAndLayerName()
    {
        var fact = new XmlLayerShadowFact("file:///leaf.xml", 5, 0, 0, "UNIT_A", "Core");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("UNIT_A", d.Message);
        Assert.Contains("Core", d.Message);
    }

    [Fact]
    public void Handle_MessageContainsSuppressionHint()
    {
        var fact = new XmlLayerShadowFact("file:///leaf.xml", 0, 0, 0, "MY_SYMBOL", "BaseGame");
        var result = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Contains("Override", result.Message);
        Assert.Contains("MY_SYMBOL", result.Message);
    }
}