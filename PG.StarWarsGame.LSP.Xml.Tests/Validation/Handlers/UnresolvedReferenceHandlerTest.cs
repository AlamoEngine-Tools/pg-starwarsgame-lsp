// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class UnresolvedReferenceHandlerTest
{
    private static readonly UnresolvedReferenceHandler Sut = new();

    private static GameSymbol MakeSymbol(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///a.xml", 0, 0), null);
    }

    [Fact]
    public void Unresolved_reference_emits_error_with_target_id()
    {
        var fact = new XmlReferenceFact("file:///test.xml", 1, 0, 5, "MissingUnit", null, "SpaceUnit");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("MissingUnit", d.Message);
    }

    [Fact]
    public void Resolved_reference_emits_no_diagnostics()
    {
        var sym = MakeSymbol("X1", "SpaceUnit");
        var fact = new XmlReferenceFact("file:///test.xml", 1, 0, 2, "X1", sym, "SpaceUnit");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unresolved_with_no_expected_type_still_emits_error()
    {
        var fact = new XmlReferenceFact("file:///test.xml", 1, 0, 5, "Missing", null, null);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }
}