// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Rename;

public sealed class DynamicEnumValueRenameBuilderTest
{
    private const string DefUri = "file:///mods/mymod/data/xml/gameconstants.xml";
    private const string EnumFileUri = "file:///mods/mymod/data/xml/enum/gameobjectcategorytype.xml";
    private const string RefUri = "file:///mods/mymod/data/xml/units.xml";

    private static GameIndex IndexWith(
        ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> defs,
        ImmutableDictionary<string, ImmutableArray<GameReference>>? refs = null,
        ImmutableDictionary<string, DocumentIndex>? docs = null)
    {
        return GameIndex.Empty with
        {
            WorkspaceEnumValueDefinitions = defs,
            WorkspaceReferences = refs ?? ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty,
            Documents = docs ?? ImmutableDictionary<string, DocumentIndex>.Empty
        };
    }

    private static DocumentIndex EmptyDoc(string uri, int layerRank)
    {
        return new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty,
            LayerRank: layerRank);
    }

    [Fact]
    public void Build_ValueNotInWorkspaceDefinitions_ReturnsNull()
    {
        var index = IndexWith(ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty);

        var result = DynamicEnumValueRenameBuilder.Build("ArmorType", "Armor_Structure", "Armor_Renamed",
            index, new FakeWorkspaceHost(), new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void Build_OriginNotNavigable_ReturnsNull()
    {
        var defs = ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty
            .Add("ArmorType", ImmutableDictionary<string, FileOrigin>.Empty
                .Add("Armor_Structure", new FileOrigin("data\\xml\\gameconstants.xml", 5, 10)));
        var index = IndexWith(defs);

        var result = DynamicEnumValueRenameBuilder.Build("ArmorType", "Armor_Structure", "Armor_Renamed",
            index, new FakeWorkspaceHost(), new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void Build_AnchorFormat_RenamesDefinitionToken()
    {
        // path$Element format (e.g. gameconstants.xml's <Armor_Types>...</Armor_Types> text list) —
        // the value is a plain text token at an exact (line, column).
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(DefUri, "<GameConstants><Armor_Types>Armor_Structure Armor_Wall</Armor_Types></GameConstants>", 1);
        var defs = ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty
            .Add("ArmorType", ImmutableDictionary<string, FileOrigin>.Empty
                .Add("Armor_Structure", new FileOrigin(DefUri, 0, 29)));
        var index = IndexWith(defs);

        var result = DynamicEnumValueRenameBuilder.Build("ArmorType", "Armor_Structure", "Armor_Renamed",
            index, host, new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.NotNull(result);
        var edit = Assert.Single(result!.Changes![DocumentUri.From(DefUri)]);
        Assert.Equal("Armor_Renamed", edit.NewText);
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(29, edit.Range.Start.Character);
        Assert.Equal(29 + "Armor_Structure".Length, edit.Range.End.Character);
    }

    [Fact]
    public void Build_BareEnumDefinitionFormat_RenamesOpenAndCloseTag()
    {
        // Bare <EnumDefinition> format (e.g. gameobjectcategorytype.xml) — the value IS the XML
        // element name; Column is null (see DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations).
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(EnumFileUri,
            "<EnumDefinition>\n\t<Structure>\t0x00000400\t</Structure>\n</EnumDefinition>", 1);
        var defs = ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty
            .Add("GameObjectCategoryType", ImmutableDictionary<string, FileOrigin>.Empty
                .Add("Structure", new FileOrigin(EnumFileUri, 1, null)));
        var index = IndexWith(defs);

        var result = DynamicEnumValueRenameBuilder.Build("GameObjectCategoryType", "Structure", "Building",
            index, host, new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.NotNull(result);
        var edits = result!.Changes![DocumentUri.From(EnumFileUri)].OrderBy(e => e.Range.Start.Character).ToList();
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.Equal("Building", e.NewText));
        // "\t<Structure>\t0x00000400\t</Structure>" — opening name starts right after '<' at col 2,
        // closing name starts right after "</" at col 26.
        Assert.Equal(2, edits[0].Range.Start.Character);
        Assert.Equal(2 + "Structure".Length, edits[0].Range.End.Character);
        Assert.Equal(26, edits[1].Range.Start.Character);
        Assert.Equal(26 + "Structure".Length, edits[1].Range.End.Character);
    }

    [Fact]
    public void Build_BareEnumDefinitionFormat_ElementNotFoundOnLine_ReturnsNull()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(EnumFileUri, "<EnumDefinition>\n\t<SomethingElse>1</SomethingElse>\n</EnumDefinition>", 1);
        var defs = ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty
            .Add("GameObjectCategoryType", ImmutableDictionary<string, FileOrigin>.Empty
                .Add("Structure", new FileOrigin(EnumFileUri, 1, null)));
        var index = IndexWith(defs);

        var result = DynamicEnumValueRenameBuilder.Build("GameObjectCategoryType", "Structure", "Building",
            index, host, new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void Build_IncludesAllReferenceOccurrences()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(DefUri, "<GameConstants><Armor_Types>Armor_Structure</Armor_Types></GameConstants>", 1);
        var defs = ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty
            .Add("ArmorType", ImmutableDictionary<string, FileOrigin>.Empty
                .Add("Armor_Structure", new FileOrigin(DefUri, 0, 29)));
        var reference = new GameReference("enum:ArmorType/Armor_Structure", null, null, RefUri, 3, 12, 15);
        var refs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
            .Add("enum:ArmorType/Armor_Structure", [reference]);
        var index = IndexWith(defs, refs);

        var result = DynamicEnumValueRenameBuilder.Build("ArmorType", "Armor_Structure", "Armor_Renamed",
            index, host, new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.NotNull(result);
        Assert.True(result!.Changes!.ContainsKey(DocumentUri.From(RefUri)));
        var refEdit = Assert.Single(result.Changes[DocumentUri.From(RefUri)]);
        Assert.Equal("Armor_Renamed", refEdit.NewText);
        Assert.Equal(3, refEdit.Range.Start.Line);
        Assert.Equal(12, refEdit.Range.Start.Character);
        Assert.Equal(27, refEdit.Range.End.Character);
    }

    // ── layer ownership ──────────────────────────────────────────────────────

    [Fact]
    public void Build_OriginInDependencyLayer_ReturnsNull()
    {
        // Dependency's own gameconstants.xml (rank 0) defines the value; the leaf project (rank 1)
        // never overrode that file. Renaming would edit a shared dependency the leaf doesn't own.
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(DefUri, "<GameConstants><Armor_Types>Armor_Structure</Armor_Types></GameConstants>", 1);
        var defs = ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty
            .Add("ArmorType", ImmutableDictionary<string, FileOrigin>.Empty
                .Add("Armor_Structure", new FileOrigin(DefUri, 0, 29)));
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(DefUri, EmptyDoc(DefUri, 0))
            .Add(RefUri, EmptyDoc(RefUri, 1)); // leaf layer is rank 1
        var index = IndexWith(defs, docs: docs);

        var result = DynamicEnumValueRenameBuilder.Build("ArmorType", "Armor_Structure", "Armor_Renamed",
            index, host, new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void Build_OriginInLeafLayer_Allowed()
    {
        // Same value, but this time the leaf project's own gameconstants.xml (also rank 1, the
        // highest rank) defines it — rename must proceed.
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(DefUri, "<GameConstants><Armor_Types>Armor_Structure</Armor_Types></GameConstants>", 1);
        var defs = ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>.Empty
            .Add("ArmorType", ImmutableDictionary<string, FileOrigin>.Empty
                .Add("Armor_Structure", new FileOrigin(DefUri, 0, 29)));
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(DefUri, EmptyDoc(DefUri, 1))
            .Add(RefUri, EmptyDoc(RefUri, 1));
        var index = IndexWith(defs, docs: docs);

        var result = DynamicEnumValueRenameBuilder.Build("ArmorType", "Armor_Structure", "Armor_Renamed",
            index, host, new FileHelper(new MockFileSystem()), NullLogger.Instance);

        Assert.NotNull(result);
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
}
