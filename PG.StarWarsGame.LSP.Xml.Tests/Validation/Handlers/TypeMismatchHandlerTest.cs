// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class TypeMismatchHandlerTest
{
    private static readonly TypeMismatchHandler Sut = new();

    private static GameSymbol MakeSymbol(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///a.xml", 0, 0), null);
    }

    [Fact]
    public void Wrong_type_emits_warning_with_id_and_types()
    {
        var sym = MakeSymbol("X1", "GroundUnit");
        var fact = new XmlReferenceFact("file:///test.xml", 1, 0, 2, "X1", sym, "SpaceUnit");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("X1", d.Message);
        Assert.Contains("SpaceUnit", d.Message);
        Assert.Contains("GroundUnit", d.Message);
    }

    [Fact]
    public void Correct_type_emits_no_diagnostics()
    {
        var sym = MakeSymbol("X1", "SpaceUnit");
        var fact = new XmlReferenceFact("file:///test.xml", 1, 0, 2, "X1", sym, "SpaceUnit");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Unresolved_symbol_emits_no_diagnostics()
    {
        var fact = new XmlReferenceFact("file:///test.xml", 1, 0, 5, "Missing", null, "SpaceUnit");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void No_expected_type_emits_no_diagnostics()
    {
        var sym = MakeSymbol("X1", "SpaceUnit");
        var fact = new XmlReferenceFact("file:///test.xml", 1, 0, 2, "X1", sym, null);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }
}