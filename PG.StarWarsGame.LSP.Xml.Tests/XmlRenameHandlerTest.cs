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

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string OtherXmlUri = "file:///other.xml";
    private const string LuaUri = "file:///script.lua";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static RenameParams RenameAt(int line, int character, string newName)
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(XmlUri) },
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

    private static GameReference XmlRef(string id, string uri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, "Unit", uri, line, col, len);
    }

    private static GameReference LuaRef(string id, string uri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, null, uri, line, col, len);
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

    private static DocumentIndex XmlDoc(string uri, GameSymbol? sym = null, GameReference? r = null)
    {
        var syms = sym is null ? ImmutableArray<GameSymbol>.Empty : ImmutableArray.Create(sym);
        var refs = r is null ? ImmutableArray<GameReference>.Empty : ImmutableArray.Create(r);
        return new DocumentIndex(uri, 1, syms, refs);
    }

    private static XmlRenameHandler MakeHandler(
        FakeWorkspaceHost? host = null,
        FakeSchemaProvider? schema = null,
        IEaWXmlContext? ctx = null,
        IFileHelper? fileHelper = null)
    {
        return new XmlRenameHandler(
            ctx ?? new AllowAllContext(),
            host ?? new FakeWorkspaceHost(),
            schema ?? new FakeSchemaProvider(),
            fileHelper ?? new FileHelper(new MockFileSystem()),
            NullLogger<XmlRenameHandler>.Instance);
    }

    // ── HandleRename ──────────────────────────────────────────────────────────

    [Fact]
    public void HandleRename_NotEaWContext_ReturnsNull()
    {
        var handler = MakeHandler(ctx: new DenyAllContext());
        var result = handler.HandleRename(XmlUri, RenameAt(0, 0, "X"), GameIndex.Empty);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_NoCursorHit_ReturnsNull()
    {
        var doc = XmlDoc(XmlUri);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc));
        var result = MakeHandler().HandleRename(XmlUri, RenameAt(0, 0, "X"), index);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_SymbolFromArchive_ReturnsNull()
    {
        var sym = XmlSymbolInArchive("UNIT_VANILLA");
        var r = XmlRef("UNIT_VANILLA", XmlUri, 0, 4, 12);
        var doc = XmlDoc(XmlUri, r: r);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_VANILLA", [sym]);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc), defs);

        var result = MakeHandler().HandleRename(XmlUri, RenameAt(0, 5, "UNIT_NEW"), index);
        Assert.Null(result);
    }

    [Fact]
    public void HandleRename_CursorOnReference_ProducesReferenceEdit()
    {
        var r = XmlRef("UNIT_A", XmlUri, 0, 10, 6);
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var doc = XmlDoc(XmlUri, r: r);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Spawn_Unit>UNIT_A</Spawn_Unit>\n<Unit Name=\"UNIT_A\"/>", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [r]));

        var result = MakeHandler(host, schema).HandleRename(XmlUri, RenameAt(0, 12, "UNIT_B"), index);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(XmlUri)].ToList();
        var refEdit = edits.First(e => e.Range.Start.Line == 0);
        Assert.Equal(10, refEdit.Range.Start.Character);
        Assert.Equal(16, refEdit.Range.End.Character);
        Assert.Equal("UNIT_B", refEdit.NewText);
    }

    [Fact]
    public void HandleRename_CursorOnDefinition_ProducesDefinitionEdit()
    {
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var doc = XmlDoc(XmlUri, sym);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", []));

        var result = MakeHandler(host, schema).HandleRename(XmlUri, RenameAt(1, 5, "UNIT_NEW"), index);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(XmlUri)].ToList();
        var defEdit = Assert.Single(edits);
        Assert.Equal("UNIT_NEW", defEdit.NewText);
        Assert.Equal(1, defEdit.Range.Start.Line);
        Assert.Equal(12, defEdit.Range.Start.Character);
        Assert.Equal(18, defEdit.Range.End.Character);
    }

    [Fact]
    public void HandleRename_CursorOnReference_AlsoRenamesLuaStringLiterals()
    {
        var xmlRef = XmlRef("UNIT_A", XmlUri, 0, 4, 6);
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", OtherXmlUri, 1);
        var xmlDoc = XmlDoc(XmlUri, r: xmlRef);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Ref>UNIT_A</Ref>", 1);
        host.AddOrUpdate(OtherXmlUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, xmlDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [xmlRef, luaRef]));

        var result = MakeHandler(host, schema).HandleRename(XmlUri, RenameAt(0, 5, "UNIT_B"), index);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
        var luaEdits = result.Changes[DocumentUri.From(LuaUri)].ToList();
        Assert.Contains(luaEdits, e => e.NewText == "UNIT_B");
    }

    [Fact]
    public void HandleRename_LuaRefIndexed_ProducesEditFromIndex()
    {
        var xmlRef = XmlRef("UNIT_A", XmlUri, 0, 4, 6);
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", OtherXmlUri, 0);
        var xmlDoc = XmlDoc(XmlUri, r: xmlRef);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Ref>UNIT_A</Ref>", 1);
        host.AddOrUpdate(OtherXmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        // LuaUri deliberately NOT in host

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, xmlDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [xmlRef, luaRef]));

        var result = MakeHandler(host, schema).HandleRename(XmlUri, RenameAt(0, 5, "UNIT_B"), index);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
        var luaEdit = Assert.Single(result.Changes[DocumentUri.From(LuaUri)]);
        Assert.Equal(0, luaEdit.Range.Start.Line);
        Assert.Equal(7, luaEdit.Range.Start.Character);
        Assert.Equal(13, luaEdit.Range.End.Character);
        Assert.Equal("UNIT_B", luaEdit.NewText);
    }

    [Fact]
    public void HandleRename_CrossFileRename_UpdatesAllXmlDocuments()
    {
        var refInTest = XmlRef("UNIT_A", XmlUri, 0, 4, 6);
        var refInOther = XmlRef("UNIT_A", OtherXmlUri, 1, 5, 6);
        var sym = XmlSymbolAt("UNIT_A", OtherXmlUri, 0);
        var testDoc = XmlDoc(XmlUri, r: refInTest);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Spawn>UNIT_A</Spawn>", 1);
        host.AddOrUpdate(OtherXmlUri, "<Unit Name=\"UNIT_A\"/>\n<Child>UNIT_A</Child>", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, testDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [refInTest, refInOther]));

        var result = MakeHandler(host, schema).HandleRename(XmlUri, RenameAt(0, 5, "UNIT_Z"), index);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(XmlUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(OtherXmlUri)));
    }

    // ── HandlePrepare ─────────────────────────────────────────────────────────

    [Fact]
    public void HandlePrepare_NotEaWXmlFile_ReturnsNull()
    {
        var result = MakeHandler(ctx: new DenyAllContext())
            .HandlePrepare(XmlUri, 0, 0, GameIndex.Empty);
        Assert.Null(result);
    }

    [Fact]
    public void HandlePrepare_NoCursorHit_ReturnsNull()
    {
        var doc = XmlDoc(XmlUri);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc));
        var result = MakeHandler().HandlePrepare(XmlUri, 0, 0, index);
        Assert.Null(result);
    }

    [Fact]
    public void HandlePrepare_CursorOnReference_ReturnsRange()
    {
        var r = XmlRef("UNIT_A", XmlUri, 0, 10, 6);
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var doc = XmlDoc(XmlUri, r: r);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc), defs);

        var result = MakeHandler().HandlePrepare(XmlUri, 0, 12, index);

        Assert.NotNull(result);
        Assert.NotNull(result!.Range);
        Assert.Equal(0, result.Range!.Start.Line);
        Assert.Equal(10, result.Range.Start.Character);
        Assert.Equal(16, result.Range.End.Character);
    }

    [Fact]
    public void HandlePrepare_CursorOnSymbol_ReturnsRange()
    {
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var doc = XmlDoc(XmlUri, sym);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc), defs);

        var result = MakeHandler().HandlePrepare(XmlUri, 1, 3, index);

        Assert.NotNull(result);
        Assert.NotNull(result!.Range);
        Assert.Equal(1, result.Range!.Start.Line);
    }

    [Fact]
    public void HandlePrepare_SymbolFromArchive_ReturnsNull()
    {
        var sym = XmlSymbolInArchive("UNIT_VANILLA");
        var r = XmlRef("UNIT_VANILLA", XmlUri, 0, 4, 12);
        var doc = XmlDoc(XmlUri, r: r);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_VANILLA", [sym]);
        var index = BuildIndex(ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc), defs);

        var result = MakeHandler().HandlePrepare(XmlUri, 0, 5, index);
        Assert.Null(result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version)
            => _docs[uri] = new TrackedDocument(uri, text, version);

        public void Remove(string uri) => _docs.Remove(uri);

        public bool TryGet(string uri, out TrackedDocument doc)
            => _docs.TryGetValue(uri, out doc!);

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, GameObjectTypeDefinition> _types =
            new(StringComparer.OrdinalIgnoreCase);

        public void RegisterType(GameObjectTypeDefinition def) => _types[def.TypeName] = def;
        public XmlTagDefinition? GetTag(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public GameObjectTypeDefinition? GetObjectType(string name) => _types.GetValueOrDefault(name);
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public EnumDefinition? GetEnum(string _) => null;
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public event EventHandler? SchemaRefreshed { add { } remove { } }
    }

    private sealed class AllowAllContext : IEaWXmlContext
    {
        public bool HasDirectories => true;
        public bool IsEaWXmlFile(string fileUri) => true;
        public void AddDirectory(string absolutePath) { }
        public void SetDirectories(IEnumerable<string> absolutePaths) { }
    }

    private sealed class DenyAllContext : IEaWXmlContext
    {
        public bool HasDirectories => false;
        public bool IsEaWXmlFile(string fileUri) => false;
        public void AddDirectory(string absolutePath) { }
        public void SetDirectories(IEnumerable<string> absolutePaths) { }
    }
}
