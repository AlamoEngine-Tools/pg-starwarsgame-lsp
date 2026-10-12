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

public sealed class LuaHoverHandlerTest
{
    private const string LuaUri = "file:///script.lua";
    private const string LibUri = "file:///lib.lua";

    // ── the analyzer's hover, merged (#154) ──────────────────────────────────

    private const string EngineStub = "---Finds a player.\n---@param name string\nfunction Find_Player(name) end";

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
        FakeWorkspaceHost? host = null,
        ILuaAnalyzer? analyzer = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new LuaHoverHandler(
            svc,
            TestLuaParseCache.For(host ?? new FakeWorkspaceHost()),
            new FileHelper(new MockFileSystem()),
            schema,
            NullLogger<LuaHoverHandler>.Instance,
            analyzer);
    }

    private static string GetMarkdown(Hover hover)
    {
        return hover.Contents.MarkupContent?.Value ?? string.Empty;
    }

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

    private static Hover Theirs(string markdown)
    {
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                { Kind = MarkupKind.Markdown, Value = markdown })
        };
    }

    private static async Task<string?> HoverWith(string text, int character, ScriptedLuaAnalyzer analyzer)
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, text, 1);
        var handler = BuildHandler(GameIndex.Empty, new LuaApiSchemaProvider([EngineStub]), host, analyzer);
        var result = await handler.Handle(HoverAt(0, character), CancellationToken.None);
        return result is null ? null : GetMarkdown(result);
    }

    [Fact]
    public async Task BothAnswer_OneCard_OursFirst()
    {
        var text = await HoverWith("Find_Player(\"Empire\")", 3,
            new ScriptedLuaAnalyzer().Answer("textDocument/hover", Theirs("THEIRS: function Find_Player")));

        Assert.NotNull(text);
        var ours = text.IndexOf("Finds a player", StringComparison.Ordinal);
        var theirs = text.IndexOf("THEIRS", StringComparison.Ordinal);
        Assert.InRange(ours, 0, theirs - 1);
        Assert.Contains("---", text[ours..theirs]);
    }

    [Fact]
    public async Task OnlyTheAnalyzerAnswers_ItsCardAlone()
    {
        var text = await HoverWith("local abc = 1", 7,
            new ScriptedLuaAnalyzer().Answer("textDocument/hover", Theirs("THEIRS: local abc: integer")));

        Assert.Equal("THEIRS: local abc: integer", text);
    }

    [Fact]
    public async Task TheAnalyzerSilent_OursAlone()
    {
        var text = await HoverWith("Find_Player(\"Empire\")", 3, new ScriptedLuaAnalyzer());

        Assert.NotNull(text);
        Assert.Contains("Finds a player", text);
        Assert.DoesNotContain("---\n", text.Replace("\r", "", StringComparison.Ordinal));
    }

    /// <summary>Measured in the spike: the analyzer renders our reference tag as text.</summary>
    [Fact]
    public async Task TheAnalyzersRenderingOfOurReferenceTag_IsDropped()
    {
        var text = await HoverWith("local abc = 1", 7,
            new ScriptedLuaAnalyzer().Answer("textDocument/hover",
                Theirs("function Find_Player(name)\n\n@*aetref* XmlObject:Faction")));

        Assert.DoesNotContain("aetref", text);
        Assert.Contains("function Find_Player(name)", text);
    }

    // ── XML reference hover ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnLuaGlobalCallSite_LuaGlobalReferenceInIndex_DoesNotShowXmlObjectHover()
    {
        // LuaGameDocumentParser emits a LuaGlobal reference at the call-site position for
        // every user-defined function call. Before the fix, TryBuildXmlRefHover would match
        // that reference and emit a spurious "XmlObject: RunMission" hover.
        var sym = new GameSymbol("RunMission", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var luaGlobalRef = new GameReference("RunMission", GameSymbolKind.LuaGlobal, null,
            LuaUri, 0, 0, 10);

        var docIndex = new DocumentIndex(LuaUri, 1, [], [luaGlobalRef]);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, docIndex),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("RunMission", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(HoverAt(0, 5), CancellationToken.None);

        // A workspace function's card is the analyzer's (#155).
        Assert.Null(result);
    }

    // The analyzer reads the same doc comments and types the workspace's own code (#155).
    [Theory]
    [InlineData("---@class PGUnit\nPGUnit = {}\nlocal u = PGUnit", 2, 12)]
    [InlineData("---@alias Command string\nlocal c = Command", 1, 12)]
    [InlineData("---Runs it.\nfunction RunMission() end\nRunMission()", 2, 4)]
    public async Task TheWorkspacesOwnTypesAndFunctions_NothingOfOurs(string text, int line, int character)
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, text, 1);
        var sym = new GameSymbol("RunMission", GameSymbolKind.LuaGlobal, null, new FileOrigin(LuaUri, 1, 9),
            "Runs it.");
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [sym], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("RunMission", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var result = await BuildHandler(index, new LuaApiSchemaProvider([]), host)
            .Handle(HoverAt(line, character), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorOnXmlRef_ReturnsHoverWithTypeAndId()
    {
        const string xmlUri = "file:///units.xml";
        const string targetId = "UNIT_A";

        // reference at col 19, length 6 - matches "UNIT_A" in Find_First_Object("UNIT_A")
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

    [Fact]
    public async Task Handle_CursorOnXmlRef_MegArchiveOrigin_ReturnsPackagedHover()
    {
        const string targetId = "UNIT_VANILLA";
        var reference = new GameReference(targetId, GameSymbolKind.XmlObject, "Unit", LuaUri, 0, 19, 12);
        var archiveSym = new GameSymbol(targetId, GameSymbolKind.XmlObject, "Unit",
            new MegArchiveOrigin("data.meg", "units.xml", 5, 0), null);

        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, docIndex),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(targetId, [archiveSym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"UNIT_VANILLA\")", 1);

        var handler = BuildHandler(index, new LuaApiSchemaProvider([]), host);
        var result = await handler.Handle(HoverAt(0, 25), CancellationToken.None);

        Assert.NotNull(result);
        var content = GetMarkdown(result!);
        Assert.Contains("UNIT_VANILLA", content);
        // Must mention the archive and explain the object is read-only.
        Assert.Contains("data.meg", content, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            content.Contains("read-only", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("packaged", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("archive", StringComparison.OrdinalIgnoreCase),
            $"Hover must mention packaged/archive/read-only status. Got: {content}");
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

    [Fact]
    public async Task Handle_CursorOnEngineGlobal_ReturnsHoverWithDescription()
    {
        var schema = new LuaApiSchemaProvider([
            """
            --- Finds the first game object with the specified type.
            ---@param objectName string
            ---@aetref XmlObject
            function Find_First_Object(objectName) end
            """
        ]);

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

    // ── definitions of names the schema knows (#249) ────────────────────────
    // Measured on emmylua 0.25.1: a script function defining a global the stubs declare gets
    // neither the stub's documentation nor its types, so the stub card is ours to give.

    private static async Task<string?> HoverOn(string text, int line, int character, LuaApiSchemaProvider schema)
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, text, 1);
        var result = await BuildHandler(GameIndex.Empty, schema, host)
            .Handle(HoverAt(line, character), CancellationToken.None);
        return result is null ? null : GetMarkdown(result);
    }

    [Fact]
    public async Task AScriptDefiningAStubGlobal_GetsTheStubsCard()
    {
        var text = await HoverOn("function Find_Player(name)\nend", 0, 12, new LuaApiSchemaProvider([EngineStub]));

        Assert.Contains("Finds a player", text);
    }

    private static LuaApiSchemaProvider WithEntryPoints()
    {
        return new LuaApiSchemaProvider([], [
            new LuaEntryPoint("main", LuaEntryPointCaller.ENGINE, [], ["object", "story"], "The script's thread."),
            new LuaEntryPoint("Definitions", LuaEntryPointCaller.LIBRARY, ["PGStateMachine"], ["object"],
                "The script's own setup.")
        ]);
    }

    [Fact]
    public async Task AnEntryPointsDefinition_SaysWhatCallsItAndWhen()
    {
        var text = await HoverOn("function main()\nend", 0, 10, WithEntryPoints());

        Assert.Contains("The script's thread.", text);
        Assert.Contains("Called by the game", text);
        Assert.Contains("object, story", text);
    }

    // The library's own call: the same card, naming the library.
    [Fact]
    public async Task ACallToALibraryCalledEntryPoint_NamesTheLibrary()
    {
        var text = await HoverOn("if Definitions then\n  Definitions()\nend", 1, 4, WithEntryPoints());

        Assert.Contains("The script's own setup.", text);
        Assert.Contains("PGStateMachine", text);
    }

    // ── richer hover content ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnEngineGlobal_WithParams_ShowsParamNamesInHover()
    {
        var schema = new LuaApiSchemaProvider([
            """
            --- Finds the first object by type name.
            ---@param typeName string The XML object type name.
            ---@aetref XmlObject
            ---@return GameObject
            function Find_First_Object(typeName) end
            """
        ]);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Find_First_Object(\"UNIT\")", 1);

        var handler = BuildHandler(GameIndex.Empty, schema, host);
        var result = await handler.Handle(HoverAt(0, 5), CancellationToken.None);

        Assert.NotNull(result);
        var content = GetMarkdown(result!);
        Assert.Contains("Find_First_Object", content);
        Assert.Contains("typeName", content);
        Assert.Contains("string", content);
        Assert.Contains("GameObject", content);
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

    // ── disk fallback (vscode restore race) ──────────────────────────────────

    [Fact]
    public async Task Handle_FileOnDiskNotInHost_RequireHoverFallsBackToDisk()
    {
        // Simulates the vscode-languageclient restored-tab race: file exists on disk but
        // the client has not yet sent didOpen, so the workspace host has no tracked document.
        var path = Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "scripts", "script.lua");
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("require(\"SomeModule\")")
        });
        var fileHelper = new FileHelper(fileSystem);
        var uri = fileHelper.PathToFileUri(path);

        var handler = new LuaHoverHandler(
            new FakeIndexService { Current = GameIndex.Empty },
            TestLuaParseCache.For(new FakeWorkspaceHost(), fileHelper), // no open doc - read from disk
            fileHelper,
            new LuaApiSchemaProvider([]),
            NullLogger<LuaHoverHandler>.Instance);

        // Cursor inside "SomeModule" - require hover should produce a result from disk content
        var result = await handler.Handle(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
                Position = new Position(0, 12)
            },
            CancellationToken.None);

        Assert.NotNull(result);
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
}