// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class DuplicateSymbolHandlerTest
{
    private static readonly DuplicateSymbolHandler Sut = new();

    private static GameSymbol MakeSymbol(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(uri, line, 0), null);
    }

    [Fact]
    public void Duplicate_in_another_file_emits_error_with_id_and_other_uri()
    {
        var sym1 = MakeSymbol("X1", "file:///a.xml", 2);
        var sym2 = MakeSymbol("X1", "file:///b.xml", 5);
        var fact = new XmlSymbolFact("file:///a.xml", 2, 0, 0, "X1", [sym1, sym2]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("X1", d.Message);
        Assert.Contains("b.xml", d.Message);
    }

    [Fact]
    public void Multiple_other_definitions_all_listed()
    {
        var sym1 = MakeSymbol("X1", "file:///a.xml", 2);
        var sym2 = MakeSymbol("X1", "file:///b.xml", 5);
        var sym3 = MakeSymbol("X1", "file:///c.xml", 8);
        var fact = new XmlSymbolFact("file:///a.xml", 2, 0, 0, "X1", [sym1, sym2, sym3]);
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Contains("b.xml", d.Message);
        Assert.Contains("c.xml", d.Message);
    }
}