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
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaHoverHandlerTest
{
    private const string LuaUri = "file:///script.lua";
    private const string LibUri = "file:///lib.lua";

    private static HoverParams HoverAt(int line, int character, string uri = LuaUri)
    {
        return new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static LuaHoverHandler BuildHandler(
        GameIndex index,
        ILuaApiSchemaProvider schema,
        FakeWorkspaceHost? host = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new LuaHoverHandler(
            svc,
            host ?? new FakeWorkspaceHost(),
            new FileHelper(new MockFileSystem()),
            schema,
            NullLogger<LuaHoverHandler>.Instance);
    }

    private static string GetMarkdown(Hover hover) =>
        hover.Contents.MarkupContent?.Value ?? string.Empty;

    // ── gating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonLuaFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, new LuaApiSchemaProvider([]));
        var result = await handler.Handle(HoverAt(0, 0, "file:///test.xml"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NotInWorkspaceHost_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, new LuaApiSchemaProvider([]));
        var result = await handler.Handle(HoverAt(0, 0), CancellationToken.None);
        Assert.Null(result);
    }

    // ── XML reference hover ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnXmlRef_ReturnsHoverWithTypeAndId()
    {
        const string xmlUri = "file:///units.xml";
        const string targetId = "UNIT_A";

        // reference at col 19, length 6 — matches "UNIT_A" in Find_First_Object("UNIT_A")
        var reference = new GameReference(targetId, GameSymbolKind.XmlObject, "Unit", LuaUri, 0, 19, 6);
        var sym = new GameSymbol(targetId, GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(xmlUri, 0, null), null);

        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, docIndex),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(targetId, [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"UNIT_A\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(HoverAt(0, 21), CancellationToken.None);

        Assert.NotNull(result);
        var content = GetMarkdown(result!);
        Assert.Contains("Unit", content);
        Assert.Contains("UNIT_A", content);
    }

    [Fact]
    public async Task Handle_CursorOnXmlRef_RangeMatchesReference()
    {
        const string targetId = "UNIT_A";
        var reference = new GameReference(targetId, GameSymbolKind.XmlObject, null, LuaUri, 0, 19, 6);
        var sym = new GameSymbol(targetId, GameSymbolKind.XmlObject, null,
            new FileOrigin(LuaUri, 0, null), null);

        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, docIndex),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(targetId, [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"UNIT_A\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(HoverAt(0, 21), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result!.Range!.Start.Line);
        Assert.Equal(19, result.Range.Start.Character);
        Assert.Equal(25, result.Range.End.Character); // 19 + 6
    }

    // ── require hover ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnRequireString_Resolved_ReturnsHoverWithFilename()
    {
        const string libFileUri = "file:///data/scripts/library/pgstatemachine.lua";
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []))
                .Add(libFileUri, new DocumentIndex(libFileUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"PGStateMachine\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        // cursor inside "PGStateMachine" at col 12
        var result = await handler.Handle(HoverAt(0, 12), CancellationToken.None);

        Assert.NotNull(result);
        var content = GetMarkdown(result!);
        Assert.Contains("PGStateMachine", content);
        Assert.Contains("pgstatemachine", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_CursorOnRequireString_Unresolved_ReturnsNotFoundHover()
    {
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"DefinitelyNotReal\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(HoverAt(0, 12), CancellationToken.None);

        Assert.NotNull(result);
        var content = GetMarkdown(result!);
        Assert.Contains("not found", content, StringComparison.OrdinalIgnoreCase);
    }

    // ── LuaGlobal identifier hover ────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnWorkspaceLuaGlobal_ReturnsHover()
    {
        var sym = new GameSymbol("RunMission", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("RunMission", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        // cursor on "RunMission" at col 5
        var result = await handler.Handle(HoverAt(0, 5), CancellationToken.None);

        Assert.NotNull(result);
        var content = GetMarkdown(result!);
        Assert.Contains("RunMission", content);
    }

    [Fact]
    public async Task Handle_CursorOnEngineGlobal_ReturnsHoverWithDescription()
    {
        var schema = new LuaApiSchemaProvider(["""
            --- Finds the first game object with the specified type.
            ---@param objectName string
            ---@xmlref XmlObject
            function Find_First_Object(objectName) end
            """]);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"UNIT\")", 1);

        var handler = BuildHandler(GameIndex.Empty, schema, host);
        // cursor on "Find_First_Object" at col 5
        var result = await handler.Handle(HoverAt(0, 5), CancellationToken.None);

        Assert.NotNull(result);
        var content = GetMarkdown(result!);
        Assert.Contains("Find_First_Object", content);
        Assert.Contains("Finds the first game object", content);
    }

    [Fact]
    public async Task Handle_CursorOnLocalVariable_ReturnsNull()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "local myVar = 1", 1);

        var handler = BuildHandler(GameIndex.Empty, new LuaApiSchemaProvider([]), host);
        // cursor on "myVar"
        var result = await handler.Handle(HoverAt(0, 8), CancellationToken.None);

        Assert.Null(result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
            => Task.CompletedTask;

        public void RemoveDocument(string uri) { }
        public void ApplyBaseline(BaselineIndex baseline) { }
        public IDisposable BeginBulkUpdate() => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version) =>
            _docs[uri] = new TrackedDocument(uri, text, version);

        public void Remove(string uri) => _docs.Remove(uri);

        public bool TryGet(string uri, out TrackedDocument doc) =>
            _docs.TryGetValue(uri, out doc!);

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}
