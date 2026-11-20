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

    // ── fixture ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     A unit `UNIT` on `unit.alo` with the given abilities list, and - when asked - one hardpoint
    ///     `HP_TURRET` attaching `hardpointModel`.
    /// </summary>
    private const string WrittenText =
        "<X><GroundInfantry Name=\"UNIT\">" + Stealth + "</GroundInfantry></X>";

    private static IReadOnlyList<XmlFact> Produce(
        string? abilities,
        IEnumerable<string>? model,
        IEnumerable<string>? proxies = null,
        string? hardpointModel = "",
        bool withHardpoint = true,
        bool written = false)
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

        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Uri, new DocumentIndex(Uri, 1, [unit], ImmutableArray<GameReference>.Empty)),
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT", [unit])
                .Add("HP_TURRET", [hardpoint]),
            ModelBones = bones,
            ModelProxies = proxyMap
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