// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlPositionResolverTest
{
    private static GameReference MakeRef(string id, int line, int col, int len, string uri = "file:///a.xml")
    {
        return new GameReference(id, GameSymbolKind.XmlObject, "Unit", uri, line, col, len);
    }

    private static GameSymbol MakeSym(string id, int line, int? col = null, string uri = "file:///a.xml")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(uri, line, col), null);
    }

    private static DocumentIndex EmptyDoc()
    {
        return new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty);
    }

    // ── no match ──────────────────────────────────────────────────────────────

    [Fact]
    public void FindAtPosition_EmptyIndex_ReturnsNull()
    {
        var result = XmlPositionResolver.FindAtPosition(EmptyDoc(), 0, 5);
        Assert.Null(result);
    }

    [Fact]
    public void FindAtPosition_CursorOnWrongLine_ReturnsNull()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef("UNIT_A", 2, 4, 6)));

        var result = XmlPositionResolver.FindAtPosition(doc, 0, 4);
        Assert.Null(result);
    }

    [Fact]
    public void FindAtPosition_CursorJustBeforeReference_ReturnsNull()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef("UNIT_A", 0, 10, 6)));

        // col 9 is one before the reference at col 10
        var result = XmlPositionResolver.FindAtPosition(doc, 0, 9);
        Assert.Null(result);
    }

    [Fact]
    public void FindAtPosition_CursorJustAfterReference_ReturnsNull()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef("UNIT_A", 0, 10, 6)));

        // col 16 is one past the end of a 6-char ref starting at col 10
        var result = XmlPositionResolver.FindAtPosition(doc, 0, 16);
        Assert.Null(result);
    }

    // ── reference hits ────────────────────────────────────────────────────────

    [Fact]
    public void FindAtPosition_CursorAtStartOfReference_ReturnsId()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef("UNIT_A", 3, 10, 6)));

        var result = XmlPositionResolver.FindAtPosition(doc, 3, 10);

        Assert.NotNull(result);
        Assert.Equal("UNIT_A", result!.Value.Id);
    }

    [Fact]
    public void FindAtPosition_CursorInsideReference_ReturnsId()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef("UNIT_A", 1, 5, 6)));

        var result = XmlPositionResolver.FindAtPosition(doc, 1, 8);

        Assert.NotNull(result);
        Assert.Equal("UNIT_A", result!.Value.Id);
    }

    [Fact]
    public void FindAtPosition_CursorAtLastCharOfReference_ReturnsId()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef("UNIT_A", 0, 4, 6)));

        // last char is at col 9 (4 + 6 - 1)
        var result = XmlPositionResolver.FindAtPosition(doc, 0, 9);

        Assert.NotNull(result);
        Assert.Equal("UNIT_A", result!.Value.Id);
    }

    [Fact]
    public void FindAtPosition_Reference_ReturnsExactRange()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef("UNIT_A", 2, 7, 6)));

        var result = XmlPositionResolver.FindAtPosition(doc, 2, 9);

        Assert.NotNull(result);
        var range = result!.Value.Range;
        Assert.Equal(new Position(2, 7), range.Start);
        Assert.Equal(new Position(2, 13), range.End);
    }

    [Fact]
    public void FindAtPosition_TwoRefsOnSameLine_ReturnsCorrectOne()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(
                MakeRef("UNIT_A", 0, 4, 6),
                MakeRef("UNIT_B", 0, 14, 6)));

        var onFirst = XmlPositionResolver.FindAtPosition(doc, 0, 5);
        var onSecond = XmlPositionResolver.FindAtPosition(doc, 0, 17);

        Assert.Equal("UNIT_A", onFirst!.Value.Id);
        Assert.Equal("UNIT_B", onSecond!.Value.Id);
    }

    // ── definition hits ───────────────────────────────────────────────────────

    [Fact]
    public void FindAtPosition_CursorOnDefinitionLine_ReturnsSymbolId()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray.Create(MakeSym("UNIT_A", 5)),
            ImmutableArray<GameReference>.Empty);

        var result = XmlPositionResolver.FindAtPosition(doc, 5, 0);

        Assert.NotNull(result);
        Assert.Equal("UNIT_A", result!.Value.Id);
    }

    [Fact]
    public void FindAtPosition_CursorOnDifferentLineThanDefinition_ReturnsNull()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray.Create(MakeSym("UNIT_A", 5)),
            ImmutableArray<GameReference>.Empty);

        var result = XmlPositionResolver.FindAtPosition(doc, 6, 0);

        Assert.Null(result);
    }

    // ── reference takes priority over definition on same line ─────────────────

    [Fact]
    public void FindAtPosition_ReferenceAndDefinitionOnSameLine_ReferenceWins()
    {
        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray.Create(MakeSym("UNIT_DEF", 0)),
            ImmutableArray.Create(MakeRef("UNIT_REF", 0, 4, 8)));

        // cursor within the reference span
        var result = XmlPositionResolver.FindAtPosition(doc, 0, 6);

        Assert.NotNull(result);
        Assert.Equal("UNIT_REF", result!.Value.Id);
    }
}