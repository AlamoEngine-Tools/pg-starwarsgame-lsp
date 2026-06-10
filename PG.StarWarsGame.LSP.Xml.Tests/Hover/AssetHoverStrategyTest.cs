// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Hover;

public sealed class AssetHoverStrategyTest
{
    private const string DocUri = "file:///test.xml";

    private static HoverContext MakeCtx(
        string xml,
        int line,
        int character,
        ISchemaProvider schema,
        IAssetFileIndex? assetFiles = null)
    {
        var hapDoc = XmlUtility.CreateHtmlDocument(xml);
        XmlUtility.TryGetRootNode(hapDoc, out var rootNode);
        XmlUtility.TryFindNode(hapDoc, line, out var node);
        var index = assetFiles is null
            ? GameIndex.Empty
            : new GameIndex(
                BaselineIndex.Empty with { },
                ImmutableDictionary<string, DocumentIndex>.Empty,
                ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
                ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty)
            { AssetFiles = assetFiles };
        return new HoverContext(DocUri, index, schema,
            hapDoc, rootNode!, node ?? new HtmlDocument().DocumentNode,
            isOnTagName: false, line, character, "en");
    }

    private static StubSchemaProvider SchemaWithTag(
        string tagName, ReferenceKind kind = ReferenceKind.TextureFile)
    {
        var p = new StubSchemaProvider();
        p.SetTag(tagName, new XmlTagDefinition
        {
            Tag = tagName,
            ValueType = XmlValueType.NameReference,
            ReferenceKind = kind
        });
        return p;
    }

    private static IAssetFileIndex WithLooseFile(string path)
        => MergedAssetFileIndex.Merge([], [path]);

    [Fact]
    public void Handle_IsOnTagName_ReturnsNull()
    {
        var hapDoc = XmlUtility.CreateHtmlDocument("<Root><Tex>foo.tga</Tex></Root>");
        XmlUtility.TryGetRootNode(hapDoc, out var rootNode);
        XmlUtility.TryFindNode(hapDoc, 0, out var node);
        var ctx = new HoverContext(DocUri, GameIndex.Empty, new StubSchemaProvider(),
            hapDoc, rootNode!, node ?? new HtmlDocument().DocumentNode,
            isOnTagName: true, 0, 1, "en");
        Assert.Null(new AssetHoverStrategy().Handle(ctx));
    }

    [Fact]
    public void Handle_NoTagDef_ReturnsNull()
    {
        var schema = new StubSchemaProvider(); // no tags registered
        var ctx = MakeCtx("<Root>\n<Texture_File>foo.tga</Texture_File>\n</Root>", 1, 9, schema);
        Assert.Null(new AssetHoverStrategy().Handle(ctx));
    }

    [Fact]
    public void Handle_NonAssetReferenceKind_ReturnsNull()
    {
        var schema = SchemaWithTag("unit_ref", ReferenceKind.XmlObject);
        var ctx = MakeCtx("<Root>\n<unit_ref>UNIT_A</unit_ref>\n</Root>", 1, 9, schema);
        Assert.Null(new AssetHoverStrategy().Handle(ctx));
    }

    [Fact]
    public void Handle_EmptyTagValue_ReturnsNull()
    {
        var schema = SchemaWithTag("texture_file");
        var ctx = MakeCtx("<Root>\n<texture_file></texture_file>\n</Root>", 1, 9, schema,
            WithLooseFile("data/art/foo.tga"));
        Assert.Null(new AssetHoverStrategy().Handle(ctx));
    }

    [Fact]
    public void Handle_AssetNotInIndex_ReturnsNull()
    {
        var schema = SchemaWithTag("texture_file");
        var ctx = MakeCtx("<Root>\n<texture_file>missing.tga</texture_file>\n</Root>", 1, 9, schema,
            MergedAssetFileIndex.Merge([], []));
        Assert.Null(new AssetHoverStrategy().Handle(ctx));
    }

    [Fact]
    public void Handle_TextureInIndex_ReturnsHover()
    {
        var schema = SchemaWithTag("texture_file");
        var ctx = MakeCtx("<Root>\n<texture_file>foo.tga</texture_file>\n</Root>", 1, 9, schema,
            WithLooseFile("data/art/textures/foo.tga"));

        var hover = new AssetHoverStrategy().Handle(ctx);

        Assert.NotNull(hover);
        Assert.Contains("foo.tga", hover!.Contents.MarkupContent!.Value);
    }

    // ── file-scoped fakes ─────────────────────────────────────────────────────

    private sealed class StubSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags =
            new(StringComparer.OrdinalIgnoreCase);

        public void SetTag(string name, XmlTagDefinition tag) => _tags[name] = tag;

        public XmlTagDefinition? GetTag(string name) => _tags.GetValueOrDefault(name);
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public GameObjectTypeDefinition? GetObjectType(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
        public EnumDefinition? GetEnum(string _) => null;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public event EventHandler? SchemaRefreshed { add { } remove { } }
    }
}
