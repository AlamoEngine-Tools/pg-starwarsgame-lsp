// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class XmlVariantFactProducerTest
{
    private const string Uri = "file:///u.xml";

    private static GameSymbol Sym(string id, string? baseId, string uri = Uri)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(uri, 0, 0), null, baseId);
    }

    private static GameIndex Index(string text, params GameSymbol[] symbols)
    {
        var docSymbols = symbols.Where(s => ((FileOrigin)s.Origin).Uri == Uri).ToImmutableArray();
        var docIndex = new DocumentIndex(Uri, 1, docSymbols, ImmutableArray<GameReference>.Empty);
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(Uri, docIndex),
            WorkspaceDefinitions = defs
        };
    }

    private static IReadOnlyList<XmlFact> Produce(string text, FakeTagSource source, FakeSchema schema,
        params GameSymbol[] symbols)
    {
        return new XmlVariantFactProducer(schema, source).Produce(Uri, text, Index(text, symbols));
    }

    [Fact]
    public void Produce_NoVariants_ReturnsEmpty()
    {
        const string text = """<X><SpaceUnit Name="A"><Max_Health>100</Max_Health></SpaceUnit></X>""";

        var facts = Produce(text, new FakeTagSource(), new FakeSchema(), Sym("A", null));

        Assert.Empty(facts);
    }

    [Fact]
    public void Produce_IgnoredTagOnVariant_EmitsIgnoredOverrideFact()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Locked>x</Locked></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type").Mode("Locked", VariantMode.Ignored);

        var facts = Produce(text, new FakeTagSource(), schema, Sym("V", "B"), Sym("B", null, "file:///b.xml"));

        Assert.Contains(facts, f => f is VariantIgnoredOverrideFact { TagName: "Locked" });
    }

    [Fact]
    public void Produce_RedundantOverride_EmitsHintFact()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Max_Health>100</Max_Health></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type");
        var source =
            new FakeTagSource().With("B", new VariantTag("Max_Health", "100", "<Max_Health>100</Max_Health>", 0));

        var facts = Produce(text, source, schema, Sym("V", "B"), Sym("B", null, "file:///b.xml"));

        Assert.Contains(facts, f => f is VariantRedundantOverrideFact { TagName: "Max_Health" });
    }

    [Fact]
    public void Produce_RedundantOverride_FactCarriesElementEndPosition()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Max_Health>100</Max_Health></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type");
        var source =
            new FakeTagSource().With("B", new VariantTag("Max_Health", "100", "<Max_Health>100</Max_Health>", 0));

        var facts = Produce(text, source, schema, Sym("V", "B"), Sym("B", null, "file:///b.xml"));

        var fact = Assert.Single(facts.OfType<VariantRedundantOverrideFact>());
        Assert.Equal(0, fact.EndLine);
        var expectedEndCol = text.IndexOf("</Max_Health>", StringComparison.Ordinal) + "</Max_Health>".Length;
        Assert.Equal(expectedEndCol, fact.EndColumn);
    }

    [Fact]
    public void Produce_RedundantOverride_MultilineElement_EndLineDiffersFromStartLine()
    {
        const string text =
            "<X>\n<SpaceUnit Name=\"V\">\n<Variant_Of_Existing_Type>B</Variant_Of_Existing_Type>\n" +
            "<Max_Health>\n100\n</Max_Health>\n</SpaceUnit>\n</X>";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type");
        var source =
            new FakeTagSource().With("B",
                new VariantTag("Max_Health", "100", "<Max_Health>\n100\n</Max_Health>", 0));

        var facts = Produce(text, source, schema, Sym("V", "B"), Sym("B", null, "file:///b.xml"));

        var fact = Assert.Single(facts.OfType<VariantRedundantOverrideFact>());
        Assert.Equal(3, fact.Line); // "<Max_Health>" opens on line 3 (0-based)
        Assert.Equal(5, fact.EndLine); // "</Max_Health>" closes on line 5
    }

    [Fact]
    public void Produce_DifferentValueOverride_NoRedundantFact()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type><Max_Health>250</Max_Health></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type");
        var source =
            new FakeTagSource().With("B", new VariantTag("Max_Health", "100", "<Max_Health>100</Max_Health>", 0));

        var facts = Produce(text, source, schema, Sym("V", "B"), Sym("B", null, "file:///b.xml"));

        Assert.DoesNotContain(facts, f => f is VariantRedundantOverrideFact);
    }

    [Fact]
    public void Produce_CyclicChain_EmitsCycleFact()
    {
        const string text =
            """<X><SpaceUnit Name="A"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type></SpaceUnit></X>""";
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type");

        var facts = Produce(text, new FakeTagSource(), schema, Sym("A", "B"), Sym("B", "A", "file:///b.xml"));

        Assert.Contains(facts, f => f is VariantCycleFact { ObjectId: "A" });
    }

    [Fact]
    public void Produce_DoesNotFlagVariantParentTagItself()
    {
        const string text =
            """<X><SpaceUnit Name="V"><Variant_Of_Existing_Type>B</Variant_Of_Existing_Type></SpaceUnit></X>""";
        // Mark the variant tag as Ignored too - it must still be skipped, not flagged.
        var schema = new FakeSchema().Variant("Variant_Of_Existing_Type");
        var source = new FakeTagSource().With("B", new VariantTag("Variant_Of_Existing_Type", "B", "", 0));

        var facts = Produce(text, source, schema, Sym("V", "B"), Sym("B", null, "file:///b.xml"));

        Assert.Empty(facts);
    }

    private sealed class FakeTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public FakeTagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }

    private sealed class FakeSchema : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string tagName)
        {
            return _tags.GetValueOrDefault(tagName);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
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

        public FakeSchema Variant(string tag)
        {
            _tags[tag] = new XmlTagDefinition
            {
                Tag = tag, ValueType = XmlValueType.TypeReference,
                ReferenceKind = ReferenceKind.XmlObject, SemanticType = TagSemanticType.VariantParent
            };
            return this;
        }

        public FakeSchema Mode(string tag, VariantMode mode)
        {
            _tags[tag] = new XmlTagDefinition { Tag = tag, ValueType = XmlValueType.NameReference, VariantMode = mode };
            return this;
        }
    }
}