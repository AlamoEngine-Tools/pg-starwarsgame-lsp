// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Diagnostics;

public sealed class LuaDiagnosticsPublisherTest
{
    private const string LuaUri = "file:///script.lua";
    private const string XmlUri = "file:///units.xml";

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (LuaDiagnosticsPublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeGameIndexService indexService,
        FakeGameWorkspaceHost workspaceHost) Build(
            ILuaApiSchemaProvider? schema = null)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var fileHelper = new FileHelper(new MockFileSystem());
        var publisher = new LuaDiagnosticsPublisher(
            p => published.Add(p),
            indexService,
            workspaceHost,
            fileHelper,
            schema ?? new LuaApiSchemaProvider([]),
            NullLogger<LuaDiagnosticsPublisher>.Instance);
        return (publisher, published, indexService, workspaceHost);
    }

    private static GameIndex IndexWithLuaRef(string documentUri, string targetId,
        string? expectedTypeName = null, GameSymbol? resolvedSymbol = null)
    {
        var reference = new GameReference(targetId, GameSymbolKind.XmlObject, expectedTypeName,
            documentUri, 0, 20, targetId.Length);
        var doc = new DocumentIndex(documentUri, 1, [], [reference]);

        var definitions = resolvedSymbol is not null
            ? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add(targetId, [resolvedSymbol])
            : ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty;

        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(documentUri, doc),
            definitions,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    // ── file-type filtering ───────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_XmlFileOpen_NoPublish()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(XmlUri, "<Root/>");

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void OnIndexChanged_NoOpenLuaFiles_NoPublish()
    {
        var (_, published, indexService, _) = Build();

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    // ── unresolved XML references ─────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_UnresolvedXmlRef_EmitsErrorDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("UNIT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "UNIT_MISSING");

        indexService.Fire(index);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("UNIT_MISSING", diag.Message);
        Assert.Contains("no object with this name exists", diag.Message);
    }

    [Fact]
    public void OnIndexChanged_ResolvedRef_NoDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("UNIT_A")""");
        var symbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, null,
            new FileOrigin(XmlUri, 0, null), null);
        var index = IndexWithLuaRef(LuaUri, "UNIT_A", resolvedSymbol: symbol);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public void OnIndexChanged_TypeMismatch_EmitsErrorDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_Player("UNIT_A")""");
        var symbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(XmlUri, 0, null), null);
        var index = IndexWithLuaRef(LuaUri, "UNIT_A",
            "Faction", symbol);

        indexService.Fire(index);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("UNIT_A", diag.Message);
        Assert.Contains("Faction", diag.Message);
    }

    [Fact]
    public void OnIndexChanged_TypeMismatch_GameObjectType_NoDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Spawn_Object("UNIT_A")""");
        var symbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(XmlUri, 0, null), null);
        // GameObjectType is a wildcard — any XmlObject matches, no diagnostic expected.
        var index = IndexWithLuaRef(LuaUri, "UNIT_A", "GameObjectType", symbol);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public void OnIndexChanged_ResolvedRef_MatchingType_NoDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_Player("REBEL")""");
        var symbol = new GameSymbol("REBEL", GameSymbolKind.XmlObject, "Faction",
            new FileOrigin(XmlUri, 0, null), null);
        var index = IndexWithLuaRef(LuaUri, "REBEL",
            "Faction", symbol);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public void OnIndexChanged_DiagnosticRange_MatchesReference_LineAndColumn()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("UNIT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "UNIT_MISSING");

        indexService.Fire(index);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(0, diag.Range.Start.Line);
        Assert.Equal(20, diag.Range.Start.Character);
        Assert.Equal(0, diag.Range.End.Line);
        Assert.Equal(20 + "UNIT_MISSING".Length, diag.Range.End.Character);
    }

    // ── syntax errors ────────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_LuaSyntaxError_EmitsErrorDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        // Incomplete function — a real Lua syntax error
        workspaceHost.Set(LuaUri, "function Foo(");
        var doc = new DocumentIndex(LuaUri, 1, [], []);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        Assert.Contains(pub.Diagnostics!, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OnIndexChanged_ValidLua_NoSyntaxDiagnostics()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, "function Definitions() end");
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public void OnIndexChanged_LuaSyntaxError_DiagnosticCodeIsLorrettaId()
    {
        // Loretta diagnostic IDs (e.g. "LUA1003") should be surfaced as the LSP Code field
        // so users and editors can identify and look up the exact Loretta diagnostic.
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, "function Foo(");
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        var errorDiags = pub.Diagnostics!.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errorDiags);
        Assert.All(errorDiags, d =>
        {
            Assert.True(d.Code?.IsString == true, $"Code should be a string Loretta ID, was: {d.Code}");
            Assert.StartsWith("LUA", d.Code?.String);
        });
    }

    // ── import (require) errors ───────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_MissingRequire_EmitsError()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """require("MissingLib")""");

        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        Assert.Contains(pub.Diagnostics!,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("MissingLib"));
    }

    [Fact]
    public void OnIndexChanged_RequireResolved_NoImportError()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """require("PGStateMachine")""");

        const string libUri = "file:///data/scripts/library/pgstatemachine.lua";
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []))
                .Add(libUri, new DocumentIndex(libUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        // No import error; the unused-require hint (L-6) is acceptable.
        Assert.DoesNotContain(Assert.Single(published).Diagnostics!,
            d => d.Severity == DiagnosticSeverity.Error);
    }

    // ── global-scope analysis (L-6) ──────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_UsesGlobalFromUnrequiredFile_EmitsWarning()
    {
        const string libUri = "file:///data/scripts/library/statemachine.lua";
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, "RunStateMachine()");

        var sym = new GameSymbol("RunStateMachine", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(libUri, 0, null), null);
        var libDoc = new DocumentIndex(libUri, 1, [sym], []);
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []))
                .Add(libUri, libDoc),
            GameIndex.Empty.WorkspaceDefinitions.Add("RunStateMachine", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        Assert.Contains(pub.Diagnostics!,
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("RunStateMachine"));
    }

    // ── open/close lifecycle ──────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_ClosedLuaFile_ClearsDiagnostics()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("MISSING")""");
        indexService.Fire(IndexWithLuaRef(LuaUri, "MISSING"));
        published.Clear();

        workspaceHost.Remove(LuaUri);
        indexService.Fire(GameIndex.Empty);

        var clear = Assert.Single(published);
        Assert.Equal(LuaUri, clear.Uri.ToString());
        Assert.Empty(clear.Diagnostics!);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = new();

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public void AddOrUpdate(string uri, string text, int version)
        {
            _docs[uri] = new TrackedDocument(uri, text, version);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;

        public void Set(string uri, string text)
        {
            _docs[uri] = new TrackedDocument(uri, text, 1);
        }
    }

    internal sealed class FakeGameIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add => _indexChanged += value;
            remove => _indexChanged -= value;
        }

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

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        private event Action<GameIndex>? _indexChanged;

        public void Fire(GameIndex index)
        {
            _indexChanged?.Invoke(index);
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