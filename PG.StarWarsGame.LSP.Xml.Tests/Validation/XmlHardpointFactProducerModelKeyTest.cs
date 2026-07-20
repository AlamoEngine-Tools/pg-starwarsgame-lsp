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

    [Fact]
    public void HardpointFileOpen_ParentBoneAbsentFromMountingObjectModel_FlaggedOnTheHardpoint()
    {
        // Direction 1: the hardpoint file is open. The parent-model bone lives on the object that
        // mounts the hardpoint, reached through the workspace reference index (WorkspaceReferences +
        // the mounting object's indexed document). A Damage_Decal bone absent from that object's model
        // must be flagged here, on the hardpoint itself - not only from the mounting-object side.
        const string objUri = "file:///object.xml";
        const string hpText =
            """<X><HardPoint Name="HP_A"><Attachment_Bone>HP_Bone</Attachment_Bone>""" +
            """<Damage_Decal>HP_Blast</Damage_Decal></HardPoint></X>""";

        var schema = new ModelSchema("Land_Model_Name");
        var source = new HardpointTagSource()
            .With("PALACE", new VariantTag("Land_Model_Name", "UB_PALACE.ALO", "", 0),
                new VariantTag("HardPoints", "HP_A", "", 0))
            .With("HP_A", new VariantTag("Attachment_Bone", "HP_Bone", "", 0),
                new VariantTag("Damage_Decal", "HP_Blast", "", 0));

        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("ub_palace.alo", ["HP_Bone", "Root"]); // HP_Blast is absent

        var hpSym = Sym("HP_A", "HardPoint");
        var objSym = new GameSymbol("PALACE", GameSymbolKind.XmlObject, "SpecialStructure",
            new FileOrigin(objUri, 0, 0), null, null);

        var hpDoc = new DocumentIndex(Uri, 1, [hpSym], ImmutableArray<GameReference>.Empty);
        var objDoc = new DocumentIndex(objUri, 1, [objSym], ImmutableArray<GameReference>.Empty);

        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Uri, hpDoc).Add(objUri, objDoc),
            WorkspaceDefinitions = new[] { hpSym, objSym }
                .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
                    StringComparer.OrdinalIgnoreCase),
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("HP_A", [new GameReference("HP_A", GameSymbolKind.XmlObject, "HardPoint", objUri, 0, 0, 0)]),
            ModelBones = bones
        };

        var facts = new XmlHardpointFactProducer(schema, source)
            .Produce(Uri, ParsedXmlDocument.Parse(hpText), index);

        // Documents the observed behaviour: is the parent bone flagged on the hardpoint file (Uri)?
        Assert.Contains(facts, f => f is HardpointBoneNotOnModelFact { BoneName: "HP_Blast" });
    }

    [Fact]
    public void HardpointFileOpen_MultipleBadParentBones_AllFlagged()
    {
        // Collision_Mesh and Damage_Decal are identical in schema and code (both parent boneName tags).
        // If one flags on the tag but the other does not, the producer would be dropping it. This pins
        // down whether the producer emits a fact for EVERY bad parent bone in the same hardpoint.
        const string objUri = "file:///object.xml";
        const string hpText =
            """<X><HardPoint Name="HP_A"><Attachment_Bone>Good_Bone</Attachment_Bone>""" +
            """<Collision_Mesh>Bad_Coll</Collision_Mesh><Damage_Decal>Bad_Blast</Damage_Decal>""" +
            """<Damage_Particles>Good_Emit</Damage_Particles></HardPoint></X>""";

        var schema = new ModelSchema("Land_Model_Name");
        var source = new HardpointTagSource()
            .With("PALACE", new VariantTag("Land_Model_Name", "UB_PALACE.ALO", "", 0),
                new VariantTag("HardPoints", "HP_A", "", 0));

        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("ub_palace.alo", ["Good_Bone", "Good_Emit", "Root"]); // Bad_Coll and Bad_Blast absent

        var hpSym = Sym("HP_A", "HardPoint");
        var objSym = new GameSymbol("PALACE", GameSymbolKind.XmlObject, "SpecialStructure",
            new FileOrigin(objUri, 0, 0), null, null);
        var hpDoc = new DocumentIndex(Uri, 1, [hpSym], ImmutableArray<GameReference>.Empty);
        var objDoc = new DocumentIndex(objUri, 1, [objSym], ImmutableArray<GameReference>.Empty);

        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(Uri, hpDoc).Add(objUri, objDoc),
            WorkspaceDefinitions = new[] { hpSym, objSym }
                .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase),
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("HP_A", [new GameReference("HP_A", GameSymbolKind.XmlObject, "HardPoint", objUri, 0, 0, 0)]),
            ModelBones = bones
        };

        var facts = new XmlHardpointFactProducer(schema, source)
            .Produce(Uri, ParsedXmlDocument.Parse(hpText), index);

        var flagged = facts.OfType<HardpointBoneNotOnModelFact>().Select(f => f.BoneName).OrderBy(x => x).ToList();
        Assert.Equal(["Bad_Blast", "Bad_Coll"], flagged);
    }

    [Fact]
    public void BoneOnTacticalModel_ButAbsentFromGalacticModel_NotFlagged()
    {
        // A starbase carries both Space_Model_Name (tactical, has the hardpoint bone) and
        // Galactic_Model_Name (low-detail galactic map, no hardpoint bones). The attachment bone must
        // be validated only against the tactical model - checking it against the galactic model is a
        // false positive (the HP01_COM_BONE-on-i_ub_0X_station.alo case).
        const string text =
            """<X><SpaceUnit Name="STATION"><Space_Model_Name>UB_01_STATION.ALO</Space_Model_Name>""" +
            """<Galactic_Model_Name>i_ub_01_station.alo</Galactic_Model_Name>""" +
            """<HardPoints>HP_A</HardPoints></SpaceUnit></X>""";

        var schema = new ModelSchema("Space_Model_Name", "Galactic_Model_Name");
        var source = new HardpointTagSource()
            .With("STATION", new VariantTag("Space_Model_Name", "UB_01_STATION.ALO", "", 0),
                new VariantTag("Galactic_Model_Name", "i_ub_01_station.alo", "", 0))
            .With("HP_A", new VariantTag("Attachment_Bone", "HP01_COM_BONE", "", 0));

        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("ub_01_station.alo", ["HP01_COM_Bone", "Root"]) // tactical model HAS the bone (mixed case)
            .Add("i_ub_01_station.alo", ["Root"]); // galactic model lacks it

        var facts = Produce(text, schema, source, bones, Sym("STATION", "SpaceUnit"));

        Assert.DoesNotContain(facts, f => f is HardpointBoneNotOnModelFact);
    }

    [Fact]
    public void HardpointFileOpen_Fact_AnchorsOnBoneNameValue_NotTheTag()
    {
        const string objUri = "file:///object.xml";
        const string hpText =
            "<X><HardPoint Name=\"HP_A\"><Damage_Decal>HP_Blast</Damage_Decal></HardPoint></X>";

        var schema = new ModelSchema("Land_Model_Name");
        var source = new HardpointTagSource()
            .With("PALACE", new VariantTag("Land_Model_Name", "UB_PALACE.ALO", "", 0),
                new VariantTag("HardPoints", "HP_A", "", 0));
        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("ub_palace.alo", ["Root"]);

        var hpSym = Sym("HP_A", "HardPoint");
        var objSym = new GameSymbol("PALACE", GameSymbolKind.XmlObject, "SpecialStructure",
            new FileOrigin(objUri, 0, 0), null, null);
        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Uri, new DocumentIndex(Uri, 1, [hpSym], ImmutableArray<GameReference>.Empty))
                .Add(objUri, new DocumentIndex(objUri, 1, [objSym], ImmutableArray<GameReference>.Empty)),
            WorkspaceDefinitions = new[] { hpSym, objSym }
                .ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase),
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("HP_A", [new GameReference("HP_A", GameSymbolKind.XmlObject, "HardPoint", objUri, 0, 0, 0)]),
            ModelBones = bones
        };

        var fact = Assert.Single(new XmlHardpointFactProducer(schema, source)
            .Produce(Uri, ParsedXmlDocument.Parse(hpText), index).OfType<HardpointBoneNotOnModelFact>());

        Assert.Equal(hpText.IndexOf("HP_Blast", StringComparison.Ordinal), fact.Column);
        Assert.Equal("HP_Blast".Length, fact.Length);
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

    private sealed class ModelSchema(params string[] modelTags) : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string tagName)
        {
            return modelTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)
                ? new XmlTagDefinition
                {
                    Tag = tagName, ValueType = XmlValueType.NameReference,
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
