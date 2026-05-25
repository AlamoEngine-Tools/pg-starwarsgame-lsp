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

public sealed class LuaCompletionHandlerTest
{
    private const string LuaUri = "file:///script.lua";

    private static CompletionParams CompletionAt(int line, int character, string uri = LuaUri)
    {
        return new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static LuaCompletionHandler BuildHandler(
        GameIndex index,
        ILuaApiSchemaProvider schema,
        FakeWorkspaceHost? host = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new LuaCompletionHandler(
            svc,
            host ?? new FakeWorkspaceHost(),
            new FileHelper(new MockFileSystem()),
            schema,
            NullLogger<LuaCompletionHandler>.Instance);
    }

    // ── gating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonLuaFile_ReturnsEmpty()
    {
        var handler = BuildHandler(GameIndex.Empty, new LuaApiSchemaProvider([]));
        var result = await handler.Handle(CompletionAt(0, 0, "file:///test.xml"), CancellationToken.None);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_NotInWorkspaceHost_ReturnsEmpty()
    {
        var handler = BuildHandler(GameIndex.Empty, new LuaApiSchemaProvider([]));
        var result = await handler.Handle(CompletionAt(0, 0), CancellationToken.None);
        Assert.Empty(result.Items);
    }

    // ── API string arg completions ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_InsideApiStringArg_ReturnsMatchingTypeSymbols()
    {
        var schema = new LuaApiSchemaProvider(["""
            ---@param objectName string
            ---@xmlref XmlObject:Unit
            function Find_First_Object(objectName) end
            """]);

        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///units.xml", 0, null), null);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        // Find_First_Object("UNIT")  — cursor at col 21, inside "UNIT"
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"UNIT\")", 1);

        var handler = BuildHandler(index, schema, host);
        var result = await handler.Handle(CompletionAt(0, 21), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "UNIT_A");
    }

    [Fact]
    public async Task Handle_InsideApiStringArg_FiltersOutNonMatchingTypes()
    {
        var schema = new LuaApiSchemaProvider(["""
            ---@param objectName string
            ---@xmlref XmlObject:Unit
            function Find_First_Object(objectName) end
            """]);

        var unitSym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///units.xml", 0, null), null);
        var factionSym = new GameSymbol("FACTION_A", GameSymbolKind.XmlObject, "Faction",
            new FileOrigin("file:///factions.xml", 0, null), null);

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", [unitSym])
                .Add("FACTION_A", [factionSym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        // Find_First_Object("")  — cursor at col 19 inside empty string
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"\")", 1);

        var handler = BuildHandler(index, schema, host);
        var result = await handler.Handle(CompletionAt(0, 19), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "UNIT_A");
        Assert.DoesNotContain(result.Items, i => i.Label == "FACTION_A");
    }

    [Fact]
    public async Task Handle_InsideApiStringArg_AnyType_ReturnsAllXmlObjects()
    {
        var schema = new LuaApiSchemaProvider(["""
            ---@param objectName string
            ---@xmlref XmlObject
            function Find_First_Object(objectName) end
            """]);

        var unitSym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///units.xml", 0, null), null);
        var factionSym = new GameSymbol("FACTION_A", GameSymbolKind.XmlObject, "Faction",
            new FileOrigin("file:///factions.xml", 0, null), null);

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", [unitSym])
                .Add("FACTION_A", [factionSym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"\")", 1);

        var handler = BuildHandler(index, schema, host);
        var result = await handler.Handle(CompletionAt(0, 19), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "UNIT_A");
        Assert.Contains(result.Items, i => i.Label == "FACTION_A");
    }

    // ── require completions ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InsideRequireString_ReturnsLuaFilenames()
    {
        const string libFileUri = "file:///data/scripts/library/pgstatemachine.lua";

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []))
                .Add(libFileUri, new DocumentIndex(libFileUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        // require("")  — cursor at col 9, inside empty string
        host.AddOrUpdate(LuaUri, "require(\"\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 9), CancellationToken.None);

        Assert.Contains(result.Items, i =>
            string.Equals(i.Label, "pgstatemachine", StringComparison.OrdinalIgnoreCase));
    }

    // ── outside string ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_OutsideAnyString_ReturnsEmpty()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var handler = BuildHandler(GameIndex.Empty, new LuaApiSchemaProvider([]), host);
        // cursor on function name at col 5 — not inside any string
        var result = await handler.Handle(CompletionAt(0, 5), CancellationToken.None);

        Assert.Empty(result.Items);
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
