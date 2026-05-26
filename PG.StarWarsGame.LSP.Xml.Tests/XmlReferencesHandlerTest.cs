// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlReferencesHandlerTest
{
    private const string TestUri = "file:///test.xml";
    private const string OtherUri = "file:///other.xml";

    private static ReferenceParams At(int line, int character, bool includeDeclaration = false, string uri = TestUri)
    {
        return new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character),
            Context = new ReferenceContext { IncludeDeclaration = includeDeclaration }
        };
    }

    private static GameReference MakeRef(string id, string docUri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, "Unit", docUri, line, col, len);
    }

    private static GameSymbol SymbolAt(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri, line, null), null);
    }

    private static DocumentIndex DocWithRef(string uri, string refId, int line, int col, int len)
    {
        return new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef(refId, uri, line, col, len)));
    }

    private static GameIndex BuildIndex(
        DocumentIndex? callerDoc = null,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? allRefs = null,
        ImmutableDictionary<string, ImmutableArray<GameSymbol>>? allDefs = null)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        if (callerDoc is not null)
            docs = docs.Add(callerDoc.DocumentUri, callerDoc);

        return new GameIndex(
            BaselineIndex.Empty,
            docs,
            allDefs ?? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            allRefs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static XmlReferencesHandler BuildHandler(GameIndex index, IEaWXmlContext? ctx = null,
        IFileHelper? fileHelper = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new XmlReferencesHandler(svc, NullLogger<XmlReferencesHandler>.Instance,
            ctx ?? new AllowAllEaWContext(),
            fileHelper ?? new FileHelper(new MockFileSystem()));
    }

    // ── null / miss cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoDocumentInIndex_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(At(0, 0), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NoCursorHit_ReturnsNull()
    {
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));
        var result = await handler.Handle(At(0, 0), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SymbolWithNoReferences_ReturnsEmptyList()
    {
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 0)),
            ImmutableArray<GameReference>.Empty);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 0)));
        var handler = BuildHandler(BuildIndex(doc, allDefs: defs));

        var result = await handler.Handle(At(0, 0), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    // ── reference lookup ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnReference_ReturnsAllRefs()
    {
        var callerDoc = DocWithRef(TestUri, "UNIT_A", 0, 4, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(
                MakeRef("UNIT_A", TestUri, 0, 4, 6),
                MakeRef("UNIT_A", OtherUri, 2, 8, 6)));
        var handler = BuildHandler(BuildIndex(callerDoc, refs));

        var result = await handler.Handle(At(0, 5), CancellationToken.None);

        Assert.NotNull(result);
        var locations = result!.ToList();
        Assert.Equal(2, locations.Count);
        Assert.Contains(locations, l => l.Uri.ToString() == TestUri && l.Range.Start.Line == 0);
        Assert.Contains(locations, l => l.Uri.ToString() == OtherUri && l.Range.Start.Line == 2);
    }

    [Fact]
    public async Task Handle_ReferenceRange_MatchesColumnAndLength()
    {
        var callerDoc = DocWithRef(TestUri, "UNIT_A", 1, 7, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", TestUri, 1, 7, 6)));
        var handler = BuildHandler(BuildIndex(callerDoc, refs));

        var result = await handler.Handle(At(1, 8), CancellationToken.None);

        Assert.NotNull(result);
        var loc = Assert.Single(result!);
        Assert.Equal(1, loc.Range.Start.Line);
        Assert.Equal(7, loc.Range.Start.Character);
        Assert.Equal(1, loc.Range.End.Line);
        Assert.Equal(13, loc.Range.End.Character);
    }

    // ── from definition site ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnDefinition_ReturnsAllRefs()
    {
        var defDoc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 0)),
            ImmutableArray<GameReference>.Empty);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", OtherUri, 3, 5, 6)));
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 0)));
        var handler = BuildHandler(BuildIndex(defDoc, refs, defs));

        var result = await handler.Handle(At(0, 0), CancellationToken.None);

        Assert.NotNull(result);
        var loc = Assert.Single(result!);
        Assert.Equal(OtherUri, loc.Uri.ToString());
    }

    // ── include declaration ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_IncludeDeclaration_IncludesDefinitionLocation()
    {
        var callerDoc = DocWithRef(TestUri, "UNIT_A", 0, 4, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", TestUri, 0, 4, 6)));
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", OtherUri, 5)));
        var handler = BuildHandler(BuildIndex(callerDoc, refs, defs));

        var result = await handler.Handle(At(0, 5, true), CancellationToken.None);

        Assert.NotNull(result);
        var locations = result!.ToList();
        Assert.Equal(2, locations.Count);
        Assert.Contains(locations, l => l.Uri.ToString() == OtherUri && l.Range.Start.Line == 5);
        Assert.Contains(locations, l => l.Uri.ToString() == TestUri && l.Range.Start.Line == 0);
    }

    [Fact]
    public async Task Handle_ExcludeDeclaration_DoesNotIncludeDefinitionLocation()
    {
        var callerDoc = DocWithRef(TestUri, "UNIT_A", 0, 4, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", TestUri, 0, 4, 6)));
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", OtherUri, 5)));
        var handler = BuildHandler(BuildIndex(callerDoc, refs, defs));

        var result = await handler.Handle(At(0, 5), CancellationToken.None);

        Assert.NotNull(result);
        var locations = result!.ToList();
        Assert.Single(locations);
        Assert.DoesNotContain(locations, l => l.Uri.ToString() == OtherUri);
    }

    // ── URI normalization ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MixedCaseUri_NormalizesBeforeIndexLookup()
    {
        // Index keys are lowercase (canonical). LSP client on Windows may send uppercase drive letter.
        const string lowercaseUri = "file:///d:/units.xml";
        const string mixedCaseUri = "file:///D:/units.xml";

        var callerDoc = DocWithRef(lowercaseUri, "UNIT_A", 0, 4, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", lowercaseUri, 0, 4, 6)));
        var handler = BuildHandler(BuildIndex(callerDoc, refs));

        var result = await handler.Handle(At(0, 5, uri: mixedCaseUri), CancellationToken.None);

        Assert.NotNull(result);
        var locations = result!.ToList();
        Assert.Single(locations);
    }

    // ── EaW directory gating ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, new DenyAllEaWContext());
        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
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