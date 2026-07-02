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
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
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
            new LuaAnnotationRepository(),
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
        var schema = new LuaApiSchemaProvider([
            """
            ---@param objectName string
            ---@xmlref XmlObject:Unit
            function Find_First_Object(objectName) end
            """
        ]);

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
        var schema = new LuaApiSchemaProvider([
            """
            ---@param objectName string
            ---@xmlref XmlObject:Unit
            function Find_First_Object(objectName) end
            """
        ]);

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
        var schema = new LuaApiSchemaProvider([
            """
            ---@param objectName string
            ---@xmlref XmlObject
            function Find_First_Object(objectName) end
            """
        ]);

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

    // ── identifier completions ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_IdentifierContext_ReturnsLua51Builtins()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "pai", 1); // partial "pairs" at start of line
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 3), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "pairs" && i.Kind == CompletionItemKind.Keyword);
    }

    [Fact]
    public async Task Handle_IdentifierContext_ReturnsEngineApiFunctions()
    {
        var schema = new LuaApiSchemaProvider([
            "function Find_Player(playerIndex) end"
        ]);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Find", 1);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var handler = BuildHandler(index, schema, host);
        var result = await handler.Handle(CompletionAt(0, 4), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "Find_Player" && i.Kind == CompletionItemKind.Function);
    }

    [Fact]
    public async Task Handle_IdentifierContext_OwnGlobal_IsIncluded()
    {
        var sym = new GameSymbol("MyGlobal", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LuaUri, 0, null), null);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "My", 1);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [sym], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("MyGlobal", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 2), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "MyGlobal");
    }

    // ── snippet completions ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AtStatementStart_SnippetsIncluded()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "if", 1); // "if" typed at start of line
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 2), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "if" && i.Kind == CompletionItemKind.Snippet);
    }

    [Fact]
    public async Task Handle_NotAtStatementStart_NoSnippets()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "local x = if", 1); // "if" after assignment
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 12), CancellationToken.None);

        Assert.DoesNotContain(result.Items, i => i.Kind == CompletionItemKind.Snippet);
    }

    // ── require ordering ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RequireCompletion_LibraryFilesHaveLowerSortText()
    {
        const string libUri = "file:///scripts/library/statemachine.lua";
        const string depUri = "file:///scripts/pgplayer.lua";
        // mainUri requires depUri (making it a Dependency), libUri is library (path based)
        var mainDoc = new DocumentIndex(LuaUri, 1, [], [], ImmutableArray.Create("pgplayer"));
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, mainDoc)
                .Add(libUri, new DocumentIndex(libUri, 1, [], []))
                .Add(depUri, new DocumentIndex(depUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 9), CancellationToken.None);

        var libItem = result.Items.FirstOrDefault(i => i.Label == "statemachine");
        var depItem = result.Items.FirstOrDefault(i => i.Label == "pgplayer");

        Assert.NotNull(libItem);
        Assert.NotNull(depItem);
        // Library sort text starts with "0_", dependency with "1_" → library sorts before dependency
        Assert.True(string.Compare(libItem!.SortText, depItem!.SortText, StringComparison.Ordinal) < 0,
            $"Library '{libItem.SortText}' should sort before Dependency '{depItem.SortText}'");
    }

    [Fact]
    public async Task Handle_RequireCompletion_StandaloneFileOmitted()
    {
        const string standaloneUri = "file:///scripts/somescript.lua";
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []))
                .Add(standaloneUri, new DocumentIndex(standaloneUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 9), CancellationToken.None);

        // "somescript" is standalone (no one requires it, not in library/) → must be omitted
        Assert.DoesNotContain(result.Items, i => i.Label == "somescript");
    }

    // ── disk fallback (vscode restore race) ──────────────────────────────────

    [Fact]
    public async Task Handle_FileOnDiskNotInHost_CompletionFallsBackToDisk()
    {
        // Simulates the vscode-languageclient restored-tab race: file on disk, no didOpen sent yet.
        var path = Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "scripts", "script.lua");
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            // cursor will be at col 21 — inside the "UNIT" string argument
            [path] = new("Find_First_Object(\"UNIT\")")
        });
        var fileHelper = new FileHelper(fileSystem);
        var uri = fileHelper.PathToFileUri(path);

        var schema = new LuaApiSchemaProvider([
            """
            ---@param objectName string
            ---@xmlref XmlObject:Unit
            function Find_First_Object(objectName) end
            """
        ]);
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///units.xml", 0, null), null);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(uri, new DocumentIndex(uri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var handler = new LuaCompletionHandler(
            new FakeIndexService { Current = index },
            new FakeWorkspaceHost(), // empty — no document tracked
            fileHelper,
            schema,
            new LuaAnnotationRepository(),
            NullLogger<LuaCompletionHandler>.Instance);

        var result = await handler.Handle(
            new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
                Position = new Position(0, 21)
            },
            CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "UNIT_A");
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;
        public event Action<ILocalisationIndex>? LocalisationChanged;

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
