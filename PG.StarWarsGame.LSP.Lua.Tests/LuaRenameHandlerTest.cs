// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string LuaUri = "file:///script.lua";
    private const string OtherLuaUri = "file:///lib.lua";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static RenameParams RenameAt(int line, int character, string newName, string uri = LuaUri)
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character),
            NewName = newName
        };
    }

    private static GameSymbol XmlSymbolAt(string id, string uri, int line, string typeName = "Unit")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(uri, line, null), null);
    }

    private static GameSymbol XmlSymbolInArchive(string id)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit",
            new MegArchiveOrigin("data.meg", "units.xml", 0, 0), null);
    }

    private static GameSymbol LuaGlobal(string name, string uri)
    {
        return new GameSymbol(name, GameSymbolKind.LuaGlobal, null, new FileOrigin(uri, 0, null), null);
    }

    private static GameReference LuaRef(string id, string uri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, null, uri, line, col, len);
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
        return new GameIndex(BaselineIndex.Empty,
            docs ?? ImmutableDictionary<string, DocumentIndex>.Empty,
            defs ?? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            refs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static DocumentIndex MakeDoc(string uri, GameSymbol? sym = null, GameReference? r = null)
    {
        var syms = sym is null ? ImmutableArray<GameSymbol>.Empty : [sym];
        var refs = r is null ? ImmutableArray<GameReference>.Empty : [r];
        return new DocumentIndex(uri, 1, syms, refs);
    }

    private static LuaRenameHandler MakeHandler(FakeWorkspaceHost? host = null, FakeSchemaProvider? schema = null,
        ILuaApiSchemaProvider? luaSchema = null, ILuaSandboxProvider? sandbox = null)
    {
        var textSource = new DocumentTextSource(
            host ?? new FakeWorkspaceHost(),
            new FileHelper(new MockFileSystem()),
            NullLogger<DocumentTextSource>.Instance);
        return new LuaRenameHandler(
            new LuaParseCache(textSource, 16),
            textSource,
            schema ?? new FakeSchemaProvider(),
            NullLogger<LuaRenameHandler>.Instance,
            luaSchema,
            sandbox,
            new FileHelper(new MockFileSystem()));
    }

    // ── Claims: what is ours to rename (#154) ────────────────────────────────

    private static bool ClaimsAt(string text, int line, int character, GameIndex index,
        ILuaApiSchemaProvider? luaSchema = null)
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, text, 1);
        return MakeHandler(host, luaSchema: luaSchema).Claims(LuaUri, line, character, index);
    }

    [Fact]
    public void ALocal_IsNotOurs()
    {
        Assert.False(ClaimsAt("local abc = 1\nprint(abc)", 1, 7, BuildIndex()));
    }

    [Fact]
    public void AnXmlObjectString_IsOurs()
    {
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("Empire", [XmlSymbolAt("Empire", XmlUri, 0, "Faction")]);

        Assert.True(ClaimsAt("Find_Player(\"Empire\")", 0, 15, BuildIndex(defs: defs)));
    }

    [Fact]
    public void AWorkspaceGlobal_IsOurs()
    {
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("Foo", [LuaGlobal("Foo", OtherLuaUri)]);

        Assert.True(ClaimsAt("Foo()", 0, 1, BuildIndex(defs: defs)));
    }

    /// <summary>The game binds it; a rename by the analyzer would break every script calling it.</summary>
    [Fact]
    public void AnEngineName_IsOurs()
    {
        var engine = new LuaApiSchemaProvider(["---@param name string\nfunction Find_Player(name) end"]);

        Assert.True(ClaimsAt("Find_Player(\"x\")", 0, 3, BuildIndex(), engine));
    }

    /// <summary>
    ///     A vanilla library's function is documented by a stub of its own, but the game does not
    ///     bind it: a script defines it, and a rename across the library and its callers is the
    ///     analyzer's to do, as before the library stubs existed.
    /// </summary>
    [Fact]
    public void ALibraryName_IsNotOurs()
    {
        var withLibrary = LuaApiSchemaProvider.FromFiles([
            new LuaStubFile("api.d.lua", "---@param name string\nfunction Find_Player(name) end"),
            new LuaStubFile("pgbase.d.lua", "---@param time number\nfunction Sleep(time) end")
        ]);

        Assert.False(ClaimsAt("Sleep(1)", 0, 2, BuildIndex(), withLibrary));
        Assert.True(ClaimsAt("Find_Player(\"x\")", 0, 3, BuildIndex(), withLibrary));
    }

    // ── HandleRename: XmlObject string path ──────────────────────────────────

    [Fact]
    public void HandleRename_CursorOnXmlObjectString_ReturnsCrossFileEdit()
    {
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var xmlDoc = MakeDoc(XmlUri, sym);
        var luaDoc = MakeDoc(LuaUri);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, xmlDoc).Add(LuaUri, luaDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [luaRef]));

        var result = MakeHandler(host, schema).HandleRename(LuaUri, RenameAt(0, 7, "UNIT_B"), index);

        Assert.NotNull(result);
        Assert.True(result.Changes!.ContainsKey(DocumentUri.From(XmlUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(LuaUri)));
    }

    [Fact]
    public void HandleRename_CursorOnXmlObjectString_LuaEditRangeSkipsQuote()
    {
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 0);
        var xmlDoc = MakeDoc(XmlUri, sym);
        var luaDoc = MakeDoc(LuaUri);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, xmlDoc).Add(LuaUri, luaDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [luaRef]));

        var result = MakeHandler(host, schema).HandleRename(LuaUri, RenameAt(0, 7, "UNIT_B"), index);

        Assert.NotNull(result);
        var luaEdits = result.Changes![DocumentUri.From(LuaUri)].ToList();
        var edit = Assert.Single(luaEdits);
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(7, edit.Range.Start.Character);
        Assert.Equal(13, edit.Range.End.Character);
        Assert.Equal("UNIT_B", edit.NewText);
    }

    [Fact]
    public void HandleRename_CursorOnUnknownString_ReturnsNull()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"NOT_A_GAME_OBJECT\")", 1);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, MakeDoc(LuaUri)));

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 8, "SOMETHING_ELSE"), index);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_CursorOnXmlObjectString_ArchiveDefinition_ReturnsNull()
    {
        var sym = XmlSymbolInArchive("VANILLA_UNIT");
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"VANILLA_UNIT\")", 1);

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, MakeDoc(LuaUri)),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("VANILLA_UNIT", [sym]));

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 8, "NEW_UNIT"), index);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_CursorOnXmlObjectString_DependencyLayerDefinition_ReturnsNull()
    {
        // Symbol is FileOrigin in a dep-layer doc (rank 0); rename must be blocked.
        var depSym = XmlSymbolAt("UNIT_DEP", "file:///dep/units.xml", 5);
        var depDoc = new DocumentIndex("file:///dep/units.xml", 1, [depSym],
            ImmutableArray<GameReference>.Empty, LayerRank: 0);
        var luaDoc = new DocumentIndex(LuaUri, 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty, LayerRank: 1);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_DEP\")", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_DEP", [depSym]);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
            .Add("UNIT_DEP", [LuaRef("UNIT_DEP", LuaUri, 0, 7, 8)]);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///dep/units.xml", depDoc)
                .Add(LuaUri, luaDoc),
            defs,
            refs);

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 8, "UNIT_NEW"), index);
        Assert.Null(result);
    }

    // ── HandleRename: LuaGlobal path ──────────────────────────────────────────

    [Fact]
    public void HandleRename_CursorOnLuaGlobalCallSite_ReturnsLuaOnlyEdit()
    {
        var callerRef = LuaGlobalRef("RunMission", LuaUri, 0, 0, 10);
        var sym = LuaGlobal("RunMission", OtherLuaUri);
        var callerDoc = new DocumentIndex(LuaUri, 1, [], [callerRef]);
        var defDoc = new DocumentIndex(OtherLuaUri, 1, [sym], []);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(OtherLuaUri, "function RunMission() end", 1);
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(OtherLuaUri, defDoc).Add(LuaUri, callerDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("RunMission", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("RunMission", [callerRef]));

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 0, "ExecuteMission"), index);

        Assert.NotNull(result);
        Assert.True(result.Changes!.ContainsKey(DocumentUri.From(OtherLuaUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(LuaUri)));
        Assert.False(result.Changes.ContainsKey(DocumentUri.From(XmlUri)));
    }

    [Fact]
    public void HandleRename_LuaGlobalCallSite_CallerNotInHost_ProducesEditFromIndex()
    {
        var callerRef = LuaGlobalRef("RunMission", LuaUri, 0, 0, 10);
        var sym = LuaGlobal("RunMission", OtherLuaUri);
        var defDoc = new DocumentIndex(OtherLuaUri, 1, [sym], []);
        var callerDoc = new DocumentIndex(LuaUri, 1, [], [callerRef]);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(OtherLuaUri, "function RunMission() end", 1);
        // LuaUri deliberately NOT in host

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(OtherLuaUri, defDoc).Add(LuaUri, callerDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("RunMission", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("RunMission", [callerRef]));

        var result = MakeHandler(host).HandleRename(OtherLuaUri, RenameAt(0, 9, "ExecuteMission", OtherLuaUri), index);

        Assert.NotNull(result);
        Assert.True(result.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
        var edit = Assert.Single(result.Changes[DocumentUri.From(LuaUri)]);
        Assert.Equal("ExecuteMission", edit.NewText);
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
        Assert.Equal(10, edit.Range.End.Character);
    }

    [Fact]
    public void HandleRename_CursorOnLuaGlobalDefinition_DefinitionEditRangeIsCorrect()
    {
        var sym = LuaGlobal("Foo", LuaUri);
        var defDoc = new DocumentIndex(LuaUri, 1, [sym], []);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "function Foo() end", 1);

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, defDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Foo", [sym]));

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 9, "Bar"), index);

        Assert.NotNull(result);
        var edits = result.Changes![DocumentUri.From(LuaUri)].ToList();
        var defEdit = edits.First(e => e.Range.Start.Character == 9);
        Assert.Equal(0, defEdit.Range.Start.Line);
        Assert.Equal(9, defEdit.Range.Start.Character);
        Assert.Equal(12, defEdit.Range.End.Character);
        Assert.Equal("Bar", defEdit.NewText);
    }

    // A Lua global lives in the script's own state (#249): the same name in another script - a game
    // script in a lower layer among them - is a different global, neither renamed nor in the way.
    [Fact]
    public void HandleRename_LuaGlobal_StaysInTheScriptsOwnState()
    {
        const string mine = "file:///mod/data/scripts/gameobject/interdictor.lua";
        const string theGames = "file:///game/data/scripts/gameobject/darthvader.lua";
        var mySym = LuaGlobal("Definitions", mine);
        var myRef = LuaGlobalRef("Definitions", mine, 2, 0, 11);
        var theirSym = LuaGlobal("Definitions", theGames);
        var theirRef = LuaGlobalRef("Definitions", theGames, 2, 0, 11);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(mine, new DocumentIndex(mine, 1, [mySym], [myRef], LayerRank: 1))
            .Add(theGames, new DocumentIndex(theGames, 1, [theirSym], [theirRef], LayerRank: 0));
        var index = BuildIndex(docs,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Definitions", [mySym, theirSym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("Definitions", [myRef, theirRef]));
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(mine, "function Definitions()\nend\nDefinitions()", 1);
        host.AddOrUpdate(theGames, "function Definitions()\nend\nDefinitions()", 1);
        var sandbox = new FixedSandbox(LuaSandbox.Build(docs, new FileHelper(new MockFileSystem())));

        var result = MakeHandler(host, sandbox: sandbox).HandleRename(mine, RenameAt(2, 3, "Setup", mine), index);

        Assert.Equal([mine], result!.Changes!.Keys.Select(k => k.ToString()));
        Assert.Equal(2, result.Changes[DocumentUri.From(mine)].Count());
    }

    // The state machine calls Definitions: one text in every script's state that loads it, so the
    // rename reaches every such script, or the library stops calling theirs.
    [Fact]
    public void HandleRename_LuaGlobal_ASharedLibraryNamingIt_TakesInEveryScriptLoadingIt()
    {
        const string mine = "file:///mod/data/scripts/gameobject/interdictor.lua";
        const string another = "file:///mod/data/scripts/gameobject/darthvader.lua";
        const string library = "file:///mod/data/scripts/library/pgstatemachine.lua";
        var mySym = LuaGlobal("Definitions", mine);
        var theirSym = LuaGlobal("Definitions", another);
        var call = LuaGlobalRef("Definitions", library, 1, 1, 11);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(mine, new DocumentIndex(mine, 1, [mySym], [], ["PGStateMachine"]))
            .Add(another, new DocumentIndex(another, 1, [theirSym], [], ["PGStateMachine"]))
            .Add(library, new DocumentIndex(library, 1, [], [call]));
        var index = BuildIndex(docs,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Definitions", [mySym, theirSym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("Definitions", [call]));
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(mine, "function Definitions()\nend", 1);
        host.AddOrUpdate(another, "function Definitions()\nend", 1);
        host.AddOrUpdate(library, "if Definitions then\n Definitions()\nend", 1);
        var sandbox = new FixedSandbox(LuaSandbox.Build(docs, new FileHelper(new MockFileSystem())));

        var result = MakeHandler(host, sandbox: sandbox).HandleRename(mine, RenameAt(0, 10, "Setup", mine), index);

        Assert.Equal([another, mine, library], result!.Changes!.Keys.Select(k => k.ToString()).Order());
    }

    // A leaf script redefining a global of a library it loads: in its state the library's definition
    // and calls are the same global.
    private static (GameIndex Index, FakeWorkspaceHost Host, FixedSandbox Sandbox) LeafRedefinesLibraryGlobal(
        int libraryRank, out string mine, out string library)
    {
        mine = "file:///mod/data/scripts/gameobject/interdictor.lua";
        library = "file:///lib/data/scripts/library/pgcommands.lua";
        var another = "file:///lib/data/scripts/gameobject/darthvader.lua";
        var libDef = LuaGlobal("Fire_Weapons", library);
        var libCall = LuaGlobalRef("Fire_Weapons", library, 3, 1, 12);
        var myDef = LuaGlobal("Fire_Weapons", mine);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(mine, new DocumentIndex(mine, 1, [myDef], [], ["PGCommands"], LayerRank: 1))
            .Add(library, new DocumentIndex(library, 1, [libDef], [libCall], LayerRank: libraryRank))
            .Add(another, new DocumentIndex(another, 1, [], [], ["PGCommands"], LayerRank: libraryRank));
        var index = BuildIndex(docs,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Fire_Weapons", [libDef, myDef]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("Fire_Weapons", [libCall]));
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(mine, "function Fire_Weapons()\nend", 1);
        host.AddOrUpdate(library, "function Fire_Weapons()\nend\nfunction Go()\n Fire_Weapons()\nend", 1);
        host.AddOrUpdate(another, "", 1);
        return (index, host, new FixedSandbox(LuaSandbox.Build(docs, new FileHelper(new MockFileSystem()))));
    }

    // Renaming the leaf's copy alone would stop it overriding the library's; the library is a
    // dependency, not the project's to edit - refused, no partial edit.
    [Fact]
    public void HandleRename_LeafRedefiningALowerLayerLibraryGlobal_IsRefused()
    {
        var (index, host, sandbox) = LeafRedefinesLibraryGlobal(0, out var mine, out _);
        var handler = MakeHandler(host, sandbox: sandbox);

        Assert.Null(handler.HandleRename(mine, RenameAt(0, 10, "Fire", mine), index));
        Assert.Null(handler.HandlePrepare(mine, 0, 10, index));
    }

    // The library in the project too: one global across the library, the leaf and every other script
    // loading the library.
    [Fact]
    public void HandleRename_LeafRedefiningALeafLibraryGlobal_RenamesTheLibraryAndEveryScriptLoadingIt()
    {
        var (index, host, sandbox) = LeafRedefinesLibraryGlobal(1, out var mine, out var library);

        var result = MakeHandler(host, sandbox: sandbox).HandleRename(mine, RenameAt(0, 10, "Fire", mine), index);

        Assert.Equal([library, mine], result!.Changes!.Keys.Select(k => k.ToString()).Order());
        Assert.Equal(2, result.Changes[DocumentUri.From(library)].Count());
    }

    public static TheoryData<string, int> SetupsAndLibraryRanks()
    {
        var data = new TheoryData<string, int>();
        foreach (var setup in ProjectSetup.All)
            for (var rank = 0; rank < setup.Layers.Count; rank++)
                data.Add(setup.Name, rank);
        return data;
    }

    // Every shape, every layer the library sits in (#280): the leaf's own library is renamed with the
    // leaf; a lower layer's is a dependency, and the rename is refused whole.
    [Theory]
    [MemberData(nameof(SetupsAndLibraryRanks))]
    public void HandleRename_LeafRedefiningALibraryGlobal_RenamesOnlyWhenTheLibraryIsTheLeafs(string name,
        int libraryRank)
    {
        var setup = ProjectSetup.Named(name);
        var mine = setup.ScriptUri(setup.LeafRank, "GameObject/Interdictor.lua");
        var library = setup.ScriptUri(libraryRank, "Library/PGCommands.lua");
        var index = ProjectSetup.Index(
            setup.Script(setup.LeafRank, "GameObject/Interdictor.lua", ["PGCommands"],
                [ProjectSetup.LuaGlobal("Fire_Weapons", mine)]),
            setup.Script(libraryRank, "Library/PGCommands.lua",
                defines: [ProjectSetup.LuaGlobal("Fire_Weapons", library)],
                uses: [ProjectSetup.LuaGlobalUse("Fire_Weapons", library, 3, 1)]));
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(mine, "function Fire_Weapons()\nend", 1);
        host.AddOrUpdate(library, "function Fire_Weapons()\nend\nfunction Go()\n Fire_Weapons()\nend", 1);
        var fileHelper = new FileHelper(setup.Disk());
        var sandbox = new FixedSandbox(LuaSandbox.Build(index.Documents, fileHelper));

        var result = MakeHandler(host, sandbox: sandbox).HandleRename(mine, RenameAt(0, 10, "Fire", mine), index);

        if (libraryRank != setup.LeafRank)
        {
            Assert.Null(result);
            return;
        }

        Assert.Equal(new[] { library, mine }.Select(fileHelper.NormalizeUri).Order(StringComparer.OrdinalIgnoreCase),
            result!.Changes!.Keys.Select(k => fileHelper.NormalizeUri(k.ToString()))
                .Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    // The game calls main by name: renamed, the script's thread never starts. Refused, and not the
    // analyzer's to rename either.
    [Fact]
    public void HandleRename_AnEntryPointTheGameCalls_IsRefused()
    {
        var sym = LuaGlobal("main", LuaUri);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, MakeDoc(LuaUri, sym)),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("main", [sym]));
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "function main()\nend", 1);
        var entryPoints = new LuaApiSchemaProvider([],
            [new LuaEntryPoint("main", LuaEntryPointCaller.ENGINE, [], ["object"], "The script's thread.")]);
        var handler = MakeHandler(host, luaSchema: entryPoints);

        Assert.Null(handler.HandleRename(LuaUri, RenameAt(0, 10, "Run"), index));
        Assert.Null(handler.HandlePrepare(LuaUri, 0, 10, index));
        Assert.True(handler.Claims(LuaUri, 0, 10, index));
    }

    // ── state names (#252) ───────────────────────────────────────────────────

    private const string MachineLibraryUri = "file:///data/scripts/library/pgstatemachine.lua";

    private static (LuaRenameHandler Handler, GameIndex Index) InMachine(string script)
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, script, 1);
        host.AddOrUpdate(MachineLibraryUri, "function Define_State(s, f) end\n" +
                                            "function Set_Next_State(s) if StateMachine[s] ~= nil then end end\n" +
                                            "function Process_States() _curproc(OnEnter) _curproc(OnUpdate) _curproc(OnExit) end",
            1);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [], ["PGStateMachine"]))
            .Add(MachineLibraryUri, new DocumentIndex(MachineLibraryUri, 1, [], [])));
        return (MakeHandler(host), index);
    }

    // The library looks a state up by the string: every Define_State and Set_Next_State of it moves.
    [Fact]
    public void AStateName_IsRenamedInEveryRegistrationAndTransition()
    {
        var (handler, index) = InMachine("Define_State(\"Attack\", A)\nSet_Next_State(\"Attack\")\n" +
                                         "Set_Next_State(\"Other\")\nfunction A(message) end");

        Assert.True(handler.Claims(LuaUri, 1, 18, index));
        Assert.NotNull(handler.HandlePrepare(LuaUri, 1, 18, index));
        var edit = handler.HandleRename(LuaUri, RenameAt(1, 18, "Assault"), index);

        var edits = edit!.Changes![DocumentUri.From(LuaUri)].ToList();
        Assert.Equal([(0, 14), (1, 16)], edits.Select(e => (e.Range.Start.Line, e.Range.Start.Character)));
        Assert.All(edits, e => Assert.Equal("Assault", e.NewText));
    }

    // A story event's name is the story XML's: renamed here alone, the event would stop reaching it.
    [Fact]
    public void AStoryEventState_IsNotRenamed()
    {
        var (handler, index) = InMachine("StoryModeEvents = { Demo_Start = S }\nfunction S(message) end");

        Assert.Null(handler.HandleRename(LuaUri, RenameAt(0, 22, "Begin"), index));
        Assert.Null(handler.HandlePrepare(LuaUri, 0, 22, index));
    }

    // An XML object is the game's, not a script's: its rename reaches every script naming it (#249).
    [Fact]
    public void HandleRename_XmlObject_ReachesEveryScript_WhateverTheirState()
    {
        const string mine = "file:///mod/data/scripts/story/a.lua";
        const string another = "file:///mod/data/scripts/story/b.lua";
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 0);
        var myRef = LuaRef("UNIT_A", mine, 0, 7, 6);
        var theirRef = LuaRef("UNIT_A", another, 0, 7, 6);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(XmlUri, MakeDoc(XmlUri, sym)).Add(mine, MakeDoc(mine, r: myRef))
            .Add(another, MakeDoc(another, r: theirRef));
        var index = BuildIndex(docs,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [myRef, theirRef]));
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        host.AddOrUpdate(mine, "Spawn(\"UNIT_A\")", 1);
        host.AddOrUpdate(another, "Spawn(\"UNIT_A\")", 1);
        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });
        var sandbox = new FixedSandbox(LuaSandbox.Build(docs, new FileHelper(new MockFileSystem())));

        var result = MakeHandler(host, schema, sandbox: sandbox)
            .HandleRename(mine, RenameAt(0, 7, "UNIT_B", mine), index);

        Assert.Equal([mine, another, XmlUri], result!.Changes!.Keys.Select(k => k.ToString()).Order());
    }

    [Fact]
    public void HandleRename_LuaGlobal_DependencyLayerDefinition_ReturnsNull()
    {
        // LuaGlobal defined in a dep-layer doc (rank 0); the rename builder must block it.
        var depSym = LuaGlobal("DepFunc", "file:///dep/lib.lua");
        var depDoc = new DocumentIndex("file:///dep/lib.lua", 1, [depSym],
            ImmutableArray<GameReference>.Empty, LayerRank: 0);
        var callerRef = LuaGlobalRef("DepFunc", LuaUri, 0, 0, 7);
        var leafDoc = new DocumentIndex(LuaUri, 1, ImmutableArray<GameSymbol>.Empty, [callerRef],
            LayerRank: 1);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate("file:///dep/lib.lua", "function DepFunc() end", 1);
        host.AddOrUpdate(LuaUri, "DepFunc()", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("DepFunc", [depSym]);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
            .Add("DepFunc", [callerRef]);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///dep/lib.lua", depDoc)
                .Add(LuaUri, leafDoc),
            defs,
            refs);

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 0, "NewFunc"), index);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_LuaGlobal_ArchiveOrigin_ReturnsNull()
    {
        // LuaGlobal with MegArchiveOrigin; the builder currently silently produces a ref-only
        // edit - after the upfront IsLeafOwned guard it must return null.
        var archiveSym = new GameSymbol("ArchiveFunc", GameSymbolKind.LuaGlobal, null,
            new MegArchiveOrigin("scripts.meg", "lib.lua", 0, 0), null);
        var callerRef = LuaGlobalRef("ArchiveFunc", LuaUri, 0, 0, 11);
        var leafDoc = new DocumentIndex(LuaUri, 1, ImmutableArray<GameSymbol>.Empty, [callerRef],
            LayerRank: 1);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "ArchiveFunc()", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("ArchiveFunc", [archiveSym]);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
            .Add("ArchiveFunc", [callerRef]);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, leafDoc),
            defs,
            refs);

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 0, "NewFunc"), index);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_CursorOnUnknownIdentifier_ReturnsNull()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "local x = 1", 1);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, MakeDoc(LuaUri)));

        var result = MakeHandler(host).HandleRename(LuaUri, RenameAt(0, 6, "y"), index);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_FileNotInHostAndNotOnDisk_ReturnsNull()
    {
        var result = MakeHandler().HandleRename(LuaUri, RenameAt(0, 0, "X"), BuildIndex());
        Assert.Null(result);
    }

    // ── HandlePrepare: LuaGlobal path ─────────────────────────────────────────

    [Fact]
    public void HandlePrepare_CursorOnGlobalRef_ReturnsRange()
    {
        var callerRef = LuaGlobalRef("RunMission", LuaUri, 0, 0, 10);
        var sym = LuaGlobal("RunMission", OtherLuaUri);
        var callerDoc = MakeDoc(LuaUri, r: callerRef);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("RunMission", [sym]);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, callerDoc), defs);

        var result = MakeHandler().HandlePrepare(LuaUri, 0, 5, index);

        Assert.NotNull(result);
        Assert.NotNull(result.Range);
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(0, result.Range.Start.Character);
        Assert.Equal(10, result.Range.End.Character);
    }

    [Fact]
    public void HandlePrepare_CursorOnGlobalDeclaration_ReturnsExtendedRange()
    {
        var sym = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null, new FileOrigin(LuaUri, 0, 9), null);
        var defDoc = MakeDoc(LuaUri, sym);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Foo", [sym]);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, defDoc), defs);

        var result = MakeHandler().HandlePrepare(LuaUri, 0, 9, index);

        Assert.NotNull(result);
        Assert.NotNull(result.Range);
        // Zero-length [0:9,0:9] extended by "Foo".Length = 3 → [0:9, 0:12]
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(9, result.Range.Start.Character);
        Assert.Equal(12, result.Range.End.Character);
    }

    [Fact]
    public void HandlePrepare_LuaGlobal_NoDocumentInIndex_ReturnsNull()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var result = MakeHandler(host).HandlePrepare(LuaUri, 0, 0, BuildIndex());
        Assert.Null(result);
    }

    // ── HandlePrepare: XmlObject string path ──────────────────────────────────

    [Fact]
    public void HandlePrepare_CursorOnXmlObjectString_ReturnsInnerRange()
    {
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var luaDoc = MakeDoc(LuaUri);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc), defs);

        var result = MakeHandler(host).HandlePrepare(LuaUri, 0, 8, index);

        Assert.NotNull(result);
        Assert.NotNull(result.Range);
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(7, result.Range.Start.Character);
        Assert.Equal(13, result.Range.End.Character);
    }

    [Fact]
    public void HandlePrepare_CursorOnXmlObjectString_ArchiveDefinition_ReturnsNull()
    {
        var sym = XmlSymbolInArchive("VANILLA_UNIT");
        var luaDoc = MakeDoc(LuaUri);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"VANILLA_UNIT\")", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("VANILLA_UNIT", [sym]);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc), defs);

        var result = MakeHandler(host).HandlePrepare(LuaUri, 0, 8, index);
        Assert.Null(result);
    }

    [Fact]
    public void HandlePrepare_XmlObjectString_DependencyLayerDefinition_ReturnsNull()
    {
        // Symbol is FileOrigin in a dep-layer doc (rank 0); leaf Lua doc is rank 1.
        // Prepare-rename on the string literal must return null.
        var depSym = XmlSymbolAt("UNIT_DEP", "file:///dep/units.xml", 5);
        var depDoc = new DocumentIndex("file:///dep/units.xml", 1, [depSym],
            ImmutableArray<GameReference>.Empty, LayerRank: 0);
        var luaDoc = new DocumentIndex(LuaUri, 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty, LayerRank: 1);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_DEP\")", 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_DEP", [depSym]);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///dep/units.xml", depDoc)
                .Add(LuaUri, luaDoc),
            defs);

        var result = MakeHandler(host).HandlePrepare(LuaUri, 0, 8, index);
        Assert.Null(result);
    }

    [Fact]
    public void HandlePrepare_LuaGlobal_DependencyLayerDefinition_ReturnsNull()
    {
        // LuaGlobal defined in a dep-layer doc (rank 0); cursor is on a ref in the leaf doc (rank 1).
        // Prepare-rename must return null.
        var depSym = new GameSymbol("DepFunc", GameSymbolKind.LuaGlobal, null,
            new FileOrigin("file:///dep/lib.lua", 0, null), null);
        var depDoc = new DocumentIndex("file:///dep/lib.lua", 1, [depSym],
            ImmutableArray<GameReference>.Empty, LayerRank: 0);
        var callerRef = LuaGlobalRef("DepFunc", LuaUri, 0, 0, 7);
        var luaDoc = new DocumentIndex(LuaUri, 1, ImmutableArray<GameSymbol>.Empty, [callerRef],
            LayerRank: 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("DepFunc", [depSym]);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///dep/lib.lua", depDoc)
                .Add(LuaUri, luaDoc),
            defs);

        var result = MakeHandler().HandlePrepare(LuaUri, 0, 3, index);
        Assert.Null(result);
    }

    [Fact]
    public void HandlePrepare_CursorOnUnknownString_ReturnsNull()
    {
        var luaDoc = MakeDoc(LuaUri);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"NOT_A_GAME_OBJECT\")", 1);

        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc));

        var result = MakeHandler(host).HandlePrepare(LuaUri, 0, 8, index);
        Assert.Null(result);
    }

    [Fact]
    public void HandlePrepare_NotInHostAndNotInIndex_ReturnsNull()
    {
        var result = MakeHandler().HandlePrepare(LuaUri, 0, 0, BuildIndex());
        Assert.Null(result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

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

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, GameObjectTypeDefinition> _types =
            new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string _)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _types.GetValueOrDefault(name);
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public EnumDefinition? GetEnum(string _)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void RegisterType(GameObjectTypeDefinition def)
        {
            _types[def.TypeName] = def;
        }
    }
}