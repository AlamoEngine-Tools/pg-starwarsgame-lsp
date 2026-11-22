// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     A type without the ION_STUN_EFFECT behaviour against the ion stun effects its model draws.
/// </summary>
/// <remarks>
///     Measured in the game: the ION_STUN_EFFECT behaviour hides every effect whose name starts with
///     <c>pi</c> when the object is created, shows them for a stun and hides them at its end. Nothing
///     else writes that group, so a type without the behaviour draws them as its model file has them.
/// </remarks>
public sealed class XmlIonStunEffectFactProducerTest
{
    private const string Uri = "file:///units.xml";

    private const string WrittenText =
        """<X><SpaceUnit Name="UNIT"><Space_Model_Name>unit.alo</Space_Model_Name><SpaceBehavior>SELECTABLE</SpaceBehavior></SpaceUnit></X>""";

    private static ModelPart Effect(string name, bool fileVisible = true)
    {
        return new ModelPart(name, true, fileVisible, false);
    }

    [Fact]
    public void ATypeWithoutTheBehaviour_WhoseModelShowsAnIonEffect_IsFlagged()
    {
        var fact = Assert.Single(Produce("SELECTABLE", [Effect("pi_sparks")]));

        Assert.Equal("UNIT", fact.ObjectId);
        Assert.Equal("unit.alo", fact.ModelName);
        Assert.Equal(["pi_sparks"], fact.Effects);
    }

    [Fact]
    public void ATypeWithTheBehaviour_IsSilent()
    {
        Assert.Empty(Produce("SELECTABLE, ION_STUN_EFFECT", [Effect("pi_sparks")]));
    }

    /// <summary>The behaviour counts from any of the three lists, and without case.</summary>
    [Theory]
    [InlineData("Behavior")]
    [InlineData("SpaceBehavior")]
    [InlineData("LandBehavior")]
    public void TheBehaviourInAnyOfTheThreeLists_IsSilent(string tag)
    {
        Assert.Empty(Produce("ion_stun_effect", [Effect("pi_sparks")], tag));
    }

    [Fact]
    public void AnIonEffectTheFileHides_IsSilent()
    {
        Assert.Empty(Produce("SELECTABLE", [Effect("pi_sparks", false)]));
    }

    /// <summary>The game compares the start of the name over two characters, case ignored.</summary>
    [Fact]
    public void TheGroupIsTheStartOfTheName_CaseIgnored()
    {
        var fact = Assert.Single(Produce("SELECTABLE",
        [
            Effect("PI_Sparks"), Effect("pe_glow"), Effect("p_ion_cannon"), Effect("spi_x"), Effect("pix")
        ]));

        Assert.Equal(["PI_Sparks", "pix"], fact.Effects);
    }

    [Fact]
    public void AMeshNamedLikeTheGroup_IsNotAnEffect()
    {
        Assert.Empty(Produce("SELECTABLE", [new ModelPart("pi_hull", false, true, true)]));
    }

    [Fact]
    public void EachEffectNameIsListedOnce_InFileOrder()
    {
        var fact = Assert.Single(Produce("SELECTABLE",
            [Effect("pi_b"), Effect("pi_a"), Effect("PI_B"), Effect("pi_c", false)]));

        Assert.Equal(["pi_b", "pi_a"], fact.Effects);
    }

    [Fact]
    public void ATypeWithoutAnyBehaviourList_IsFlagged()
    {
        Assert.Single(Produce(null, [Effect("pi_sparks")]));
    }

    [Fact]
    public void AModelWhosePartsAreUnknown_IsSilent()
    {
        Assert.Empty(Produce("SELECTABLE", null));
    }

    [Fact]
    public void ATypeWithoutAModel_IsSilent()
    {
        Assert.Empty(Produce("SELECTABLE", [Effect("pi_sparks")], model: null));
    }

    [Fact]
    public void WithNothingCatalogued_IsSilent()
    {
        Assert.Empty(Produce("SELECTABLE", [Effect("pi_sparks")], catalogued: false));
    }

    /// <summary>The list the behaviour is missing from is what this document can change.</summary>
    [Fact]
    public void TheBehaviourListWrittenInThisDocument_IsTheAnchor()
    {
        var fact = Assert.Single(Produce("SELECTABLE", [Effect("pi_sparks")], written: true));

        Assert.Equal("SpaceBehavior", WrittenText.Substring(fact.Column, fact.Length));
    }

    [Fact]
    public void WithNoBehaviourListInThisDocument_TheObjectIsTheAnchor()
    {
        var fact = Assert.Single(Produce("SELECTABLE", [Effect("pi_sparks")]));

        Assert.Equal("SpaceUnit", Text.Substring(fact.Column, fact.Length));
    }

    private const string Text = """<X><SpaceUnit Name="UNIT"></SpaceUnit></X>""";

    private static List<IonEffectsNeverHiddenFact> Produce(
        string? behaviours,
        IEnumerable<ModelPart>? parts,
        string behaviourTag = "SpaceBehavior",
        string? model = "unit.alo",
        bool catalogued = true,
        bool written = false)
    {
        var unit = new GameSymbol("UNIT", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin(Uri, 0, 0), null);

        var tags = new List<VariantTag>();
        if (model is not null)
            tags.Add(new VariantTag("Space_Model_Name", model, "", 0));
        if (behaviours is not null)
            tags.Add(new VariantTag(behaviourTag, behaviours, "", 0));

        var source = new TagSource().With("UNIT", [.. tags]);

        // Never empty when catalogued: an empty catalogue is the producer's "nothing was indexed" guard.
        var bones = catalogued
            ? ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("other.alo", ["Root"])
                .Add("unit.alo", ["Root"])
            : ImmutableDictionary<string, ImmutableArray<string>>.Empty;

        var partMap = ImmutableDictionary<string, ImmutableArray<ModelPart>>.Empty;
        if (parts is not null)
            partMap = partMap.Add("unit.alo", [.. parts]);

        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Uri, new DocumentIndex(Uri, 1, [unit], ImmutableArray<GameReference>.Empty)),
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT", [unit]),
            ModelBones = bones,
            ModelParts = partMap
        };

        return
        [
            .. new XmlIonStunEffectFactProducer(ModelSchema(), source)
                .Produce(Uri, ParsedXmlDocument.Parse(written ? WrittenText : Text), index)
                .OfType<IonEffectsNeverHiddenFact>()
        ];
    }

    /// <summary>A schema in which the model tag, and only it, names a model file.</summary>
    private static FakeSchemaProvider ModelSchema()
    {
        return new SchemaProviderBuilder()
            .WithTag(new XmlTagDefinition
            {
                Tag = "Space_Model_Name", ValueType = XmlValueType.NameReference,
                ReferenceKind = ReferenceKind.ModelFile
            })
            .Build();
    }

    private sealed class TagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public TagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }
}