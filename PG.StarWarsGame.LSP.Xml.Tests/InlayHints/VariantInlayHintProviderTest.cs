// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.InlayHints;

public sealed class VariantInlayHintProviderTest
{
    private const string Text =
        """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Mass>5</Mass></SpaceUnit></X>""";

    private static GameSymbol Sym(string id, string? baseId, string uri)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(uri, 0, 0), null, baseId);
    }

    private static InlayHintContext Ctx(HtmlNode node, XmlTagDefinition tagDef, GameIndex index, ISchemaProvider schema)
    {
        var hapDoc = XmlUtility.CreateHtmlDocument(Text);
        return new InlayHintContext("file:///u.xml", index, schema, hapDoc, node, tagDef, XmlUtility.GetLine(node));
    }

    [Fact]
    public void Handle_VariantTag_EmitsSummaryHint()
    {
        var hapDoc = XmlUtility.CreateHtmlDocument(Text);
        var variantNode = hapDoc.DocumentNode.Descendants()
            .First(n => n.Name.Equals("variant_of_existing_type", StringComparison.OrdinalIgnoreCase));

        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type");
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[] { Sym("V", "B", "file:///u.xml"), Sym("B", null, "file:///b.xml") }
                .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
        var source = new FakeTagSource()
            .With("V", new VariantTag("Mass", "5", "<Mass>5</Mass>", 0),
                new VariantTag("Variant_Of_Existing_Type", "B", "", 0))
            .With("B", new VariantTag("Max_Health", "100", "<Max_Health>100</Max_Health>", 0));

        var hints = new VariantInlayHintProvider(source)
            .Handle(Ctx(variantNode, schema.GetTag("Variant_Of_Existing_Type")!, index, schema)).ToList();

        var hint = Assert.Single(hints);
        var label = hint.Label.String ?? string.Empty;
        Assert.Contains("inherits 1", label);
        Assert.Contains("adds 1", label);
    }

    [Fact]
    public void Handle_NonVariantTag_NoHint()
    {
        var hapDoc = XmlUtility.CreateHtmlDocument(Text);
        var massNode = hapDoc.DocumentNode.Descendants()
            .First(n => n.Name.Equals("mass", StringComparison.OrdinalIgnoreCase));
        var plain = new XmlTagDefinition { Tag = "Mass", ValueType = XmlValueType.Float };

        var hints = new VariantInlayHintProvider(new FakeTagSource())
            .Handle(Ctx(massNode, plain, GameIndex.Empty, new FakeSchema()));

        Assert.Empty(hints);
    }

    private sealed class FakeTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeTagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId) => _byId.GetValueOrDefault(objectId);
    }

    private sealed class FakeSchema : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);

        public FakeSchema Variant(string tag)
        {
            _tags[tag] = new XmlTagDefinition
            {
                Tag = tag, ValueType = XmlValueType.TypeReference,
                ReferenceKind = ReferenceKind.XmlObject, SemanticType = TagSemanticType.VariantParent
            };
            return this;
        }

        public XmlTagDefinition? GetTag(string tagName) => _tags.GetValueOrDefault(tagName);
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName) => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public GameObjectTypeDefinition? GetObjectType(string typeName) => null;
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName) => [];
        public EnumDefinition? GetEnum(string enumName) => null;
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}
