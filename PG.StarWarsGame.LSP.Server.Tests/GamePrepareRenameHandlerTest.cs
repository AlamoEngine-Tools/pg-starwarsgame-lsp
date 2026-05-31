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

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class GamePrepareRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string LuaUri = "file:///script.lua";
    private const string OtherXmlUri = "file:///other.xml";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static PrepareRenameParams PrepareAt(int line, int character, string uri = XmlUri)
    {
        return new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static GameSymbol XmlSymbolAt(string id, string uri, int line, string typeName = "Unit")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(uri, line, null), null);
    }

    private static GameSymbol XmlSymbolInArchive(string id)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new MegArchiveOrigin("data.meg", "units.xml", 0, 0),
            null);
    }

    private static GameSymbol LuaGlobal(string name, string uri, int line = 0, int? col = null)
    {
        return new GameSymbol(name, GameSymbolKind.LuaGlobal, null, new FileOrigin(uri, line, col), null);
    }

    private static GameReference XmlRef(string id, string uri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, "Unit", uri, line, col, len);
    }

    private static GameReference LuaGlobalRef(string id, string uri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.LuaGlobal, null, uri, line, col, len);
    }

    private static GameIndex BuildIndex(
        ImmutableDictionary<string, DocumentIndex>? docs = null,
        ImmutableDictionary<string, ImmutableArray<GameSymbol>>? defs = null,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? refs = null)
    {
        return new GameIndex(
            BaselineIndex.Empty,
            docs ?? ImmutableDictionary<string, DocumentIndex>.Empty,
            defs ?? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            refs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static DocumentIndex XmlDoc(string uri, GameSymbol? sym = null, GameReference? r = null)
    {
        var syms = sym is null ? ImmutableArray<GameSymbol>.Empty : ImmutableArray.Create(sym);
        var refs = r is null ? ImmutableArray<GameReference>.Empty : ImmutableArray.Create(r);
        return new DocumentIndex(uri, 1, syms, refs);
    }

    private static DocumentIndex LuaDocWith(string uri, GameSymbol? sym = null, GameReference? r = null)
    {
        var syms = sym is null ? ImmutableArray<GameSymbol>.Empty : ImmutableArray.Create(sym);
        var refs = r is null ? ImmutableArray<GameReference>.Empty : ImmutableArray.Create(r);
        return new DocumentIndex(uri, 1, syms, refs);
    }

    private static GamePrepareRenameHandler BuildHandler(
        GameIndex index,
        FakeWorkspaceHost? host = null,
        IEaWXmlContext? ctx = null,
        IFileHelper? fileHelper = null)
    {
        return new GamePrepareRenameHandler(
            new FakeIndexService { Current = index },
            host ?? new FakeWorkspaceHost(),
            ctx ?? new AllowAllEaWContext(),
            fileHelper ?? new FileHelper(new MockFileSystem()),
            NullLogger<GamePrepareRenameHandler>.Instance);
    }

    // ── file-type gating ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownFileType_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(PrepareAt(0, 0, "file:///data.txt"), CancellationToken.None);
        Assert.Null(result);
    }

    // ── XML scenarios ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_XmlFile_NotEaWXmlFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, ctx: new DenyAllEaWContext());
        var result = await handler.Handle(PrepareAt(0, 0), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_XmlFile_NoCursorHit_ReturnsNull()
    {
        var doc = XmlDoc(XmlUri);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc));
        var handler = BuildHandler(index);
        var result = await handler.Handle(PrepareAt(0, 0), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_XmlFile_CursorOnReference_ReturnsRange()
    {
        // Reference "UNIT_A" at col 10..16 on line 0
        var r = XmlRef("UNIT_A", XmlUri, 0, 10, 6);
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var doc = XmlDoc(XmlUri, r: r);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc),
            defs);

        var handler = BuildHandler(index);
        var result = await handler.Handle(PrepareAt(0, 12), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Range);
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(10, result.Range.Start.Character);
        Assert.Equal(16, result.Range.End.Character);
    }

    [Fact]
    public async Task Handle_XmlFile_CursorOnSymbol_ReturnsRange()
    {
        // Symbol "UNIT_A" at line 1, no column → defaults to 0, range [1:0, 1:6]
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var doc = XmlDoc(XmlUri, sym);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc),
            defs);

        var handler = BuildHandler(index);
        var result = await handler.Handle(PrepareAt(1, 3), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Range);
        Assert.Equal(1, result.Range!.Start.Line);
        Assert.Equal(0, result.Range.Start.Character);
        Assert.Equal(6, result.Range.End.Character);
    }

    [Fact]
    public async Task Handle_XmlFile_SymbolFromArchive_ReturnsNull()
    {
        var sym = XmlSymbolInArchive("UNIT_VANILLA");
        var r = XmlRef("UNIT_VANILLA", XmlUri, 0, 4, 12);
        var doc = XmlDoc(XmlUri, r: r);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_VANILLA", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc),
            defs);

        var handler = BuildHandler(index);
        var result = await handler.Handle(PrepareAt(0, 5), CancellationToken.None);

        Assert.Null(result);
    }

    // ── Lua: LuaGlobal scenarios ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_LuaFile_CursorOnGlobalRef_ReturnsRange()
    {
        // Reference "RunMission" at col 0..10 on line 0
        var callerRef = LuaGlobalRef("RunMission", LuaUri, 0, 0, 10);
        var sym = LuaGlobal("RunMission", "file:///lib.lua");
        var callerDoc = LuaDocWith(LuaUri, r: callerRef);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("RunMission", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, callerDoc),
            defs);

        var handler = BuildHandler(index);
        var result = await handler.Handle(PrepareAt(0, 5, LuaUri), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Range);
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(0, result.Range.Start.Character);
        Assert.Equal(10, result.Range.End.Character);
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnGlobalDeclaration_ReturnsExtendedRange()
    {
        // Declaration "Foo" at line 0, col 9 (function Foo)
        var sym = LuaGlobal("Foo", LuaUri, 0, 9);
        var defDoc = LuaDocWith(LuaUri, sym);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("Foo", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, defDoc),
            defs);

        var handler = BuildHandler(index);
        var result = await handler.Handle(PrepareAt(0, 9, LuaUri), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Range);
        // Zero-length [0:9,0:9] extended by "Foo".Length = 3 → [0:9, 0:12]
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(9, result.Range.Start.Character);
        Assert.Equal(12, result.Range.End.Character);
    }

    [Fact]
    public async Task Handle_LuaFile_LuaGlobal_NoDocumentInIndex_ReturnsNull()
    {
        var index = BuildIndex();
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var handler = BuildHandler(index, host);
        var result = await handler.Handle(PrepareAt(0, 0, LuaUri), CancellationToken.None);

        Assert.Null(result);
    }

    // ── Lua: XmlObject string literal scenarios ───────────────────────────────

    [Fact]
    public async Task Handle_LuaFile_CursorOnXmlObjectString_ReturnsInnerRange()
    {
        // Spawn("UNIT_A") — "UNIT_A" opens at col 6, value starts at col 7, ends at col 13
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var luaDoc = LuaDocWith(LuaUri);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc),
            defs);

        var handler = BuildHandler(index, host);
        var result = await handler.Handle(PrepareAt(0, 8, LuaUri), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Range);
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(7, result.Range.Start.Character); // after opening quote
        Assert.Equal(13, result.Range.End.Character); // before closing quote
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnXmlObjectString_ArchiveDefinition_ReturnsNull()
    {
        var sym = XmlSymbolInArchive("VANILLA_UNIT");
        var luaDoc = LuaDocWith(LuaUri);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"VANILLA_UNIT\")", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("VANILLA_UNIT", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc),
            defs);

        var handler = BuildHandler(index, host);
        var result = await handler.Handle(PrepareAt(0, 8, LuaUri), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnUnknownString_ReturnsNull()
    {
        var luaDoc = LuaDocWith(LuaUri);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"NOT_A_GAME_OBJECT\")", 1);

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc));

        var handler = BuildHandler(index, host);
        var result = await handler.Handle(PrepareAt(0, 8, LuaUri), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_LuaFile_NotInHostAndNotInIndex_ReturnsNull()
    {
        var index = BuildIndex();
        var handler = BuildHandler(index, new FakeWorkspaceHost());
        var result = await handler.Handle(PrepareAt(0, 0, LuaUri), CancellationToken.None);
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

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version)
        {
            _docs[uri] = new TrackedDocument(uri, text, version);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }

    private sealed class AllowAllEaWContext : IEaWXmlContext
    {
        public bool IsEaWXmlFile(string fileUri)
        {
            return true;
        }
    }

    private sealed class DenyAllEaWContext : IEaWXmlContext
    {
        public bool IsEaWXmlFile(string fileUri)
        {
            return false;
        }
    }
}