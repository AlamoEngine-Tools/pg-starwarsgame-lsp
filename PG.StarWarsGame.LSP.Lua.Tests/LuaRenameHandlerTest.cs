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

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaRenameHandlerTest
{
    private const string LuaUri = "file:///script.lua";
    private const string OtherLuaUri = "file:///lib.lua";

    private static RenameParams RenameAt(int line, int character, string newName, string uri = LuaUri)
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character),
            NewName = newName
        };
    }

    private static GameIndex BuildIndex(
        ImmutableDictionary<string, DocumentIndex> docs,
        ImmutableDictionary<string, ImmutableArray<GameSymbol>> defs)
    {
        return new GameIndex(BaselineIndex.Empty, docs, defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static LuaRenameHandler BuildHandler(GameIndex index, FakeWorkspaceHost? host = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new LuaRenameHandler(
            svc,
            host ?? new FakeWorkspaceHost(),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaRenameHandler>.Instance);
    }

    private static GameSymbol MakeGlobal(string name, string uri) =>
        new(name, GameSymbolKind.LuaGlobal, null, new FileOrigin(uri, 0, null), null);

    // ── gating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonLuaFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, new FakeWorkspaceHost());
        var result = await handler.Handle(RenameAt(0, 0, "New", "file:///test.xml"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NotInWorkspaceHost_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, new FakeWorkspaceHost());
        var result = await handler.Handle(RenameAt(0, 0, "New"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorNotOnKnownGlobal_ReturnsNull()
    {
        var docIndex = new DocumentIndex(LuaUri, 1, [], []);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, docIndex),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "local x = 1", 1);

        var handler = BuildHandler(index, host);
        var result = await handler.Handle(RenameAt(0, 6, "y"), CancellationToken.None);
        Assert.Null(result);
    }

    // ── rename from call site ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnCallSite_RenamesDefinitionAndCallSite()
    {
        var sym = MakeGlobal("RunMission", OtherLuaUri);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("RunMission", [sym]);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(OtherLuaUri, new DocumentIndex(OtherLuaUri, 1, [sym], []))
            .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(OtherLuaUri, "function RunMission() end", 1);
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var handler = BuildHandler(BuildIndex(docs, defs), host);
        var result = await handler.Handle(RenameAt(0, 0, "ExecuteMission"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(OtherLuaUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(LuaUri)));
    }

    // ── rename from definition ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnDefinition_RenamesNameToken()
    {
        var sym = MakeGlobal("RunMission", LuaUri);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("RunMission", [sym]);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(LuaUri, new DocumentIndex(LuaUri, 1, [sym], []));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "function RunMission() end", 1);

        var handler = BuildHandler(BuildIndex(docs, defs), host);
        // cursor at "function |RunMission()" → col 9
        var result = await handler.Handle(RenameAt(0, 9, "ExecuteMission"), CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(LuaUri)].ToList();
        Assert.Contains(edits, e => e.NewText == "ExecuteMission" && e.Range.Start.Character == 9);
    }

    [Fact]
    public async Task Handle_DefinitionRenameRange_IsCorrect()
    {
        var sym = MakeGlobal("Foo", LuaUri);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("Foo", [sym]);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(LuaUri, new DocumentIndex(LuaUri, 1, [sym], []));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "function Foo() end", 1); // "Foo" at col 9–11

        var handler = BuildHandler(BuildIndex(docs, defs), host);
        var result = await handler.Handle(RenameAt(0, 10, "Bar"), CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(LuaUri)].ToList();
        var defEdit = Assert.Single(edits);
        Assert.Equal(0, defEdit.Range.Start.Line);
        Assert.Equal(9, defEdit.Range.Start.Character);
        Assert.Equal(12, defEdit.Range.End.Character);
        Assert.Equal("Bar", defEdit.NewText);
    }

    // ── cross-file ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CrossFile_RenamesInAllLuaFiles()
    {
        var sym = MakeGlobal("PlayCutscene", OtherLuaUri);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("PlayCutscene", [sym]);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(OtherLuaUri, new DocumentIndex(OtherLuaUri, 1, [sym], []))
            .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(OtherLuaUri, "function PlayCutscene(n) end", 1);
        host.AddOrUpdate(LuaUri, "PlayCutscene(\"intro\")\nPlayCutscene(\"outro\")", 1);

        var handler = BuildHandler(BuildIndex(docs, defs), host);
        var result = await handler.Handle(RenameAt(0, 0, "ShowCutscene"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(OtherLuaUri)));
        var callerEdits = result.Changes[DocumentUri.From(LuaUri)].ToList();
        Assert.Equal(2, callerEdits.Count);
        Assert.All(callerEdits, e => Assert.Equal("ShowCutscene", e.NewText));
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
