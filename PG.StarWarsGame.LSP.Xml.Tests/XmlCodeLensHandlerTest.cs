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
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlCodeLensHandlerTest
{
    private const string TestUri = "file:///test.xml";
    private const string OtherUri = "file:///other.xml";

    private static CodeLensParams ForDoc(string uri = TestUri)
    {
        return new CodeLensParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) }
        };
    }

    private static GameSymbol SymbolAt(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri, line, null), null);
    }

    private static GameSymbol BaselineSymbol(string id)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit",
            new MegArchiveOrigin("data.meg", "units.xml", null, null), null);
    }

    private static GameReference MakeRef(string id, string docUri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, "Unit", docUri, line, col, len);
    }

    private static GameIndex BuildIndex(
        DocumentIndex? doc = null,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? allRefs = null,
        ImmutableDictionary<string, ImmutableArray<GameSymbol>>? allDefs = null)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        if (doc is not null)
            docs = docs.Add(doc.DocumentUri, doc);

        return new GameIndex(
            BaselineIndex.Empty,
            docs,
            allDefs ?? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            allRefs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static XmlCodeLensHandler BuildHandler(GameIndex index, IEaWXmlContext? ctx = null,
        IFileHelper? fileHelper = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new XmlCodeLensHandler(svc, NullLogger<XmlCodeLensHandler>.Instance,
            ctx ?? new AllowAllEaWContext(),
            fileHelper ?? new FileHelper(new MockFileSystem()));
    }

    // ── null / miss cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoDocumentInIndex_ReturnsEmptyContainer()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(ForDoc(), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, new DenyAllEaWContext());
        var result = await handler.Handle(ForDoc(), CancellationToken.None);
        Assert.Null(result);
    }

    // ── symbol iteration ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DocumentWithSymbolAndZeroRefs_ReturnsLensWithZeroCount()
    {
        var sym = SymbolAt("UNIT_A", TestUri, 3);
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal(3, lens.Range.Start.Line);
        Assert.Contains("0", lens.Command!.Title);
    }

    [Fact]
    public async Task Handle_DocumentWithSymbolAndThreeRefs_ReturnsLensWithCountThree()
    {
        var sym = SymbolAt("UNIT_A", TestUri, 1);
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(
                MakeRef("UNIT_A", OtherUri, 0, 4, 6),
                MakeRef("UNIT_A", OtherUri, 2, 4, 6),
                MakeRef("UNIT_A", OtherUri, 4, 4, 6)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Contains("3", lens.Command!.Title);
    }

    [Fact]
    public async Task Handle_MultipleSymbols_ReturnsOneLensPerSymbol()
    {
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(
                SymbolAt("UNIT_A", TestUri, 1),
                SymbolAt("UNIT_B", TestUri, 5),
                SymbolAt("UNIT_C", TestUri, 9)),
            ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        Assert.Equal(3, result!.Count());
    }

    [Fact]
    public async Task Handle_LensRange_PlacedAtDefinitionLine()
    {
        var sym = SymbolAt("UNIT_A", TestUri, 7);
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal(7, lens.Range.Start.Line);
        Assert.Equal(0, lens.Range.Start.Character);
        Assert.Equal(7, lens.Range.End.Line);
    }

    // ── FileOrigin guard ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MegArchiveOriginSymbol_NoLensEmitted()
    {
        var sym = BaselineSymbol("UNIT_BASE");
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        Assert.Empty(result!);
    }

    // ── title formatting ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SingularCount_TitleContainsSingularWord()
    {
        var sym = SymbolAt("UNIT_A", TestUri, 0);
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", OtherUri, 0, 0, 6)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal("1 reference", lens.Command!.Title);
    }

    [Fact]
    public async Task Handle_PluralCount_TitleContainsPluralWord()
    {
        var sym = SymbolAt("UNIT_A", TestUri, 0);
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(
                MakeRef("UNIT_A", OtherUri, 0, 0, 6),
                MakeRef("UNIT_A", OtherUri, 1, 0, 6)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.Equal("2 references", lens.Command!.Title);
    }

    // ── command attachment ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonZeroCount_CommandIsAttached()
    {
        var sym = SymbolAt("UNIT_A", TestUri, 0);
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", OtherUri, 0, 0, 6)));
        var handler = BuildHandler(BuildIndex(doc, refs));

        var result = await handler.Handle(ForDoc(), CancellationToken.None);

        var lens = Assert.Single(result!);
        Assert.NotNull(lens.Command);
        Assert.Equal("aet-eaw-edit.lsp.showReferences", lens.Command!.Name);
        Assert.NotNull(lens.Command.Arguments);
    }

    // ── URI normalization ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MixedCaseUri_NormalizesBeforeIndexLookup()
    {
        const string lowercaseUri = "file:///d:/units.xml";
        const string mixedCaseUri = "file:///D:/units.xml";

        var sym = SymbolAt("UNIT_A", lowercaseUri, 0);
        var doc = new DocumentIndex(lowercaseUri, 1,
            ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));

        var result = await handler.Handle(ForDoc(mixedCaseUri), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!);
    }

    // ── resolve passthrough ───────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ReturnsLensUnchanged()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var lens = new CodeLens { Range = new LspRange(new Position(0, 0), new Position(0, 0)) };

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