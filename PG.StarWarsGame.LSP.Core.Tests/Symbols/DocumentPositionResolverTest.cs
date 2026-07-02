// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class DocumentPositionResolverTest
{
    private static readonly Func<GameReference, bool> AllRefs = _ => true;
    private static readonly Func<GameSymbol, int?> IdLengthSizer = s => s.Id.Length;

    private static GameReference MakeRef(
        string id, int line, int col, int len, GameSymbolKind kind = GameSymbolKind.XmlObject,
        string uri = "file:///a.xml")
    {
        return new GameReference(id, kind, "Unit", uri, line, col, len);
    }

    private static GameSymbol MakeSym(
        string id, int line, int? col = null, GameSymbolKind kind = GameSymbolKind.XmlObject,
        string uri = "file:///a.xml")
    {
        return new GameSymbol(id, kind, "Unit", new FileOrigin(uri, line, col), null);
    }

    private static DocumentIndex Doc(
        ImmutableArray<GameSymbol> symbols = default,
        ImmutableArray<GameReference> references = default,
        ImmutableArray<DocumentGroupMembership> groupMemberships = default)
    {
        return new DocumentIndex("file:///a.xml", 1,
            symbols.IsDefault ? ImmutableArray<GameSymbol>.Empty : symbols,
            references.IsDefault ? ImmutableArray<GameReference>.Empty : references,
            GroupMemberships: groupMemberships);
    }

    [Fact]
    public void FindAtPosition_EmptyIndex_ReturnsNull()
    {
        var result = DocumentPositionResolver.FindAtPosition(Doc(), 0, 5, AllRefs, IdLengthSizer);
        Assert.Null(result);
    }

    [Fact]
    public void FindAtPosition_ReferenceMatchesFilter_ReturnsId()
    {
        var doc = Doc(references: ImmutableArray.Create(MakeRef("UNIT_A", 0, 4, 6)));
        var result = DocumentPositionResolver.FindAtPosition(doc, 0, 5, AllRefs, IdLengthSizer);

        Assert.NotNull(result);
        Assert.Equal("UNIT_A", result!.Value.Id);
        Assert.Equal(new Position(0, 4), result.Value.Range.Start);
        Assert.Equal(new Position(0, 10), result.Value.Range.End);
    }

    [Fact]
    public void FindAtPosition_ReferenceFailsFilter_SkipsReference()
    {
        var doc = Doc(references: ImmutableArray.Create(
            MakeRef("UNIT_A", 0, 4, 6, GameSymbolKind.XmlObject)));

        // Filter only accepts LuaGlobal references — the XmlObject ref must be ignored.
        var result = DocumentPositionResolver.FindAtPosition(
            doc, 0, 5, r => r.ExpectedKind == GameSymbolKind.LuaGlobal, IdLengthSizer);

        Assert.Null(result);
    }

    [Fact]
    public void FindAtPosition_CursorOnGroupKeyTag_ReturnsGroupKey()
    {
        var membership = new DocumentGroupMembership(
            new GroupMembership("GROUP_A", "Unit", new FileOrigin("file:///a.xml", 0, 0)),
            TagLine: 2, TagColumn: 8, TagLength: 7);
        var doc = Doc(groupMemberships: ImmutableArray.Create(membership));

        var result = DocumentPositionResolver.FindAtPosition(doc, 2, 10, AllRefs, IdLengthSizer);

        Assert.NotNull(result);
        Assert.Equal("GROUP_A", result!.Value.Id);
        Assert.Equal(new Position(2, 8), result.Value.Range.Start);
        Assert.Equal(new Position(2, 15), result.Value.Range.End);
    }

    [Fact]
    public void FindAtPosition_SymbolSizerReturnsNull_SkipsSymbol()
    {
        var doc = Doc(symbols: ImmutableArray.Create(MakeSym("UNIT_A", 5)));

        // Sizer rejects every symbol — must fall through to no match.
        var result = DocumentPositionResolver.FindAtPosition(doc, 5, 0, AllRefs, _ => null);

        Assert.Null(result);
    }

    [Fact]
    public void FindAtPosition_SymbolSizerReturnsZero_ReturnsZeroWidthRange()
    {
        var doc = Doc(symbols: ImmutableArray.Create(MakeSym("GLOBAL_A", 5, 3)));

        var result = DocumentPositionResolver.FindAtPosition(doc, 5, 0, AllRefs, _ => 0);

        Assert.NotNull(result);
        Assert.Equal("GLOBAL_A", result!.Value.Id);
        Assert.Equal(new Position(5, 3), result.Value.Range.Start);
        Assert.Equal(new Position(5, 3), result.Value.Range.End);
    }

    [Fact]
    public void FindAtPosition_ReferenceTakesPriorityOverSymbolOnSameLine()
    {
        var doc = Doc(
            symbols: ImmutableArray.Create(MakeSym("UNIT_DEF", 0)),
            references: ImmutableArray.Create(MakeRef("UNIT_REF", 0, 4, 8)));

        var result = DocumentPositionResolver.FindAtPosition(doc, 0, 6, AllRefs, IdLengthSizer);

        Assert.NotNull(result);
        Assert.Equal("UNIT_REF", result!.Value.Id);
    }
}
