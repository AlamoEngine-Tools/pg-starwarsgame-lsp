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
///     <c>Land_Damage_Alternates</c> against the model that has to draw those stages.
/// </summary>
/// <remarks>
///     <para>
///         One direction only, which is the whole design. A model may tag more stages than the XML
///         uses and that is never reported - it is an asset carrying more than this object asks of
///         it, the same shape as a <c>PTE_</c> effect on a unit with no TURBO, or a stealth shell on
///         a unit that cannot cloak. The reverse is a unit that reaches a damage state and does not
///         change, which nothing else in the toolchain would ever tell the author.
///     </para>
///     <para>
///         Measured across foc: 219 objects declare the table, 212 have every stage tagged, one names
///         no model, and 6 declare a stage the model has nothing for. A first pass at that count said
///         222 and 9 because it matched the raw text and several objects keep a damage table inside an
///         XML COMMENT - which the parser, correctly, does not see.
///     </para>
/// </remarks>
public sealed class XmlDamageStageFactProducerTest
{
    private const string Uri = "file:///structures.xml";

    [Fact]
    public void DeclaredStageTheModelTagsNothingFor_IsFlagged()
    {
        // `Gas_Collection_Droid` in miniature: the table names three stages and the model stages none.
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates></GroundStructure></X>""",
            model: ["Root", "hull"]);

        var fact = Assert.Single(facts.OfType<DamageStageNotOnModelFact>());

        Assert.Equal("BUNKER", fact.ObjectId);
        Assert.Equal("bunker.alo", fact.ModelName);
        Assert.Equal([1, 2], fact.MissingStages);
        Assert.Empty(fact.TaggedStages);
    }

    [Fact]
    public void EveryDeclaredStageTagged_IsSilent()
    {
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates></GroundStructure></X>""",
            model: ["Root", "hull_ALT1", "p_smoke_ALT2"]);

        Assert.Empty(facts.OfType<DamageStageNotOnModelFact>());
    }

    // The user's rule, stated: the model supporting more than the XML uses is FINE.
    [Fact]
    public void ModelTaggingMoreThanIsDeclared_IsSilent()
    {
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """<Land_Damage_Alternates>0, 1</Land_Damage_Alternates></GroundStructure></X>""",
            model: ["Root", "hull_ALT1", "hull_ALT2", "hull_ALT3"]);

        Assert.Empty(facts.OfType<DamageStageNotOnModelFact>());
    }

    // Stage 0 is the undamaged state a model opens in - every untagged mesh IS it. Demanding an
    // `_ALT0` would fire on almost every object that declares the table at all.
    [Fact]
    public void StageZero_IsNeverAskedFor()
    {
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """<Land_Damage_Alternates>0</Land_Damage_Alternates></GroundStructure></X>""",
            model: ["Root"]);

        Assert.Empty(facts.OfType<DamageStageNotOnModelFact>());
    }

    // The catalogue holds bones UNION mesh names and no proxy names. Measured across the 1957 shipped
    // models, that costs nothing: of the 149 carrying an ALT-tagged proxy, not one tags a level no
    // bone or mesh also tags - because a proxy rides a bone named after it.
    [Fact]
    public void AStageTaggedOnlyByAProxyBone_Counts()
    {
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates></GroundStructure></X>""",
            model: ["Root", "p_smoke_small_thin_ALT1", "p_electricalstatic_ALT2"]);

        Assert.Empty(facts.OfType<DamageStageNotOnModelFact>());
    }

    [Fact]
    public void UncataloguedModel_IsSilentRatherThanWrong()
    {
        // Undecidable: with no entry the model's tags are unknown, and the warning would be about the
        // index rather than about the file.
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates></GroundStructure></X>""",
            model: null, otherModel: ["Root"]);

        Assert.Empty(facts.OfType<DamageStageNotOnModelFact>());
    }

    [Fact]
    public void ObjectWithNoModel_IsSilent()
    {
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER">"""
            + """<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates></GroundStructure></X>""",
            model: ["Root"], declaresModel: false);

        Assert.Empty(facts.OfType<DamageStageNotOnModelFact>());
    }

    [Fact]
    public void NoAlternatesDeclared_IsSilent()
    {
        // Every space unit. Nothing to check against, and nothing to say.
        var facts = Produce(
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """</GroundStructure></X>""",
            model: ["Root"]);

        Assert.Empty(facts.OfType<DamageStageNotOnModelFact>());
    }

    [Fact]
    public void Fact_AnchorsOnTheMissingStage_NotTheWholeList()
    {
        // The list is POSITIONAL and sits beside Land_Damage_Thresholds and Land_Damage_SFX, so
        // underlining all of it says the table is wrong when one entry is.
        const string text =
            """<X><GroundStructure Name="BUNKER"><Land_Model_Name>bunker.alo</Land_Model_Name>"""
            + """<Land_Damage_Alternates>0, 1, 2</Land_Damage_Alternates></GroundStructure></X>""";

        var fact = Assert.Single(
            Produce(text, model: ["Root", "hull_ALT1"]).OfType<DamageStageNotOnModelFact>());

        Assert.Equal(1, fact.Length);

        var lines = text.Split('\n');
        Assert.Equal("2", lines[fact.Line].Substring(fact.Column, fact.Length));
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<XmlFact> Produce(
        string text,
        IEnumerable<string>? model,
        bool declaresModel = true,
        IEnumerable<string>? otherModel = null)
    {
        var symbol = new GameSymbol("BUNKER", GameSymbolKind.XmlObject, "GroundStructure",
            new FileOrigin(Uri, 0, 0), null, null);

        var tags = new List<VariantTag>();
        if (declaresModel)
            tags.Add(new VariantTag("Land_Model_Name", "bunker.alo", "", 0));

        var source = new StageTagSource().With("BUNKER", [.. tags]);

        // Never empty: an empty catalogue is the producer's "nothing was indexed" guard, and a test
        // for an UNCATALOGUED model has to get past it to mean anything.
        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("other.alo", [.. otherModel ?? ["Root"]]);

        if (model is not null)
            bones = bones.Add("bunker.alo", [.. model]);

        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Uri, new DocumentIndex(Uri, 1, [symbol], ImmutableArray<GameReference>.Empty)),
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("BUNKER", [symbol]),
            ModelBones = bones
        };

        return new XmlDamageStageFactProducer(new ModelSchema("Land_Model_Name"), source)
            .Produce(Uri, ParsedXmlDocument.Parse(text), index);
    }

    private sealed class StageTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public StageTagSource With(string id, params VariantTag[] tags)
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
