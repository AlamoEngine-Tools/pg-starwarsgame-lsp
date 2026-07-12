// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Rename;

public sealed class XmlObjectRenameBuilderTest
{
    private const string XmlUri = "file:///units.xml";
    private const string LuaUri = "file:///script.lua";
    private const string OtherXmlUri = "file:///other.xml";

    private static GameIndex BuildIndex(
        ImmutableDictionary<string, ImmutableArray<GameSymbol>>? defs = null,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? refs = null)
    {
        return new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            defs ?? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            refs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static FakeSchemaProvider SchemaWithUnit()
    {
        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" });
        return schema;
    }

    [Fact]
    public void Build_DefinitionBlockedByArchive_ReturnsNull()
    {
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new MegArchiveOrigin("data.meg", "units.xml", 0, 0), null);
        var index = BuildIndex(
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]));

        var result = XmlObjectRenameBuilder.Build("UNIT_A", "UNIT_B", index,
            SchemaWithUnit(), Source(new FakeWorkspaceHost()),
            NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void Build_NoDefinitionsNoRefs_ReturnsNull()
    {
        var result = XmlObjectRenameBuilder.Build("UNIT_A", "UNIT_B", GameIndex.Empty,
            SchemaWithUnit(), Source(new FakeWorkspaceHost()),
            NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void Build_DefinitionEdit_NameAttributeFound()
    {
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(XmlUri, 0, null), null);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        var index = BuildIndex(
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]));

        var result = XmlObjectRenameBuilder.Build("UNIT_A", "UNIT_B", index,
            SchemaWithUnit(), Source(host),
            NullLogger.Instance);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(XmlUri)].ToList();
        var defEdit = Assert.Single(edits);
        Assert.Equal("UNIT_B", defEdit.NewText);
        Assert.Equal(0, defEdit.Range.Start.Line);
        // "<Unit Name=\"UNIT_A\"/>" — Name="UNIT_A" — value starts at col 12
        Assert.Equal(12, defEdit.Range.Start.Character);
        Assert.Equal(18, defEdit.Range.End.Character);
    }

    [Fact]
    public void Build_ReferenceEdit_ProducedFromIndex()
    {
        var r = new GameReference("UNIT_A", GameSymbolKind.XmlObject, "Unit", OtherXmlUri, 2, 5, 6);
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(XmlUri, 0, null), null);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        var index = BuildIndex(
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [r]));

        var result = XmlObjectRenameBuilder.Build("UNIT_A", "UNIT_B", index,
            SchemaWithUnit(), Source(host),
            NullLogger.Instance);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(OtherXmlUri)));
        var refEdit = Assert.Single(result.Changes[DocumentUri.From(OtherXmlUri)]);
        Assert.Equal(2, refEdit.Range.Start.Line);
        Assert.Equal(5, refEdit.Range.Start.Character);
        Assert.Equal(11, refEdit.Range.End.Character);
        Assert.Equal("UNIT_B", refEdit.NewText);
    }

    [Fact]
    public void Build_StoryEventDoubleIndexed_EmitsDefinitionEditOnce()
    {
        // A story event is indexed BOTH as a StoryEvent symbol (matched by the column path) and as
        // a StoryParser object (matched by the Name= nameTag path) at the SAME span. The rename must
        // emit that definition edit once — the client rejects the whole applyEdit if a document has
        // overlapping/duplicate text edits (the real "rename does nothing" bug).
        var storyEvent = new GameSymbol("Story_Ev", GameSymbolKind.XmlObject, "StoryEvent",
            new FileOrigin(XmlUri, 0, 13), null);
        var storyParser = new GameSymbol("Story_Ev", GameSymbolKind.XmlObject, "StoryParser",
            new FileOrigin(XmlUri, 0, 13), null);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Event Name=\"Story_Ev\"/>", 1);
        var schema = new FakeSchemaProvider();
        schema.RegisterType(new GameObjectTypeDefinition { TypeName = "StoryParser", NameTag = "Name" });
        var index = BuildIndex(ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("Story_Ev", [storyEvent, storyParser]));

        var result = XmlObjectRenameBuilder.Build("Story_Ev", "Renamed_Ev", index,
            schema, Source(host), NullLogger.Instance);

        Assert.NotNull(result);
        var edit = Assert.Single(result!.Changes![DocumentUri.From(XmlUri)]); // deduped, not two
        Assert.Equal("Renamed_Ev", edit.NewText);
        Assert.Equal(13, edit.Range.Start.Character);
        Assert.Equal(21, edit.Range.End.Character);
    }

    [Fact]
    public void Build_LuaRef_IncludedInEdits()
    {
        var luaRef = new GameReference("UNIT_A", GameSymbolKind.XmlObject, null, LuaUri, 0, 7, 6);
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(XmlUri, 0, null), null);
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(XmlUri, "<Unit Name=\"UNIT_A\"/>", 1);
        var index = BuildIndex(
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("UNIT_A", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty.Add("UNIT_A", [luaRef]));

        var result = XmlObjectRenameBuilder.Build("UNIT_A", "UNIT_B", index,
            SchemaWithUnit(), Source(host),
            NullLogger.Instance);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(LuaUri)));
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private static DocumentTextSource Source(IGameWorkspaceHost host)
    {
        return new DocumentTextSource(host, new FileHelper(new MockFileSystem()),
            NullLogger<DocumentTextSource>.Instance);
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