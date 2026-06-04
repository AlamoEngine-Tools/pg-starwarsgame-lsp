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

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class GameRenameHandlerTest
{
    private const string XmlUri = "file:///test.xml";
    private const string OtherXmlUri = "file:///other.xml";
    private const string LuaUri = "file:///script.lua";
    private const string OtherLuaUri = "file:///lib.lua";

    // ── helpers ────────────────────────────────────────────────────────────────

    private static RenameParams RenameAt(int line, int character, string newName, string uri = XmlUri)
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
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new MegArchiveOrigin("data.meg", "units.xml", 0, 0),
            null);
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

    private static GameIndex BuildIndex(
        ImmutableDictionary<string, DocumentIndex>? docs = null,
        ImmutableDictionary<string, ImmutableArray<GameSymbol>>? defs = null,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? refs = null)
    {
        return new GameIndex(
            BaselineIndex.Empty,
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

    private static DocumentIndex LuaDoc(string uri)
    {
        return new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
    }

    private static GameRenameHandler BuildHandler(
        GameIndex index,
        FakeWorkspaceHost? host = null,
        FakeSchemaProvider? schema = null,
        IEaWXmlContext? ctx = null,
        IFileHelper? fileHelper = null)
    {
        return new GameRenameHandler(
            new FakeIndexService { Current = index },
            host ?? new FakeWorkspaceHost(),
            schema ?? new FakeSchemaProvider(),
            ctx ?? new AllowAllEaWContext(),
            fileHelper ?? new FileHelper(new MockFileSystem()),
            NullLogger<GameRenameHandler>.Instance);
    }

    // ── file-type gating ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownFileType_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(RenameAt(0, 0, "X", "file:///data.txt"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_XmlFile_NotEaWContext_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, ctx: new DenyAllEaWContext());
        var result = await handler.Handle(RenameAt(0, 0, "X"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_XmlFile_NoCursorHit_ReturnsNull()
    {
        var doc = XmlDoc(XmlUri);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc));
        var handler = BuildHandler(index);
        var result = await handler.Handle(RenameAt(0, 0, "X"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_XmlFile_SymbolFromArchive_ReturnsNull()
    {
        var sym = XmlSymbolInArchive("UNIT_VANILLA");
        var r = XmlRef("UNIT_VANILLA", XmlUri, 0, 4, 12);
        var doc = XmlDoc(XmlUri, r: r);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_VANILLA", ImmutableArray.Create(sym));
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc),
            defs);
        var handler = BuildHandler(index);
        var result = await handler.Handle(RenameAt(0, 5, "UNIT_NEW"), CancellationToken.None);
        Assert.Null(result);
    }

    // ── XML → XmlObject rename ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_XmlFile_CursorOnReference_ProducesReferenceEdit()
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
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(r)));

        var handler = BuildHandler(index, host, schema);
        var result = await handler.Handle(RenameAt(0, 12, "UNIT_B"), CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(XmlUri)].ToList();
        var refEdit = edits.First(e => e.Range.Start.Line == 0);
        Assert.Equal(10, refEdit.Range.Start.Character);
        Assert.Equal(16, refEdit.Range.End.Character);
        Assert.Equal("UNIT_B", refEdit.NewText);
    }

    [Fact]
    public async Task Handle_XmlFile_CursorOnDefinition_ProducesDefinitionEdit()
    {
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var doc = XmlDoc(XmlUri, sym);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(XmlUri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_A", ImmutableArray<GameReference>.Empty));

        var handler = BuildHandler(index, host, schema);
        // cursor on "UNIT_A" inside Name="UNIT_A" on line 1
        var result = await handler.Handle(RenameAt(1, 5, "UNIT_NEW"), CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(XmlUri)].ToList();
        var defEdit = Assert.Single(edits);
        Assert.Equal("UNIT_NEW", defEdit.NewText);
        // "<Unit Name="UNIT_A"/>" — value starts at col 12
        Assert.Equal(1, defEdit.Range.Start.Line);
        Assert.Equal(12, defEdit.Range.Start.Character);
        Assert.Equal(18, defEdit.Range.End.Character);
    }

    [Fact]
    public async Task Handle_XmlFile_CursorOnReference_AlsoRenamesLuaStringLiterals()
    {
        var xmlRef = XmlRef("UNIT_A", XmlUri, 0, 4, 6);
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", OtherXmlUri, 1);
        var xmlDoc = XmlDoc(XmlUri, r: xmlRef);
        var luaDoc = LuaDoc(LuaUri);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Ref>UNIT_A</Ref>", 1);
        host.AddOrUpdate(OtherXmlUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(XmlUri, xmlDoc)
                .Add(LuaUri, luaDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(xmlRef, luaRef)));

        var handler = BuildHandler(index, host, schema);
        var result = await handler.Handle(RenameAt(0, 5, "UNIT_B"), CancellationToken.None);

        Assert.NotNull(result);
        // Lua file must be in the changes
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
        var luaEdits = result.Changes[DocumentUri.From(LuaUri)].ToList();
        Assert.Contains(luaEdits, e => e.NewText == "UNIT_B");
    }

    [Fact]
    public async Task Handle_XmlFile_LuaRefIndexed_ProducesEditWithoutHostText()
    {
        // When a Lua ref is in the index, the edit must come from the index directly —
        // even if the Lua file is not open in the workspace host (no text to scan).
        var xmlRef = XmlRef("UNIT_A", XmlUri, 0, 4, 6);
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", OtherXmlUri, 0);
        var xmlDoc = XmlDoc(XmlUri, r: xmlRef);
        var luaDoc = LuaDoc(LuaUri);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Ref>UNIT_A</Ref>", 1);
        host.AddOrUpdate(OtherXmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        // LuaUri deliberately NOT added to host

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(XmlUri, xmlDoc)
                .Add(LuaUri, luaDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(xmlRef, luaRef)));

        var handler = BuildHandler(index, host, schema);
        var result = await handler.Handle(RenameAt(0, 5, "UNIT_B"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
        var luaEdit = Assert.Single(result.Changes[DocumentUri.From(LuaUri)]);
        Assert.Equal(0, luaEdit.Range.Start.Line);
        Assert.Equal(7, luaEdit.Range.Start.Character);
        Assert.Equal(13, luaEdit.Range.End.Character);
        Assert.Equal("UNIT_B", luaEdit.NewText);
    }

    [Fact]
    public async Task Handle_XmlFile_CrossFileRename_UpdatesAllXmlDocuments()
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
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(XmlUri, testDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(refInTest, refInOther)));

        var handler = BuildHandler(index, host, schema);
        var result = await handler.Handle(RenameAt(0, 5, "UNIT_Z"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(XmlUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(OtherXmlUri)));
    }

    // ── Lua → XmlObject rename (cursor on string literal) ─────────────────────

    [Fact]
    public async Task Handle_LuaFile_CursorOnXmlObjectString_ReturnsCrossFileEdit()
    {
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 1);
        var xmlDoc = XmlDoc(XmlUri, sym);
        var luaDoc = LuaDoc(LuaUri);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(XmlUri, xmlDoc)
                .Add(LuaUri, luaDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(luaRef)));

        var handler = BuildHandler(index, host, schema);
        // cursor at position 7 inside "UNIT_A" in the Lua file
        var result = await handler.Handle(RenameAt(0, 7, "UNIT_B", LuaUri), CancellationToken.None);

        Assert.NotNull(result);
        // Must update both the XML definition and the Lua string literal
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(XmlUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(LuaUri)));
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnXmlObjectString_LuaEditRangeSkipsQuote()
    {
        // Spawn("UNIT_A") — "UNIT_A" starts at col 6 (the opening quote).
        // The edit range should point to UNIT_A (not the quote), i.e., col 7..13.
        var luaRef = LuaRef("UNIT_A", LuaUri, 0, 7, 6);
        var sym = XmlSymbolAt("UNIT_A", XmlUri, 0);
        var xmlDoc = XmlDoc(XmlUri, sym);
        var luaDoc = LuaDoc(LuaUri);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        host.AddOrUpdate(LuaUri, "Spawn(\"UNIT_A\")", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(XmlUri, xmlDoc)
                .Add(LuaUri, luaDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(luaRef)));

        var handler = BuildHandler(index, host, schema);
        var result = await handler.Handle(RenameAt(0, 7, "UNIT_B", LuaUri), CancellationToken.None);

        Assert.NotNull(result);
        var luaEdits = result!.Changes![DocumentUri.From(LuaUri)].ToList();
        var edit = Assert.Single(luaEdits);
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(7, edit.Range.Start.Character); // after opening quote
        Assert.Equal(13, edit.Range.End.Character); // before closing quote
        Assert.Equal("UNIT_B", edit.NewText);
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnUnknownString_ReturnsNull()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"NOT_A_GAME_OBJECT\")", 1);

        var luaDoc = LuaDoc(LuaUri);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc));

        var handler = BuildHandler(index, host);
        // cursor inside the string
        var result = await handler.Handle(RenameAt(0, 8, "SOMETHING_ELSE", LuaUri), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnXmlObjectString_XmlArchiveDefinition_ReturnsNull()
    {
        // Definition is in an archive (not FileOrigin) — rename must be blocked.
        var sym = XmlSymbolInArchive("VANILLA_UNIT");
        var luaDoc = LuaDoc(LuaUri);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "Spawn(\"VANILLA_UNIT\")", 1);

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("VANILLA_UNIT", ImmutableArray.Create(sym)));

        var handler = BuildHandler(index, host);
        var result = await handler.Handle(RenameAt(0, 8, "NEW_UNIT", LuaUri), CancellationToken.None);
        Assert.Null(result);
    }

    // ── Lua → LuaGlobal rename ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LuaFile_CursorOnLuaGlobalCallSite_ReturnsLuaOnlyEdit()
    {
        // callerRef is the indexed LuaGlobal reference emitted by LuaGameDocumentParser for
        // the "RunMission()" call site — rename must use the index, not re-parse host text.
        var callerRef = new GameReference("RunMission", GameSymbolKind.LuaGlobal, null, LuaUri, 0, 0, 10);
        var sym = LuaGlobal("RunMission", OtherLuaUri);
        var callerDoc =
            new DocumentIndex(LuaUri, 1, ImmutableArray<GameSymbol>.Empty, ImmutableArray.Create(callerRef));
        var defDoc = new DocumentIndex(OtherLuaUri, 1, ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(OtherLuaUri, "function RunMission() end", 1);
        host.AddOrUpdate(LuaUri, "RunMission()", 1);

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(OtherLuaUri, defDoc)
                .Add(LuaUri, callerDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("RunMission", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("RunMission", ImmutableArray.Create(callerRef)));

        var handler = BuildHandler(index, host);
        // cursor on "RunMission" call at col 0
        var result = await handler.Handle(RenameAt(0, 0, "ExecuteMission", LuaUri), CancellationToken.None);

        Assert.NotNull(result);
        // Definition file must be updated
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(OtherLuaUri)));
        // Caller file must be updated
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(LuaUri)));
        // XML file must NOT be in changes
        Assert.False(result.Changes.ContainsKey(DocumentUri.From(XmlUri)));
    }

    [Fact]
    public async Task Handle_LuaFile_LuaGlobalCallSite_IndexedRef_ProducesEditWhenCallerFileNotInHost()
    {
        // The call site is tracked in the index but the caller file is not open in the
        // workspace host.  BuildLuaGlobalEdit must read refs from the index (O(1)) rather
        // than re-scanning host text (O(N) re-parse), so the edit is still produced.
        var callerRef = new GameReference("RunMission", GameSymbolKind.LuaGlobal, null, LuaUri, 0, 0, 10);
        var sym = LuaGlobal("RunMission", OtherLuaUri);
        var defDoc = new DocumentIndex(OtherLuaUri, 1, ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);
        var callerDoc =
            new DocumentIndex(LuaUri, 1, ImmutableArray<GameSymbol>.Empty, ImmutableArray.Create(callerRef));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(OtherLuaUri, "function RunMission() end", 1);
        // LuaUri deliberately NOT added to host

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(OtherLuaUri, defDoc)
                .Add(LuaUri, callerDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("RunMission", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("RunMission", ImmutableArray.Create(callerRef)));

        var handler = BuildHandler(index, host);
        // cursor on the definition in OtherLuaUri
        var result = await handler.Handle(RenameAt(0, 9, "ExecuteMission", OtherLuaUri), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
        var edit = Assert.Single(result.Changes[DocumentUri.From(LuaUri)]);
        Assert.Equal("ExecuteMission", edit.NewText);
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
        Assert.Equal(10, edit.Range.End.Character);
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnLuaGlobalDefinition_DefinitionEditRangeIsCorrect()
    {
        var sym = LuaGlobal("Foo", LuaUri);
        var defDoc = new DocumentIndex(LuaUri, 1, ImmutableArray.Create(sym), ImmutableArray<GameReference>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "function Foo() end", 1);

        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, defDoc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("Foo", ImmutableArray.Create(sym)));

        var handler = BuildHandler(index, host);
        // cursor on "Foo" at col 9
        var result = await handler.Handle(RenameAt(0, 9, "Bar", LuaUri), CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(LuaUri)].ToList();
        var defEdit = edits.First(e => e.Range.Start.Character == 9);
        Assert.Equal(0, defEdit.Range.Start.Line);
        Assert.Equal(9, defEdit.Range.Start.Character);
        Assert.Equal(12, defEdit.Range.End.Character);
        Assert.Equal("Bar", defEdit.NewText);
    }

    [Fact]
    public async Task Handle_LuaFile_CursorOnUnknownIdentifier_ReturnsNull()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(LuaUri, "local x = 1", 1);

        var luaDoc = LuaDoc(LuaUri);
        var index = BuildIndex(
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, luaDoc));

        var handler = BuildHandler(index, host);
        var result = await handler.Handle(RenameAt(0, 6, "y", LuaUri), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_LuaFile_FileNotInWorkspaceHostAndNotOnDisk_ReturnsNull()
    {
        var index = BuildIndex();
        var handler = BuildHandler(index, new FakeWorkspaceHost());
        var result = await handler.Handle(RenameAt(0, 0, "X", LuaUri), CancellationToken.None);
        Assert.Null(result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
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
            System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<string>> bones)
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

        public void AddOrUpdate(string uri, string text, int version)
        {
            _docs[uri] = new TrackedDocument(uri, text, version);
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

    private sealed class AllowAllEaWContext : IEaWXmlContext
    {
        public bool IsEaWXmlFile(string fileUri)
        {
            return true;
        }
    }

    private sealed class DenyAllEaWContext : IEaWXmlContext
    {
        public bool IsEaWXmlFile(string fileUri)
        {
            return false;
        }
    }
}