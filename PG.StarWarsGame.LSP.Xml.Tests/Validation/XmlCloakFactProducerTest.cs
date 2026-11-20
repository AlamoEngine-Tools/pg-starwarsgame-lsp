// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     A cloaking unit against the parts of its model the cloak decides by name.
/// </summary>
/// <remarks>
///     Measured in the game: a cloak hides every part whose name contains BASEMESH, shows every part
///     whose name contains STEALTH, and leaves every other part as it was; a stealth also hides the
///     models a hardpoint attaches, a Force cloak does not. On the unit's XML object, never on the
///     model file: a model shared by a cloaking unit and an ordinary one is only wrong for the first.
/// </remarks>
public sealed class XmlCloakFactProducerTest
{
    private const string Uri = "file:///units.xml";

    private const string Stealth =
        "<Abilities><Stealth_Ability Name=\"S\"><Stealth_Color>255, 64, 64, 120</Stealth_Color>"
        + "<Stealth_Transition_Time>.6</Stealth_Transition_Time></Stealth_Ability></Abilities>";

    private const string WhiteStealth =
        "<Abilities><Stealth_Ability Name=\"S\"><Stealth_Color>255, 255, 255, 255</Stealth_Color>"
        + "<Stealth_Transition_Time>.6</Stealth_Transition_Time></Stealth_Ability></Abilities>";

    private const string ForceCloak =
        "<Abilities><Force_Cloak_Ability Name=\"C\"><Force_Cloak_Color>255, 255, 255, 64</Force_Cloak_Color>"
        + "<Force_Cloak_Transition_In_Secs>.4</Force_Cloak_Transition_In_Secs></Force_Cloak_Ability></Abilities>";

    // ── FR-5: nothing hidden ─────────────────────────────────────────────────

    [Fact]
    public void CloakingUnitWhoseModelHasNoBasemesh_IsFlagged()
    {
        var fact = Assert.Single(Produce(Stealth, ["Root", "Body", "Stealth_Shell"]).OfType<CloakHidesNothingFact>());

        Assert.Equal("UNIT", fact.ObjectId);
        Assert.Equal("unit.alo", fact.ModelName);
        Assert.False(fact.LookChangesNothing);
    }

    [Fact]
    public void CloakingUnitWhoseModelHasABasemesh_IsSilentOnIt()
    {
        Assert.Empty(Produce(Stealth, ["Root", "Unit_BaseMesh", "Stealth_Shell"]).OfType<CloakHidesNothingFact>());
    }

    /// <summary>White opaque, or a transition of 0: the cloak's look changes nothing either.</summary>
    [Fact]
    public void ACloakWhoseLookChangesNothing_SaysSo()
    {
        var fact = Assert.Single(Produce(WhiteStealth, ["Root", "Body"]).OfType<CloakHidesNothingFact>());

        Assert.True(fact.LookChangesNothing);
    }

    [Fact]
    public void AUnitThatCannotCloak_IsSilent()
    {
        Assert.Empty(Produce(null, ["Root", "Body"]));
    }

    [Fact]
    public void ATransitionTimeOfZero_ChangesNothing()
    {
        const string instant =
            "<Abilities><Stealth_Ability Name=\"S\"><Stealth_Color>255, 64, 64, 120</Stealth_Color>"
            + "<Stealth_Transition_Time>0</Stealth_Transition_Time></Stealth_Ability></Abilities>";

        Assert.True(Assert.Single(Produce(instant, ["Root"]).OfType<CloakHidesNothingFact>()).LookChangesNothing);
    }

    [Fact]
    public void AnUnwrittenColour_IsWhiteAndChangesNothing()
    {
        const string uncoloured =
            "<Abilities><Stealth_Ability Name=\"S\">"
            + "<Stealth_Transition_Time>.6</Stealth_Transition_Time></Stealth_Ability></Abilities>";

        Assert.True(Assert.Single(Produce(uncoloured, ["Root"]).OfType<CloakHidesNothingFact>()).LookChangesNothing);
    }

    [Fact]
    public void TheCloakWrittenInThisDocument_IsTheAnchor()
    {
        var fact = Assert.Single(Produce(Stealth, ["Root"], written: true).OfType<CloakHidesNothingFact>());

        Assert.Equal("stealth_ability".Length, fact.Length);
        Assert.Equal("Stealth_Ability", WrittenText.Substring(fact.Column, fact.Length));
    }

    // ── FR-6: no shell ───────────────────────────────────────────────────────

    [Fact]
    public void CloakingUnitWhoseModelHasNoStealthPart_IsFlagged()
    {
        var fact = Assert.Single(Produce(Stealth, ["Root", "Unit_BaseMesh"]).OfType<CloakShowsNoShellFact>());

        Assert.Equal("unit.alo", fact.ModelName);
    }

