// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaCodeLensHandlerTest
{
    private const string LuaUri = "file:///script.lua";
    private const string OtherUri = "file:///other.lua";

    private static CodeLensParams ForDoc(string uri = LuaUri)
    {
        return new CodeLensParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) }
        };
    }

    private static GameSymbol GlobalAt(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.LuaGlobal, null, new FileOrigin(uri, line, 0), null);
    }

    private static GameReference MakeRef(string id, string docUri, int line)
    {
        return new GameReference(id, GameSymbolKind.LuaGlobal, null, docUri, line, 0, id.Length);
    }

    private static LuaCodeLensHandler BuildHandler(GameIndex index)
    {
        return new LuaCodeLensHandler(
            new FakeIndexService { Current = index },
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaCodeLensHandler>.Instance);
    }

    private static GameIndex BuildIndex(
        DocumentIndex? doc = null,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? allRefs = null)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        if (doc is not null)
            docs = docs.Add(doc.DocumentUri, doc);

        return new GameIndex(
            BaselineIndex.Empty,
            docs,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            allRefs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    // ── gating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonLuaFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(ForDoc("file:///test.xml"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NoDocumentInIndex_ReturnsEmptyContainer()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(ForDoc(), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    // ── symbol iteration ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_GlobalWithZeroReferences_ReturnsLensWithZeroCount()
    {
        var sym = GlobalAt("MyFunc", LuaUri, 3);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal(3, lens.Range.Start.Line);
        Assert.Contains("0", lens.Command!.Title);
    }

    [Fact]
    public async Task Handle_GlobalWithReferences_ReturnsLensWithCount()
    {
        var sym = GlobalAt("MyFunc", LuaUri, 1);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "MyFunc", ImmutableArray.Create(
                MakeRef("MyFunc", OtherUri, 0),
                MakeRef("MyFunc", OtherUri, 2)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Contains("2", lens.Command!.Title);
    }

    [Fact]
    public async Task Handle_MultipleGlobals_ReturnsOneLensPerGlobal()
    {
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(
                GlobalAt("FuncA", LuaUri, 1),
                GlobalAt("FuncB", LuaUri, 5),
                GlobalAt("FuncC", LuaUri, 9)),
            ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        Assert.Equal(3, result!.Count());
    }

    [Fact]
    public async Task Handle_LensRange_PlacedAtDefinitionLine()
    {
        var sym = GlobalAt("MyFunc", LuaUri, 7);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal(7, lens.Range.Start.Line);
        Assert.Equal(0, lens.Range.Start.Character);
        Assert.Equal(7, lens.Range.End.Line);
    }

    // ── non-FileOrigin symbols skipped ────────────────────────────────────────

    [Fact]
    public async Task Handle_SymbolWithNonFileOrigin_NoLensEmitted()
    {
        var sym = new GameSymbol("MyFunc", GameSymbolKind.LuaGlobal, null, new UnknownOrigin("test"), null);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        Assert.Empty(result!);
    }

    [Fact]
    public async Task Handle_XmlObjectSymbolInLuaDoc_NoLensEmitted()
    {
        // XmlObject symbols must not produce Lua code lenses
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(LuaUri, 2, 0), null);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        Assert.Empty(result!);
    }

    // ── title formatting ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SingularCount_TitleContainsSingularWord()
    {
        var sym = GlobalAt("MyFunc", LuaUri, 0);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "MyFunc", ImmutableArray.Create(MakeRef("MyFunc", OtherUri, 0)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal("1 reference", lens.Command!.Title);
    }

    [Fact]
    public async Task Handle_PluralCount_TitleContainsPluralWord()
    {
        var sym = GlobalAt("MyFunc", LuaUri, 0);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "MyFunc", ImmutableArray.Create(
                MakeRef("MyFunc", OtherUri, 0),
                MakeRef("MyFunc", OtherUri, 1)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal("2 references", lens.Command!.Title);
    }

    // ── command attachment ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonZeroCount_CommandIsAttached()
    {
        var sym = GlobalAt("MyFunc", LuaUri, 0);
        var doc = new DocumentIndex(LuaUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "MyFunc", ImmutableArray.Create(MakeRef("MyFunc", OtherUri, 0)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.NotNull(lens.Command);
        Assert.Equal("aet-eaw-edit.lsp.showReferences", lens.Command!.Name);
        Assert.NotNull(lens.Command.Arguments);
    }

    // ── resolve passthrough ───────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ReturnsLensUnchanged()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var lens = new CodeLens
        {
            Range = new Range(
                new Position(0, 0), new Position(0, 0))
        };

        var result = await handler.Handle(lens, CancellationToken.None);

        Assert.Same(lens, result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(
            ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(
            ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }
        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}