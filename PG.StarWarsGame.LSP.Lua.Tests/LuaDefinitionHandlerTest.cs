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

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaDefinitionHandlerTest
{
    private const string LuaUri = "file:///script.lua";
    private const string LibUri = "file:///lib.lua";

    private static DefinitionParams RequestAt(int line, int character, string uri = LuaUri)
    {
        return new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static LuaDefinitionHandler BuildHandler(GameIndex index, FakeWorkspaceHost? host = null)
    {
        return new LuaDefinitionHandler(
            new FakeIndexService { Current = index },
            host ?? new FakeWorkspaceHost(),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaDefinitionHandler>.Instance);
    }

    // ── gating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonLuaFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(RequestAt(0, 0, "file:///test.xml"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NoSymbolAtCursor_ReturnsNull()
    {
        var docIndex = new DocumentIndex(LuaUri, 1, [], []);
        var index = MakeIndex(documents: (LuaUri, docIndex));
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "-- just a comment", 1);

        var result = await BuildHandler(index, host).Handle(RequestAt(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    // ── LuaGlobal reference ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnGlobalReference_ReturnsDefinitionLocation()
    {
        // "Foo" is called at col 0, length 3 in script.lua
        var reference = new GameReference("Foo", GameSymbolKind.LuaGlobal, null, LuaUri, 0, 0, 3);
        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);

        // "Foo" is defined at line 5, col 9 in lib.lua
        var symbol = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null, new FileOrigin(LibUri, 5, 9), null);
        var libDocIndex = new DocumentIndex(LibUri, 1, [symbol], []);

        var index = MakeIndex(
            [("Foo", ImmutableArray.Create(symbol))], (LuaUri, docIndex), (LibUri, libDocIndex));

        var result = await BuildHandler(index).Handle(RequestAt(0, 1), CancellationToken.None);

        var loc = Assert.Single(result!.Select(l => l.Location!));
        Assert.Equal(LibUri, loc.Uri.ToString());
        Assert.Equal(5, loc.Range.Start.Line);
        Assert.Equal(9, loc.Range.Start.Character);
    }

    [Fact]
    public async Task Handle_CursorOnGlobalSymbolItself_ReturnsSelfLocation()
    {
        // cursor is on the "Bar" declaration in lib.lua
        var symbol = new GameSymbol("Bar", GameSymbolKind.LuaGlobal, null, new FileOrigin(LibUri, 2, 9), null);
        var docIndex = new DocumentIndex(LibUri, 1, [symbol], []);
        var index = MakeIndex(
            [("Bar", ImmutableArray.Create(symbol))],
            (LibUri, docIndex));

        var result = await BuildHandler(index).Handle(RequestAt(2, 9, LibUri), CancellationToken.None);

        var loc = Assert.Single(result!.Select(l => l.Location!));
        Assert.Equal(LibUri, loc.Uri.ToString());
        Assert.Equal(2, loc.Range.Start.Line);
        Assert.Equal(9, loc.Range.Start.Character);
    }

    [Fact]
    public async Task Handle_SymbolWithNonFileOrigin_ReturnsNull()
    {
        var reference = new GameReference("Foo", GameSymbolKind.LuaGlobal, null, LuaUri, 0, 0, 3);
        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);

        // Symbol has UnknownOrigin — not navigable
        var symbol = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null, new UnknownOrigin("test"), null);
        var index = MakeIndex(
            [("Foo", ImmutableArray.Create(symbol))],
            (LuaUri, docIndex));

        var result = await BuildHandler(index).Handle(RequestAt(0, 1), CancellationToken.None);
        Assert.Null(result);
    }

    // ── XmlObject string literal go-to ───────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnXmlObjectStringLiteral_FileOrigin_ReturnsXmlDefinitionLocation()
    {
        // "UNIT_A" is referenced as an XmlObject at col 10, length 6 in the Lua file.
        const string xmlUri = "file:///units.xml";
        var reference = new GameReference("UNIT_A", GameSymbolKind.XmlObject, "Unit", LuaUri, 0, 10, 6);
        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);

        // The XML symbol is at line 5, col 0 in units.xml.
        var xmlSymbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit", new FileOrigin(xmlUri, 5, 0), null);
        var index = MakeIndex(
            [("UNIT_A", ImmutableArray.Create(xmlSymbol))],
            (LuaUri, docIndex));

        var result = await BuildHandler(index).Handle(RequestAt(0, 12), CancellationToken.None);

        // Cursor at (0, 12) is inside [10, 16) — must resolve to the XML file.
        var loc = Assert.Single(result!.Select(l => l.Location!));
        Assert.Equal(xmlUri, loc.Uri.ToString());
        Assert.Equal(5, loc.Range.Start.Line);
        Assert.Equal(0, loc.Range.Start.Character);
    }

    [Fact]
    public async Task Handle_CursorOnXmlObjectStringLiteral_MegArchiveOrigin_ReturnsNull()
    {
        // Symbol exists but lives in a packaged .meg archive — cannot navigate.
        var reference = new GameReference("UNIT_A", GameSymbolKind.XmlObject, "Unit", LuaUri, 0, 10, 6);
        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);

        var archiveSymbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new MegArchiveOrigin("data.meg", "units.xml", 5, 0), null);
        var index = MakeIndex(
            [("UNIT_A", ImmutableArray.Create(archiveSymbol))],
            (LuaUri, docIndex));

        var result = await BuildHandler(index).Handle(RequestAt(0, 12), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorOnXmlObjectStringLiteral_SymbolNotInIndex_ReturnsNull()
    {
        // Reference exists in the Lua doc but the symbol was never indexed.
        var reference = new GameReference("UNIT_MISSING", GameSymbolKind.XmlObject, "Unit", LuaUri, 0, 10, 12);
        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);
        var index = MakeIndex(documents: (LuaUri, docIndex));

        var result = await BuildHandler(index).Handle(RequestAt(0, 12), CancellationToken.None);
        Assert.Null(result);
    }

    // ── require() go-to ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnRequireArg_ReturnsTargetFileStart()
    {
        // require("foo/bar") — cursor at col 10 (inside the string)
        const string fooBarUri = "file:///scripts/foo/bar.lua";
        var docIndex = new DocumentIndex(LuaUri, 1, [], []);
        var fooDocIndex = new DocumentIndex(fooBarUri, 1, [], []);
        var index = MakeIndex(documents: [(LuaUri, docIndex), (fooBarUri, fooDocIndex)]);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"foo/bar\")", 1);

        var result = await BuildHandler(index, host).Handle(RequestAt(0, 10), CancellationToken.None);

        var loc = Assert.Single(result!.Select(l => l.Location!));
        Assert.Equal(fooBarUri, loc.Uri.ToString());
        Assert.Equal(0, loc.Range.Start.Line);
        Assert.Equal(0, loc.Range.Start.Character);
    }

    [Fact]
    public async Task Handle_CursorOnRelativeRequireArg_ReturnsNull()
    {
        var docIndex = new DocumentIndex(LuaUri, 1, [], []);
        var index = MakeIndex(documents: (LuaUri, docIndex));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"./local\")", 1);

        var result = await BuildHandler(index, host).Handle(RequestAt(0, 10), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorOnUnresolvableRequire_ReturnsNull()
    {
        var docIndex = new DocumentIndex(LuaUri, 1, [], []);
        var index = MakeIndex(documents: (LuaUri, docIndex));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"unknown.module\")", 1);

        var result = await BuildHandler(index, host).Handle(RequestAt(0, 10), CancellationToken.None);
        Assert.Null(result);
    }

    // ── disk fallback (vscode restore race) ──────────────────────────────────

    [Fact]
    public async Task Handle_FileOnDiskNotInHost_RequireDefinitionFallsBackToDisk()
    {
        // Simulates the vscode-languageclient restored-tab race: file on disk, no didOpen sent yet.
        var scriptPath = Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "scripts", "script.lua");
        var targetPath = Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "scripts", "foo", "bar.lua");
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [scriptPath] = new("require(\"foo/bar\")"),
            [targetPath] = new("-- bar module")
        });
        var fileHelper = new FileHelper(fileSystem);
        var scriptUri = fileHelper.PathToFileUri(scriptPath);
        var targetUri = fileHelper.PathToFileUri(targetPath);

        var docIndex = new DocumentIndex(scriptUri, 1, [], []);
        var targetDocIndex = new DocumentIndex(targetUri, 1, [], []);
        var index = MakeIndex(documents: [(scriptUri, docIndex), (targetUri, targetDocIndex)]);

        // No document in workspace host — simulates not-yet-synced state
        var handler = new LuaDefinitionHandler(
            new FakeIndexService { Current = index },
            new FakeWorkspaceHost(),
            fileHelper,
            NullLogger<LuaDefinitionHandler>.Instance);

        var result = await handler.Handle(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(scriptUri) },
                Position = new Position(0, 10)
            },
            CancellationToken.None);

        var loc = Assert.Single(result!.Select(l => l.Location!));
        Assert.Equal(targetUri, loc.Uri.ToString());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static GameIndex MakeIndex(
        (string Id, ImmutableArray<GameSymbol> Syms)[]? definitions = null,
        params (string Uri, DocumentIndex Doc)[] documents)
    {
        var docs = documents.Aggregate(
            ImmutableDictionary<string, DocumentIndex>.Empty,
            (d, p) => d.Add(p.Uri, p.Doc));

        var defs = (definitions ?? []).Aggregate(
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            (d, p) => d.Add(p.Id, p.Syms));

        return new GameIndex(BaselineIndex.Empty, docs, defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

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

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
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
}