    [Fact]
    public void CloakingUnitWhoseModelHasAStealthPart_IsSilentOnIt()
    {
        Assert.Empty(Produce(Stealth, ["Root", "Unit_BaseMesh"], proxies: ["p_stealth_glow"])
            .OfType<CloakShowsNoShellFact>());
    }

    // ── FR-8: Force cloak and an attached model ──────────────────────────────

    [Fact]
    public void ForceCloakWithAHardpointThatAttachesAModel_IsFlaggedOnThatHardpoint()
    {
        var fact = Assert.Single(
            Produce(ForceCloak, ["Root", "Unit_BaseMesh", "Stealth"], hardpointModel: "turret.alo")
                .OfType<ForceCloakKeepsAttachedModelFact>());

        Assert.Equal("HP_TURRET", fact.HardpointId);
        Assert.Equal("turret.alo", fact.AttachedModel);
    }

    [Fact]
    public void StealthWithAHardpointThatAttachesAModel_IsSilent()
    {
        Assert.Empty(Produce(Stealth, ["Root", "Unit_BaseMesh", "Stealth"], hardpointModel: "turret.alo")
            .OfType<ForceCloakKeepsAttachedModelFact>());
    }

    [Fact]
    public void ForceCloakWithAHardpointThatAttachesNothing_IsSilent()
    {
        Assert.Empty(Produce(ForceCloak, ["Root", "Unit_BaseMesh", "Stealth"], hardpointModel: null)
            .OfType<ForceCloakKeepsAttachedModelFact>());
    }

    [Fact]
    public void AnUncataloguedModel_IsSilent()
    {
        Assert.Empty(Produce(Stealth, null));
    }

    // ── FR-7: parts the cloak leaves as they are ─────────────────────────────

    private static readonly string[] CloakModel = ["Root", "Unit_BaseMesh", "Unit_Stealth"];

    [Fact]
    public void ACloakingUnitWithAVisibleSurfaceItNeitherHidesNorShows_IsFlagged()
    {
        var fact = Assert.Single(Produce(Stealth, CloakModel, parts:
        [
            new ModelPart("Unit_BaseMesh", false, true, true), new ModelPart("Unit_Stealth", false, false, true),
            new ModelPart("Backpack", false, true, true)
        ]).OfType<CloakKeepsPartsFact>());

        Assert.Equal("unit.alo", fact.ModelName);
        Assert.Equal("Stealth_Ability", fact.Ability);
        Assert.Equal(["Backpack"], fact.Parts);
    }

    /// <summary>Effects are kept: a visible proxy the cloak does not name stays on.</summary>
    [Fact]
    public void AVisibleEffectItNeitherHidesNorShows_IsFlagged()
    {
        var fact = Assert.Single(Produce(Stealth, CloakModel, parts:
        [
            new ModelPart("Unit_BaseMesh", false, true, true), new ModelPart("p_engine_glow", true, true, false),
            new ModelPart("p_stealth_shimmer", true, false, false)
        ]).OfType<CloakKeepsPartsFact>());

        Assert.Equal(["p_engine_glow"], fact.Parts);
    }

    /// <summary>A shadow volume is no surface, and a hidden part is not drawn to begin with.</summary>
    [Fact]
    public void OnlyNonSurfaceAndHiddenParts_AreSilent()
    {
        Assert.Empty(Produce(Stealth, CloakModel, parts:
        [
            new ModelPart("Unit_BaseMesh", false, true, true), new ModelPart("Shadow", false, true, false),
            new ModelPart("Panel", false, false, true), new ModelPart("p_off", true, false, false)
        ]).OfType<CloakKeepsPartsFact>());
    }

    [Fact]
    public void EveryVisiblePartNamedForTheCloak_IsSilent()
    {
        Assert.Empty(Produce(Stealth, CloakModel, parts:
        [
            new ModelPart("Unit_BaseMesh", false, true, true), new ModelPart("Unit_Stealth", false, true, true),
            new ModelPart("p_basemesh_glow", true, true, false)
        ]).OfType<CloakKeepsPartsFact>());
    }

    /// <summary>
    ///     A part the game hides when the type loads - BUILDUP or CHARGED in its name, the weapon
    ///     charge effects - is not drawn when the cloak starts, so it is not one that stays drawn.
    /// </summary>
    [Fact]
    public void APartHiddenWhenTheTypeLoads_IsNotListed()
    {
        var fact = Assert.Single(Produce(Stealth, CloakModel, parts:
        [
            new ModelPart("Unit_BaseMesh", false, true, true), new ModelPart("p_weapon_buildup", true, true, false),
            new ModelPart("p_weapon_Charged", true, true, false), new ModelPart("Engine", false, true, true)
        ]).OfType<CloakKeepsPartsFact>());

        Assert.Equal(["Engine"], fact.Parts);
    }

