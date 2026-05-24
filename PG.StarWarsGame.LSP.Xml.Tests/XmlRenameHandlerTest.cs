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
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlRenameHandlerTest
{
    private const string TestUri = "file:///test.xml";
    private const string OtherUri = "file:///other.xml";

    private static RenameParams RenameAt(int line, int character, string newName, string uri = TestUri)
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character),
            NewName = newName
        };
    }

    private static GameReference MakeRef(string id, string docUri, int line, int col, int len)
    {
        return new GameReference(id, GameSymbolKind.XmlObject, "Unit", docUri, line, col, len);
    }

    private static GameSymbol SymbolAt(string id, string uri, int line, string typeName = "Unit")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(uri, line, null), null);
    }

    private static GameSymbol SymbolInArchive(string id)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new MegArchiveOrigin("data.meg", "units.xml", 0, 0),
            null);
    }

    private static DocumentIndex DocWithRef(string uri, string refId, int line, int col, int len)
    {
        return new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(MakeRef(refId, uri, line, col, len)));
    }

    private static GameIndex BuildIndex(
        DocumentIndex? callerDoc = null,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? allRefs = null,
        ImmutableDictionary<string, ImmutableArray<GameSymbol>>? allDefs = null)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        if (callerDoc is not null)
            docs = docs.Add(callerDoc.DocumentUri, callerDoc);

        return new GameIndex(
            BaselineIndex.Empty,
            docs,
            allDefs ?? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            allRefs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static XmlRenameHandler BuildHandler(
        GameIndex index,
        FakeWorkspaceHost? host = null,
        FakeSchemaProvider? schema = null,
        IEaWXmlContext? ctx = null,
        IFileHelper? fileHelper = null)
    {
        var svc = new FakeIndexService { Current = index };
        return new XmlRenameHandler(
            svc,
            host ?? new FakeWorkspaceHost(),
            schema ?? new FakeSchemaProvider(),
            NullLogger<XmlRenameHandler>.Instance,
            ctx ?? new AllowAllEaWContext(),
            fileHelper ?? new FileHelper(new MockFileSystem()));
    }

    // ── null / miss cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoDocumentInIndex_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(RenameAt(0, 0, "NEW_NAME"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NoCursorHit_ReturnsNull()
    {
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
        var handler = BuildHandler(BuildIndex(doc));
        var result = await handler.Handle(RenameAt(0, 0, "NEW_NAME"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SymbolInArchive_ReturnsNull()
    {
        var doc = DocWithRef(TestUri, "UNIT_VANILLA", 0, 4, 12);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_VANILLA", ImmutableArray.Create(SymbolInArchive("UNIT_VANILLA")));
        var handler = BuildHandler(BuildIndex(doc, allDefs: defs));

        var result = await handler.Handle(RenameAt(0, 5, "NEW_NAME"), CancellationToken.None);
        Assert.Null(result);
    }

    // ── rename references ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnReference_RenamesAllRefs()
    {
        var callerDoc = DocWithRef(TestUri, "UNIT_A", 0, 4, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(
                MakeRef("UNIT_A", TestUri, 0, 4, 6),
                MakeRef("UNIT_A", OtherUri, 3, 10, 6)));
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 1)));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(TestUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);
        host.AddOrUpdate(OtherUri, "<Root>\n\n\n<Spawn_Unit>UNIT_A</Spawn_Unit>", 1);

        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var handler = BuildHandler(BuildIndex(callerDoc, refs, defs), host, schema);
        var result = await handler.Handle(RenameAt(0, 5, "UNIT_B"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(TestUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(OtherUri)));
        // Verify the reference in OtherUri is renamed
        var otherEdits = result.Changes[DocumentUri.From(OtherUri)].ToList();
        Assert.Contains(otherEdits, e => e.NewText == "UNIT_B" && e.Range.Start.Line == 3);
    }

    [Fact]
    public async Task Handle_CursorOnReference_RenamesRefWithCorrectRange()
    {
        var callerDoc = DocWithRef(TestUri, "UNIT_A", 0, 10, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", TestUri, 0, 10, 6)));
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 1)));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(TestUri, "<Spawn_Unit>UNIT_A</Spawn_Unit>\n<Unit Name=\"UNIT_A\"/>", 1);
        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var handler = BuildHandler(BuildIndex(callerDoc, refs, defs), host, schema);
        var result = await handler.Handle(RenameAt(0, 12, "UNIT_B"), CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(TestUri)].ToList();
        var refEdit = edits.First(e => e.Range.Start.Line == 0);
        Assert.Equal(10, refEdit.Range.Start.Character);
        Assert.Equal(16, refEdit.Range.End.Character);
        Assert.Equal("UNIT_B", refEdit.NewText);
    }

    // ── rename definition ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnDefinition_RenamesNameAttribute()
    {
        // The definition is on line 1 of TestUri: <Unit Name="UNIT_A"/>
        var defDoc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 1)),
            ImmutableArray<GameReference>.Empty);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 1)));
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
            .Add("UNIT_A", ImmutableArray<GameReference>.Empty);

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(TestUri, "<Root>\n<Unit Name=\"UNIT_A\"/>\n</Root>", 1);
        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var handler = BuildHandler(BuildIndex(defDoc, refs, defs), host, schema);
        var result = await handler.Handle(RenameAt(1, 5, "UNIT_NEW"), CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(TestUri)].ToList();
        var defEdit = Assert.Single(edits);
        Assert.Equal("UNIT_NEW", defEdit.NewText);
        // The value "UNIT_A" starts at col 12 in `<Unit Name="UNIT_A"/>` (after `<Unit Name="`)
        Assert.Equal(1, defEdit.Range.Start.Line);
        Assert.Equal(12, defEdit.Range.Start.Character);
        Assert.Equal(18, defEdit.Range.End.Character);
    }

    // ── cross-file rename ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CrossFileRename_UpdatesAllDocuments()
    {
        var callerDoc = DocWithRef(TestUri, "UNIT_A", 0, 4, 6);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add(
            "UNIT_A", ImmutableArray.Create(
                MakeRef("UNIT_A", TestUri, 0, 4, 6),
                MakeRef("UNIT_A", OtherUri, 1, 5, 6)));
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", OtherUri, 0)));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(TestUri, "<Spawn>UNIT_A</Spawn>", 1);
        host.AddOrUpdate(OtherUri, "<Unit Name=\"UNIT_A\"/>\n<Child>UNIT_A</Child>", 1);
        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });

        var handler = BuildHandler(BuildIndex(callerDoc, refs, defs), host, schema);
        var result = await handler.Handle(RenameAt(0, 5, "UNIT_Z"), CancellationToken.None);

        Assert.NotNull(result);
        // Both files should be updated
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(TestUri)));
        Assert.True(result.Changes.ContainsKey(DocumentUri.From(OtherUri)));
        // OtherUri should have 2 edits: the definition and the reference
        var otherEdits = result.Changes[DocumentUri.From(OtherUri)].ToList();
        Assert.Equal(2, otherEdits.Count);
        Assert.All(otherEdits, e => Assert.Equal("UNIT_Z", e.NewText));
    }

    // ── URI normalization ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MixedCaseUri_NormalizesBeforeIndexLookup()
    {
        const string lowercaseUri = "file:///d:/units.xml";
        const string mixedCaseUri = "file:///D:/units.xml";

        var doc = DocWithRef(lowercaseUri, "UNIT_A", 0, 4, 6);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(SymbolAt("UNIT_A", lowercaseUri, 1)));
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(MakeRef("UNIT_A", lowercaseUri, 0, 4, 6)));

        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(lowercaseUri, "<Spawn>UNIT_A</Spawn>", 1);
        var schema = new FakeSchemaProvider();

        var handler = BuildHandler(BuildIndex(doc, refs, defs), host, schema);
        var result = await handler.Handle(RenameAt(0, 5, "UNIT_B", mixedCaseUri), CancellationToken.None);

        Assert.NotNull(result);
    }

    // ── EaW directory gating ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, ctx: new DenyAllEaWContext());
        var result = await handler.Handle(RenameAt(0, 0, "NEW_NAME"), CancellationToken.None);
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
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

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