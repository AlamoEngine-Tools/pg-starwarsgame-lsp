// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

public sealed class XmlObjectTagResolverTest
{
    private static readonly HtmlNode DummyNode;

    static XmlObjectTagResolverTest()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<n/>");
        DummyNode = doc.DocumentNode.FirstChild;
    }

    [Fact]
    public void Resolve_InnermostContext_WinsOverParentContext()
    {
        var schema = new TwoTypeSchemaProvider();
        var parentContext = new TagResolutionContext("GameObjectType", 1, DummyNode);
        var innerContext = new TagResolutionContext("LuckyShotAttackAbility", 3, DummyNode, parentContext);

        var result = new XmlObjectTagResolver().Resolve(schema, "Text_ID", innerContext);

        Assert.NotNull(result);
        Assert.Equal(XmlValueType.NameReferenceList, result.ValueType);
    }

    [Fact]
    public void Resolve_ContextTypeName_WinsOverGlobal()
    {
        var schema = new TwoTypeSchemaProvider();
        var context = new TagResolutionContext("SFXEvent", 1, DummyNode);

        var result = new XmlObjectTagResolver().Resolve(schema, "Text_ID", context);

        Assert.NotNull(result);
        Assert.Equal(XmlValueType.NameReferenceList, result.ValueType);
    }

    [Fact]
    public void Resolve_NullContext_ReturnsFlatGlobal()
    {
        var schema = new TwoTypeSchemaProvider();

        var result = new XmlObjectTagResolver().Resolve(schema, "Text_ID", null);

        Assert.NotNull(result);
        Assert.Equal(XmlValueType.NameReference, result.ValueType);
    }
}

file sealed class TwoTypeSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition GlobalTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition SfxTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReferenceList };

    private static readonly XmlTagDefinition GameObjectTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition AbilityTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReferenceList };

    public XmlTagDefinition? GetTag(string tagName)
        => tagName.Equals("Text_ID", StringComparison.OrdinalIgnoreCase) ? GlobalTextId : null;

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];

    public GameObjectTypeDefinition? GetObjectType(string _) => null;

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        if (typeName.Equals("SFXEvent", StringComparison.OrdinalIgnoreCase)) return [SfxTextId];
        if (typeName.Equals("GameObjectType", StringComparison.OrdinalIgnoreCase)) return [GameObjectTextId];
        if (typeName.Equals("LuckyShotAttackAbility", StringComparison.OrdinalIgnoreCase)) return [AbilityTextId];
        return [];
    }

    public EnumDefinition? GetEnum(string _) => null;

    public IReadOnlyList<XmlTagDefinition> AllTags => [GlobalTextId];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}