    /// <summary>
    ///     No part data for the model - a baseline built before the field, or a file whose parts could
    ///     not be read - says nothing rather than guess.
    /// </summary>
    [Fact]
    public void AModelWithUnknownParts_IsSilent()
    {
        Assert.Empty(Produce(Stealth, CloakModel).OfType<CloakKeepsPartsFact>());
    }

    [Fact]
    public void AUnitThatCannotCloak_SaysNothingOfItsParts()
    {
        Assert.Empty(Produce(null, CloakModel, parts: [new ModelPart("Backpack", false, true, true)]));
    }

    /// <summary>A name shared by several parts is listed once, in file order.</summary>
    [Fact]
    public void APartNameThatRepeats_IsListedOnce()
    {
        var fact = Assert.Single(Produce(Stealth, CloakModel, parts:
        [
            new ModelPart("Unit_BaseMesh", false, true, true), new ModelPart("Engine", false, true, true),
            new ModelPart("p_glow", true, true, false), new ModelPart("engine", false, true, true)
        ]).OfType<CloakKeepsPartsFact>());

        Assert.Equal(["Engine", "p_glow"], fact.Parts);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private const string WrittenText =
        "<X><GroundInfantry Name=\"UNIT\">" + Stealth + "</GroundInfantry></X>";

    /// <summary>
    ///     A unit `UNIT` on `unit.alo` with the given abilities list, and - when asked - one hardpoint
    ///     `HP_TURRET` attaching `hardpointModel`, and the model's parts when given.
    /// </summary>
    private static IReadOnlyList<XmlFact> Produce(
        string? abilities,
        IEnumerable<string>? model,
        IEnumerable<string>? proxies = null,
        string? hardpointModel = "",
        bool withHardpoint = true,
        bool written = false,
        IEnumerable<ModelPart>? parts = null)
    {
        var unit = new GameSymbol("UNIT", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin(Uri, 0, 0), null);
        var hardpoint = new GameSymbol("HP_TURRET", GameSymbolKind.XmlObject, "HardPoint",
            new FileOrigin(Uri, 0, 0), null);

        var unitTags = new List<VariantTag> { new("Land_Model_Name", "unit.alo", "", 0) };
        if (abilities is not null)
            unitTags.Add(new VariantTag("Abilities", "", abilities, 0));

        var hasHardpoint = withHardpoint && hardpointModel != "";
        if (hasHardpoint)
            unitTags.Add(new VariantTag("HardPoints", "HP_TURRET", "", 0));

        var hardpointTags = new List<VariantTag>();
        if (hardpointModel is not null)
            hardpointTags.Add(new VariantTag("Model_To_Attach", hardpointModel, "", 0));

        var source = new CloakTagSource()
            .With("UNIT", [.. unitTags])
            .With("HP_TURRET", [.. hardpointTags]);

        // Never empty: an empty catalogue is the producer's "nothing was indexed" guard.
        var bones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("other.alo", ["Root"]);
        if (model is not null)
            bones = bones.Add("unit.alo", [.. model]);

        var proxyMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty;
        if (proxies is not null)
            proxyMap = proxyMap.Add("unit.alo", [.. proxies]);

        var partMap = ImmutableDictionary<string, ImmutableArray<ModelPart>>.Empty;
        if (parts is not null)
            partMap = partMap.Add("unit.alo", [.. parts]);

        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Uri, new DocumentIndex(Uri, 1, [unit], ImmutableArray<GameReference>.Empty)),
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT", [unit])
                .Add("HP_TURRET", [hardpoint]),
            ModelBones = bones,
            ModelProxies = proxyMap,
            ModelParts = partMap
        };

        var text = written ? WrittenText : """<X><GroundInfantry Name="UNIT"></GroundInfantry></X>""";

        return new XmlCloakFactProducer(ModelSchema(), source)
            .Produce(Uri, ParsedXmlDocument.Parse(text), index);
    }

    /// <summary>A schema in which the model tag, and only it, names a model file.</summary>
    private static FakeSchemaProvider ModelSchema()
    {
        return new SchemaProviderBuilder()
            .WithTag(new XmlTagDefinition
            {
                Tag = "Land_Model_Name", ValueType = XmlValueType.NameReference,
                ReferenceKind = ReferenceKind.ModelFile
            })
            .Build();
    }

    private sealed class CloakTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public CloakTagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }
}