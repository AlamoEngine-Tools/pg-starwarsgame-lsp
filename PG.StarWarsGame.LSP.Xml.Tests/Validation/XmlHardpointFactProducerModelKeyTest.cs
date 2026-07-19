// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     Regression guard for the model-bone key mismatch that produced a false
///     "hardpoint model bones unavailable" diagnostic. The bone catalog is keyed by bare filename
///     (<see cref="ModelBoneKey" />); the producer must reduce the XML model reference to that same
///     key before looking it up, otherwise every hardpoint on the object reads as bones-unavailable
///     and the per-(model, owner) aggregation surfaces it on just the first hardpoint.
/// </summary>
public sealed class XmlHardpointFactProducerModelKeyTest
{
    private const string Uri = "file:///structures.xml";

    [Fact]
    public void MountingObject_ModelReferencedWithPath_ResolvesBonesAndEmitsNoUnavailableFact()
    {
        // The model tag carries a path-qualified value; the catalog is keyed "ub_palace.alo". Only a
        // filename-normalised lookup connects the two - a raw lookup misses and reports unavailable.
        const string text =
            """<X><SpecialStructure Name="PALACE"><Land_Model_Name>Models\UB_PALACE.ALO</Land_Model_Name>""" +
            """<HardPoints>HP_A, HP_B</HardPoints></SpecialStructure></X>""";

        var schema = new ModelSchema("Land_Model_Name");
        var source = new HardpointTagSource()
            .With("PALACE", new VariantTag("Land_Model_Name", @"Models\UB_PALACE.ALO", "", 0))
            .With("HP_A", new VariantTag("Attachment_Bone", "HP_Bone_A", "", 0))
            .With("HP_B", new VariantTag("Attachment_Bone", "HP_Bone_B", "", 0));

        // Catalog keyed by bare filename, as the fixed builders now produce it.
        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("ub_palace.alo", ["HP_Bone_A", "HP_Bone_B", "Root"]);

        var facts = Produce(text, schema, source, bones, Sym("PALACE", "SpecialStructure"));

        Assert.DoesNotContain(facts, f => f is HardpointModelBonesUnavailableFact);
        Assert.DoesNotContain(facts, f => f is HardpointBoneNotOnModelFact);
    }

    [Fact]
    public void MountingObject_BoneGenuinelyAbsent_StillFlagged()
    {
        // Sanity: the fix must not blanket-suppress real problems - a bone the model lacks is flagged.
        const string text =
            """<X><SpecialStructure Name="PALACE"><Land_Model_Name>UB_PALACE.ALO</Land_Model_Name>""" +
            """<HardPoints>HP_A</HardPoints></SpecialStructure></X>""";

        var schema = new ModelSchema("Land_Model_Name");
        var source = new HardpointTagSource()
            .With("PALACE", new VariantTag("Land_Model_Name", "UB_PALACE.ALO", "", 0))
            .With("HP_A", new VariantTag("Attachment_Bone", "Nonexistent_Bone", "", 0));

        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("ub_palace.alo", ["HP_Bone_A", "Root"]);

        var facts = Produce(text, schema, source, bones, Sym("PALACE", "SpecialStructure"));

        Assert.Contains(facts, f => f is HardpointBoneNotOnModelFact { BoneName: "Nonexistent_Bone" });
    }

    private static IReadOnlyList<XmlFact> Produce(string text, ISchemaProvider schema,
        IVariantTagSource source, ImmutableDictionary<string, ImmutableArray<string>> bones,
        params GameSymbol[] symbols)
    {
        var docSymbols = symbols.ToImmutableArray();
        var docIndex = new DocumentIndex(Uri, 1, docSymbols, ImmutableArray<GameReference>.Empty);
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(Uri, docIndex),
            WorkspaceDefinitions = defs,
            ModelBones = bones
        };

        return new XmlHardpointFactProducer(schema, source)
            .Produce(Uri, ParsedXmlDocument.Parse(text), index);
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(Uri, 0, 0), null, null);
    }

    private sealed class HardpointTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId) => _byId.GetValueOrDefault(objectId);

        public HardpointTagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }

    private sealed class ModelSchema(string modelTag) : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string tagName)
        {
            return string.Equals(tagName, modelTag, StringComparison.OrdinalIgnoreCase)
                ? new XmlTagDefinition
                {
                    Tag = modelTag, ValueType = XmlValueType.NameReference,
                    ReferenceKind = ReferenceKind.ModelFile
                }
                : null;
        }

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
