// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string OtherXmlUri = "file:///other.xml";
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

    private static GameReference XmlRef(string id, string uri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, "Unit", uri, line, col, len);
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

    private static LuaRenameHandler MakeHandler(FakeWorkspaceHost? host = null, FakeSchemaProvider? schema = null)
    {
        return new LuaRenameHandler(
            host ?? new FakeWorkspaceHost(),
            schema ?? new FakeSchemaProvider(),
            new FileHelper(new MockFileSystem()),
            NullLogger<LuaRenameHandler>.Instance);
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
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(XmlUri)));
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
        var luaEdits = result!.Changes![DocumentUri.From(LuaUri)].ToList();
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
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(OtherLuaUri)));
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
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
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
        var edits = result!.Changes![DocumentUri.From(LuaUri)].ToList();
        var defEdit = edits.First(e => e.Range.Start.Character == 9);
        Assert.Equal(0, defEdit.Range.Start.Line);
        Assert.Equal(9, defEdit.Range.Start.Character);
        Assert.Equal(12, defEdit.Range.End.Character);
        Assert.Equal("Bar", defEdit.NewText);
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
        // edit — after the upfront IsLeafOwned guard it must return null.
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
        Assert.NotNull(result!.Range);
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
        Assert.NotNull(result!.Range);
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
        Assert.NotNull(result!.Range);
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

    private sealed class FakeGameIndexService : IGameIndexService
    {
        public GameIndex Current { get; } = GameIndex.Empty;
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

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
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
}