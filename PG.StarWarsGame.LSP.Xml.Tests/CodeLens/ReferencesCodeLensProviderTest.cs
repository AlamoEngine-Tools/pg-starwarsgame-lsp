// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.CodeLens;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeLens;

public sealed class ReferencesCodeLensProviderTest
{
    private const string DocUri = "file:///units.xml";
    private const string RefUri = "file:///other.xml";

    private static GameSymbol SymAt(string id, int line)
        => new(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(DocUri, line, null), null);

    private static GameReference Ref(string id, int line)
        => new(id, GameSymbolKind.XmlObject, "Unit", RefUri, line, 0, 5);

    private static GameIndex IndexWithRefs(GameSymbol symbol, params GameReference[] refs)
    {
        var docIndex = new DocumentIndex(DocUri, 1, [symbol], []);
        var allRefs = refs.Length > 0
            ? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add(symbol.Id, refs.ToImmutableArray())
            : ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty;
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(DocUri, docIndex),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            allRefs);
    }

    private static CodeLensSymbolContext MakeCtx(GameSymbol symbol, GameIndex index)
    {
        var origin = (FileOrigin)symbol.Origin;
        return new CodeLensSymbolContext(symbol, origin, index);
    }

    [Fact]
    public void NoReferences_ReturnsLensWithZeroTitle()
    {
        var sym = SymAt("Unit_A", 5);
        var index = IndexWithRefs(sym);
        var provider = new ReferencesCodeLensProvider();

        var lens = provider.Handle(MakeCtx(sym, index));

        Assert.NotNull(lens);
        Assert.Equal("0 references", lens!.Command!.Title);
        Assert.Equal(5, lens.Range.Start.Line);
    }

    [Fact]
    public void OneReference_ReturnsLensWithSingular()
    {
        var sym = SymAt("Unit_B", 10);
        var index = IndexWithRefs(sym, Ref("Unit_B", 3));
        var provider = new ReferencesCodeLensProvider();

        var lens = provider.Handle(MakeCtx(sym, index));

        Assert.NotNull(lens);
        Assert.Equal("1 reference", lens!.Command!.Title);
        Assert.Equal("aet-eaw-edit.lsp.showReferences", lens.Command!.Name);
    }

    [Fact]
    public void MultipleReferences_ReturnsLensWithPlural()
    {
        var sym = SymAt("Unit_C", 2);
        var index = IndexWithRefs(sym, Ref("Unit_C", 1), Ref("Unit_C", 2));
        var provider = new ReferencesCodeLensProvider();

        var lens = provider.Handle(MakeCtx(sym, index));

        Assert.NotNull(lens);
        Assert.Equal("2 references", lens!.Command!.Title);
    }

    [Fact]
    public void ZeroReferences_CommandHasNoName()
    {
        var sym = SymAt("Unit_D", 7);
        var index = IndexWithRefs(sym);
        var provider = new ReferencesCodeLensProvider();

        var lens = provider.Handle(MakeCtx(sym, index));

        // With 0 refs the command has no name (no showReferences call makes sense)
        Assert.Null(lens!.Command!.Name);
    }
}
