// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Testing;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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

    private static LuaDefinitionHandler BuildHandler(GameIndex index, FakeWorkspaceHost? host = null,
        ILspConfigurationProvider? config = null, ILuaAnalyzer? analyzer = null, ILuaSandboxProvider? sandbox = null)
    {
        return new LuaDefinitionHandler(
            new FakeIndexService { Current = index },
            TestLuaParseCache.For(host ?? new FakeWorkspaceHost()),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaDefinitionHandler>.Instance,
            config ?? new FakeLspConfigurationProvider(),
            analyzer,
            sandbox);
    }

    // ── ours, else the analyzer's (#154) ─────────────────────────────────────

    private static LocationOrLocationLinks TheirLocation()
    {
        return new LocationOrLocationLinks(new Location
        {
            Uri = DocumentUri.From(LuaUri),
            Range = new Range(7, 6, 7, 9)
        });
    }

    [Fact]
    public async Task NothingOfOurs_TheAnalyzersDefinition()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "local abc = 1\nprint(abc)", 1);
        var index = MakeIndex(documents: (LuaUri, new DocumentIndex(LuaUri, 1, [], [])));
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/definition", TheirLocation());

        var result = await BuildHandler(index, host, analyzer: analyzer)
            .Handle(RequestAt(1, 7), CancellationToken.None);

        Assert.Equal(7, Assert.Single(result!).Location!.Range.Start.Line);
    }

    // Measured on the EaW scripts: go to definition on Interdictor.lua's Definitions call jumped to
    // another script's Definitions. Two object scripts are two states (#249).
    [Fact]
    public async Task TheAnalyzersDefinition_InAnotherScriptsState_IsDropped()
    {
        const string interdictor = "file:///mod/data/scripts/gameobject/interdictor.lua";
        const string vader = "file:///mod/data/scripts/gameobject/darthvader.lua";
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(interdictor, "Definitions()", 1);
        var documents = ImmutableDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(interdictor, new DocumentIndex(interdictor, 1, [], [])),
            KeyValuePair.Create(vader, new DocumentIndex(vader, 1, [], []))
        });
        var index = MakeIndex(documents: [(interdictor, documents[interdictor]), (vader, documents[vader])]);
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/definition", new LocationOrLocationLinks(
            new Location { Uri = DocumentUri.From(vader), Range = new Range(3, 9, 3, 20) },
            new Location { Uri = DocumentUri.From(interdictor), Range = new Range(8, 9, 8, 20) }));
        var sandbox = new FixedSandbox(LuaSandbox.Build(documents, new FileHelper(new MockFileSystem())));

        var result = await BuildHandler(index, host, analyzer: analyzer, sandbox: sandbox)
            .Handle(RequestAt(0, 3, interdictor), CancellationToken.None);

        Assert.Equal(interdictor, Assert.Single(result!).Location!.Uri.ToString());
    }

    // Our own index holds every script's globals; a Lua global resolves within the script's state.
    [Fact]
    public async Task OurLuaGlobal_ResolvesInTheScriptsOwnState()
    {
        const string mine = "file:///game/data/scripts/gameobject/interdictor.lua";
        const string another = "file:///mod/data/scripts/gameobject/darthvader.lua";
        var myDef = new GameSymbol("Definitions", GameSymbolKind.LuaGlobal, null, new FileOrigin(mine, 4, 9), null);
        var theirDef = new GameSymbol("Definitions", GameSymbolKind.LuaGlobal, null, new FileOrigin(another, 7, 9),
            null);
        var call = new GameReference("Definitions", GameSymbolKind.LuaGlobal, null, mine, 0, 0, 11);
        var documents = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(mine, new DocumentIndex(mine, 1, [myDef], [call], LayerRank: 0))
            .Add(another, new DocumentIndex(another, 1, [theirDef], [], LayerRank: 1));
        var index = MakeIndex([("Definitions", ImmutableArray.Create(myDef, theirDef))],
            (mine, documents[mine]), (another, documents[another]));
        var sandbox = new FixedSandbox(LuaSandbox.Build(documents, new FileHelper(new MockFileSystem())));

        var result = await BuildHandler(index, sandbox: sandbox).Handle(RequestAt(0, 3, mine), CancellationToken.None);

        Assert.Equal(mine, Assert.Single(result!).LocationLink!.TargetUri.ToString());
    }

    // A library runs in every state that requires it: a global it calls and each script defines is
    // one definition per state - Definitions from PGStateMachine is every object script's (1:n).
    // From one of those scripts its own state is the only one, so the answer stays its own (1:1).
    [Fact]
    public async Task FromALibrary_EveryRequiringScriptsDefinition_FromAScript_ItsOwn()
    {
        const string library = "file:///game/data/scripts/library/pgstatemachine.lua";
        const string interdictor = "file:///mod/data/scripts/gameobject/interdictor.lua";
        const string vader = "file:///mod/data/scripts/gameobject/darthvader.lua";
        const string loose = "file:///mod/data/scripts/gameobject/loose.lua";
        var interdictorDef = new GameSymbol("Definitions", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(interdictor, 4, 9), null);
        var vaderDef = new GameSymbol("Definitions", GameSymbolKind.LuaGlobal, null, new FileOrigin(vader, 7, 9),
            null);
        var looseDef = new GameSymbol("Definitions", GameSymbolKind.LuaGlobal, null, new FileOrigin(loose, 1, 9),
            null);
        var libraryCall = new GameReference("Definitions", GameSymbolKind.LuaGlobal, null, library, 0, 0, 11);
        var vaderCall = new GameReference("Definitions", GameSymbolKind.LuaGlobal, null, vader, 0, 0, 11);
        var documents = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(library, new DocumentIndex(library, 1, [], [libraryCall], LayerRank: 0))
            .Add(interdictor, new DocumentIndex(interdictor, 1, [interdictorDef], [], ["PGStateMachine"], LayerRank: 1))
            .Add(vader, new DocumentIndex(vader, 1, [vaderDef], [vaderCall], ["PGStateMachine"], LayerRank: 1))
            .Add(loose, new DocumentIndex(loose, 1, [looseDef], [], LayerRank: 1));
        var index = MakeIndex([("Definitions", ImmutableArray.Create(interdictorDef, vaderDef, looseDef))],
            (library, documents[library]), (interdictor, documents[interdictor]), (vader, documents[vader]),
            (loose, documents[loose]));
        var sandbox = new FixedSandbox(LuaSandbox.Build(documents, new FileHelper(new MockFileSystem())));
        var handler = BuildHandler(index, sandbox: sandbox);

        var fromLibrary = await handler.Handle(RequestAt(0, 3, library), CancellationToken.None);
        var fromScript = await handler.Handle(RequestAt(0, 3, vader), CancellationToken.None);

        Assert.Equal([vader, interdictor], fromLibrary!.Select(l => l.LocationLink!.TargetUri.ToString()).Order());
        Assert.Equal(vader, Assert.Single(fromScript!).LocationLink!.TargetUri.ToString());
    }

    public static TheoryData<string> Setups => new(ProjectSetup.Names);

    // The same in every shape (#280): the library is the lowest layer's, the scripts the leaf's.
    [Theory]
    [MemberData(nameof(Setups))]
    public async Task FromALowerLayersLibrary_EveryRequiringScriptsDefinition_FromAScript_ItsOwn(string name)
    {
        var setup = ProjectSetup.Named(name);
        var library = setup.ScriptUri(0, "Library/PGStateMachine.lua");
        var interdictor = setup.ScriptUri(setup.LeafRank, "GameObject/Interdictor.lua");
        var vader = setup.ScriptUri(setup.LeafRank, "GameObject/DarthVader.lua");
        var index = ProjectSetup.Index(
            setup.Script(0, "Library/PGStateMachine.lua",
                uses: [ProjectSetup.LuaGlobalUse("Definitions", library, 0, 0)]),
            setup.Script(setup.LeafRank, "GameObject/Interdictor.lua", ["PGStateMachine"],
                [ProjectSetup.LuaGlobal("Definitions", interdictor)]),
            setup.Script(setup.LeafRank, "GameObject/DarthVader.lua", ["PGStateMachine"],
                [ProjectSetup.LuaGlobal("Definitions", vader)],
                [ProjectSetup.LuaGlobalUse("Definitions", vader, 0, 0)]));
        var fileHelper = new FileHelper(setup.Disk());
        var handler = BuildHandler(index, sandbox: new FixedSandbox(LuaSandbox.Build(index.Documents, fileHelper)));

        var fromLibrary = await handler.Handle(RequestAt(0, 3, library), CancellationToken.None);
        var fromScript = await handler.Handle(RequestAt(0, 3, vader), CancellationToken.None);

        Assert.Equal(
            new[] { interdictor, vader }.Select(fileHelper.NormalizeUri).Order(StringComparer.OrdinalIgnoreCase),
            fromLibrary!.Select(l => fileHelper.NormalizeUri(l.LocationLink!.TargetUri.ToString()))
                .Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(fileHelper.NormalizeUri(vader),
            fileHelper.NormalizeUri(Assert.Single(fromScript!).LocationLink!.TargetUri.ToString()),
            StringComparer.OrdinalIgnoreCase);
    }

    // A state name in Set_Next_State goes to the handler registered under it (#252).
    [Fact]
    public async Task AStateName_GoesToItsHandler()
    {
        const string library = "file:///data/scripts/library/pgstatemachine.lua";
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"PGStateMachine\")\nDefine_State(\"Attack\", State_Attack)\n" +
                                 "Set_Next_State(\"Attack\")\nfunction State_Attack(message) end", 1);
        host.AddOrUpdate(library, "function Define_State(s, f) end\n" +
                                  "function Set_Next_State(s) if StateMachine[s] ~= nil then end end\n" +
                                  "function Process_States() _curproc(OnEnter) _curproc(OnUpdate) _curproc(OnExit) end",
            1);
        var index = MakeIndex(documents:
        [
            (LuaUri, new DocumentIndex(LuaUri, 1, [], [], ["PGStateMachine"])),
            (library, new DocumentIndex(library, 1, [], []))
        ]);

        var result = await BuildHandler(index, host).Handle(RequestAt(2, 18), CancellationToken.None);

        var link = Assert.Single(result!).LocationLink!;
        Assert.Equal((3, 9), (link.TargetSelectionRange.Start.Line, link.TargetSelectionRange.Start.Character));
        Assert.Equal(16, link.OriginSelectionRange!.Start.Character);
    }

    [Fact]
    public async Task OursFound_TheAnalyzerIsNotAsked()
    {
        var reference = new GameReference("Foo", GameSymbolKind.LuaGlobal, null, LuaUri, 0, 0, 3);
        var symbol = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null, new FileOrigin(LibUri, 5, 9), null);
        var index = MakeIndex([("Foo", ImmutableArray.Create(symbol))],
            (LuaUri, new DocumentIndex(LuaUri, 1, [], [reference])),
            (LibUri, new DocumentIndex(LibUri, 1, [symbol], [])));
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/definition", TheirLocation());

        var result = await BuildHandler(index, analyzer: analyzer).Handle(RequestAt(0, 1), CancellationToken.None);

        Assert.Equal(LibUri, Assert.Single(result!).LocationLink!.TargetUri.ToString());
        Assert.Empty(analyzer.Requests);
    }

    // ── feature flag ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LuaGoToDefinitionFlagOff_ReturnsNull()
    {
        // Same arrange as Handle_CursorOnGlobalReference - only the flag differs.
        var reference = new GameReference("Foo", GameSymbolKind.LuaGlobal, null, LuaUri, 0, 0, 3);
        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);
        var symbol = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null, new FileOrigin(LibUri, 5, 9), null);
        var libDocIndex = new DocumentIndex(LibUri, 1, [symbol], []);
        var index = MakeIndex(
            [("Foo", ImmutableArray.Create(symbol))], (LuaUri, docIndex), (LibUri, libDocIndex));
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Lua = new LuaFeatureFlags { GoToDefinition = false } });

        var result = await BuildHandler(index, config: config)
            .Handle(RequestAt(0, 1), CancellationToken.None);

        Assert.Null(result);
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

        var loc = Assert.Single(result!.Select(l => l.LocationLink!));
        Assert.Equal(LibUri, loc.TargetUri.ToString());
        Assert.Equal(5, loc.TargetRange.Start.Line);
        Assert.Equal(9, loc.TargetRange.Start.Character);
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

        var loc = Assert.Single(result!.Select(l => l.LocationLink!));
        Assert.Equal(LibUri, loc.TargetUri.ToString());
        Assert.Equal(2, loc.TargetRange.Start.Line);
        Assert.Equal(9, loc.TargetRange.Start.Character);
    }

    [Fact]
    public async Task Handle_SymbolWithNonFileOrigin_ReturnsNull()
    {
        var reference = new GameReference("Foo", GameSymbolKind.LuaGlobal, null, LuaUri, 0, 0, 3);
        var docIndex = new DocumentIndex(LuaUri, 1, [], [reference]);

        // Symbol has UnknownOrigin - not navigable
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

        // Cursor at (0, 12) is inside [10, 16) - must resolve to the XML file.
        var loc = Assert.Single(result!.Select(l => l.LocationLink!));
        Assert.Equal(xmlUri, loc.TargetUri.ToString());
        Assert.Equal(5, loc.TargetRange.Start.Line);
        Assert.Equal(0, loc.TargetRange.Start.Character);
    }

    [Fact]
    public async Task Handle_CursorOnXmlObjectStringLiteral_MegArchiveOrigin_ReturnsNull()
    {
        // Symbol exists but lives in a packaged .meg archive - cannot navigate.
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
        // require("foo/bar") - cursor at col 10 (inside the string)
        const string fooBarUri = "file:///scripts/foo/bar.lua";
        var docIndex = new DocumentIndex(LuaUri, 1, [], []);
        var fooDocIndex = new DocumentIndex(fooBarUri, 1, [], []);
        var index = MakeIndex(documents: [(LuaUri, docIndex), (fooBarUri, fooDocIndex)]);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "require(\"foo/bar\")", 1);

        var result = await BuildHandler(index, host).Handle(RequestAt(0, 10), CancellationToken.None);

        var loc = Assert.Single(result!.Select(l => l.LocationLink!));
        Assert.Equal(fooBarUri, loc.TargetUri.ToString());
        Assert.Equal(0, loc.TargetRange.Start.Line);
        Assert.Equal(0, loc.TargetRange.Start.Character);
        // The require-string span drives the Ctrl-hover link decoration.
        Assert.NotNull(loc.OriginSelectionRange);
        Assert.Equal(0, loc.OriginSelectionRange!.Start.Line);
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

        // No document in workspace host - simulates not-yet-synced state
        var handler = new LuaDefinitionHandler(
            new FakeIndexService { Current = index },
            TestLuaParseCache.For(new FakeWorkspaceHost(), fileHelper), // no open doc - read from disk
            fileHelper,
            NullLogger<LuaDefinitionHandler>.Instance,
            new FakeLspConfigurationProvider());

        var result = await handler.Handle(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(scriptUri) },
                Position = new Position(0, 10)
            },
            CancellationToken.None);

        var loc = Assert.Single(result!.Select(l => l.LocationLink!));
        // Compared with the fold, not ordinally: the protocol's own DocumentUri lowercases a
        // Windows drive letter on the way out, so "file:///D:/..." comes back as "file:///d:/...".
        // That is exactly why sameness is a comparer's job - we do not control every producer.
        Assert.True(DocumentUris.Same(targetUri, loc.TargetUri.ToString()),
            $"expected {targetUri}, got {loc.TargetUri}");
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