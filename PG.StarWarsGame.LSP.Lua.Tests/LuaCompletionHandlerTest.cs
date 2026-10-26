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
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaCompletionHandlerTest
{
    private const string LuaUri = "file:///script.lua";

    // Find_First_Object("UNIT") - character 21 is inside "UNIT"
    private const string ObjectArgument = "Find_First_Object(\"UNIT\")";

    // ── the analyzer's items, merged (#154) ─────────────────────────────────

    private static readonly string[] EngineStub =
        ["---@param objectName string\n---@aetref XmlObject:Unit\nfunction Find_First_Object(objectName) end"];

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
        FakeWorkspaceHost? host = null,
        ILuaAnalyzer? analyzer = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new LuaCompletionHandler(
            svc,
            TestLuaParseCache.For(host ?? new FakeWorkspaceHost()),
            new FileHelper(new MockFileSystem()),
            schema,
            NullLogger<LuaCompletionHandler>.Instance,
            analyzer);
    }

    // Set_Next_State takes a registered state's name (#252).
    [Fact]
    public async Task SetNextState_OffersTheRegisteredStates()
    {
        const string library = "file:///data/scripts/library/pgstatemachine.lua";
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri,
            "require(\"PGStateMachine\")\nDefine_State(\"Init\", A)\nDefine_State(\"Attack\", B)\n" +
            "Set_Next_State(\"\")\nfunction A(message) end\nfunction B(message) end", 1);
        host.AddOrUpdate(library, "function Define_State(s, f) end\n" +
                                  "function Set_Next_State(s) if StateMachine[s] ~= nil then end end\n" +
                                  "function Process_States() _curproc(OnEnter) _curproc(OnUpdate) _curproc(OnExit) end",
            1);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [], ["PGStateMachine"]))
                .Add(library, new DocumentIndex(library, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var list = await BuildHandler(index, new LuaApiSchemaProvider([]), host)
            .Handle(CompletionAt(3, 16), CancellationToken.None);

        Assert.Equal(["Attack", "Init"], list.Items.Select(i => i.Label).Order());
    }

    private static LuaCompletionHandler WithAnalyzer(string text, ScriptedLuaAnalyzer? analyzer)
    {
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///units.xml", 0, null), null);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, text, 1);
        return BuildHandler(index, new LuaApiSchemaProvider(EngineStub), host, analyzer: analyzer);
    }

    private static CompletionList TheirList(params CompletionItem[] items)
    {
        return new CompletionList(items);
    }

    [Fact]
    public async Task BothAnswer_TheUnion_OursWinningALabelBothOffer()
    {
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/completion", TheirList(
            new CompletionItem { Label = "UNIT_A", Detail = "theirs" },
            new CompletionItem { Label = "local_thing", Detail = "theirs" }));

        var result = await WithAnalyzer(ObjectArgument, analyzer).Handle(CompletionAt(0, 21), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("local_thing", labels);
        var unit = Assert.Single(result.Items, i => i.Label == "UNIT_A");
        Assert.NotEqual("theirs", unit.Detail);
    }

    // Identifiers, members and keywords are the analyzer's alone (#155): ours name XML objects,
    // localisation keys and modules inside a string argument, and nothing else.
    [Theory]
    [InlineData("Fin", 3)]
    [InlineData("table.ins", 9)]
    [InlineData("if", 2)]
    public async Task OutsideAStringArgument_NothingOfOurs(string text, int character)
    {
        var result = await WithAnalyzer(text, null).Handle(CompletionAt(0, character), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task AnAnalyzerItem_IsResolvedByTheAnalyzer_WithItsOwnData()
    {
        var analyzer = new ScriptedLuaAnalyzer()
            .Answer("textDocument/completion", TheirList(new CompletionItem { Label = "local_thing", Data = 42 }))
            .Answer("completionItem/resolve", new CompletionItem { Label = "local_thing", Detail = "resolved" });
        var handler = WithAnalyzer(ObjectArgument, analyzer);
        var item = (await handler.Handle(CompletionAt(0, 21), CancellationToken.None)).Items
            .Single(i => i.Label == "local_thing");

        var resolved = await handler.Handle(item, CancellationToken.None);

        Assert.Equal("resolved", resolved.Detail);
        var (method, parameters) = analyzer.Requests.Last();
        Assert.Equal("completionItem/resolve", method);
        Assert.Equal(42, ((CompletionItem)parameters).Data!.ToObject<int>());
    }

    [Fact]
    public async Task OurItem_IsNotSentToTheAnalyzerToResolve()
    {
        var analyzer = new ScriptedLuaAnalyzer();
        var handler = WithAnalyzer(ObjectArgument, analyzer);
        var item = (await handler.Handle(CompletionAt(0, 21), CancellationToken.None)).Items
            .Single(i => i.Label == "UNIT_A");

        var resolved = await handler.Handle(item, CancellationToken.None);

        Assert.Same(item, resolved);
        Assert.DoesNotContain(analyzer.Requests, r => r.Method == "completionItem/resolve");
    }

    [Fact]
    public async Task TheAnalyzerSilent_OursAlone()
    {
        var result = await WithAnalyzer(ObjectArgument, new ScriptedLuaAnalyzer())
            .Handle(CompletionAt(0, 21), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "UNIT_A");
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
            ---@aetref XmlObject:Unit
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
        // Find_First_Object("UNIT")  - cursor at col 21, inside "UNIT"
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"UNIT\")", 1);

        var handler = BuildHandler(index, schema, host);
        var result = await handler.Handle(CompletionAt(0, 21), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "UNIT_A");
    }

    [Fact]
    public async Task Handle_InsideTextKeyStringArg_ReturnsTheLocalisationKeys()
    {
        var schema = new LuaApiSchemaProvider([
            """
            ---@param textId string
            ---@aetref LocalisationKey
            function Game_Message(textId) end
            """
        ]);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty)
        {
            Localisation = new StubLocalisationIndex(("TEXT_HELLO", "Hello"), ("TEXT_BYE", "Bye"))
        };

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Game_Message(\"TEXT\")", 1); // cursor at col 18, inside "TEXT"

        var handler = BuildHandler(index, schema, host);
        var result = await handler.Handle(CompletionAt(0, 18), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "TEXT_HELLO" && i.Detail == "Hello");
        Assert.Contains(result.Items, i => i.Label == "TEXT_BYE");
    }

    [Fact]
    public async Task Handle_InsideApiStringArg_FiltersOutNonMatchingTypes()
    {
        var schema = new LuaApiSchemaProvider([
            """
            ---@param objectName string
            ---@aetref XmlObject:Unit
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
        // Find_First_Object("")  - cursor at col 19 inside empty string
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
            ---@aetref XmlObject
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
        // require("")  - cursor at col 9, inside empty string
        host.AddOrUpdate(LuaUri, "require(\"\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(CompletionAt(0, 9), CancellationToken.None);

        Assert.Contains(result.Items, i =>
            string.Equals(i.Label, "pgstatemachine", StringComparison.OrdinalIgnoreCase));
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
        Assert.True(string.Compare(libItem.SortText, depItem.SortText, StringComparison.Ordinal) < 0,
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
            // cursor will be at col 21 - inside the "UNIT" string argument
            [path] = new("Find_First_Object(\"UNIT\")")
        });
        var fileHelper = new FileHelper(fileSystem);
        var uri = fileHelper.PathToFileUri(path);

        var schema = new LuaApiSchemaProvider([
            """
            ---@param objectName string
            ---@aetref XmlObject:Unit
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
            TestLuaParseCache.For(new FakeWorkspaceHost(), fileHelper), // no open doc - read from disk
            fileHelper,
            schema,
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

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
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

    private sealed class StubLocalisationIndex(params (string Key, string Value)[] entries) : ILocalisationIndex
    {
        public IEnumerable<string> Keys => entries.Select(e => e.Key);

        public bool ContainsKey(string key)
        {
            return entries.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public string? GetValue(string key)
        {
            return entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        }
    }
}
