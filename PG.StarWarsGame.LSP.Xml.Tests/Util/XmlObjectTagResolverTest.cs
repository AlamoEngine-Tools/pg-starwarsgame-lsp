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
        DummyNode = doc.DocumentNode.FirstChild
                    ?? throw new InvalidOperationException("The dummy document did not parse to a node.");
    }

    [Fact]
    public void Resolve_InnermostContext_WinsOverParentContext()
    {
        var schema = TwoTypeSchemaProvider.Build();
        var parentContext = new TagResolutionContext("GameObjectType", 1, DummyNode);
        var innerContext = new TagResolutionContext("LuckyShotAttackAbility", 3, DummyNode, parentContext);

        var result = new XmlObjectTagResolver().Resolve(schema, "Text_ID", innerContext);

        Assert.NotNull(result);
        Assert.Equal(XmlValueType.NameReferenceList, result.ValueType);
    }

    [Fact]
    public void Resolve_ContextTypeName_WinsOverGlobal()
    {
        var schema = TwoTypeSchemaProvider.Build();
        var context = new TagResolutionContext("SFXEvent", 1, DummyNode);

        var result = new XmlObjectTagResolver().Resolve(schema, "Text_ID", context);

        Assert.NotNull(result);
        Assert.Equal(XmlValueType.NameReferenceList, result.ValueType);
    }

    [Fact]
    public void Resolve_NullContext_ReturnsFlatGlobal()
    {
        var schema = TwoTypeSchemaProvider.Build();

        var result = new XmlObjectTagResolver().Resolve(schema, "Text_ID", null);

        Assert.NotNull(result);
        Assert.Equal(XmlValueType.NameReference, result.ValueType);
    }
}

file static class TwoTypeSchemaProvider
{
    private static readonly XmlTagDefinition GlobalTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition SfxTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReferenceList };

    private static readonly XmlTagDefinition GameObjectTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition AbilityTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReferenceList };

    public static FakeSchemaProvider Build()
    {
        return new SchemaProviderBuilder()
            .WithTag(GlobalTextId)
            .WithTags("SFXEvent", SfxTextId)
            .WithTags("GameObjectType", GameObjectTextId)
            .WithTags("LuckyShotAttackAbility", AbilityTextId)
            .Build();
    }
}