// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Assembling a unit from its XML: hull, mounted hardpoints, damage wiring and faction colours.
/// </summary>
/// <remarks>
///     Modelled on <c>Generic_Star_Destroyer</c>, which is the reference case for the whole feature -
///     10 hardpoints, 8 of them with a <c>Model_To_Attach</c>, each naming an <c>HP_*_EmitDamage</c>
///     bone that the hull's damage proxies hang off.
/// </remarks>
public sealed class PreviewSceneBuilderTest
{
    [Fact]
    public void BuildForObject_CarriesTheSubjectsAffiliation()
    {
        // Which faction's fallback colour applies when no team tint does. 772 shipped objects
        // declare one; nothing read it.
        var index = Index([Sym("Xwing", "SpaceUnit")]);
        var tags = new FakeVariantTagSource().With("Xwing",
            Tag("Space_Model_Name", "hull.alo"), Tag("Affiliation", "Rebel"));

        Assert.Equal("Rebel", Builder(index, tags, "hull.alo").BuildForObject("Xwing").Affiliation);
    }

    [Fact]
    public void BuildForObject_TakesTheFirstOfSeveralAffiliations()
    {
        // 29 of the 775 name more than one - `Neutral, Rebel, Empire` on the capturable structures.
        // The first is the primary; a unit cannot wear two fallback colours at once.
        var index = Index([Sym("Pad", "GroundStructure")]);
        var tags = new FakeVariantTagSource().With("Pad",
            Tag("Space_Model_Name", "hull.alo"), Tag("Affiliation", " Neutral, Rebel, Empire "));

        Assert.Equal("Neutral", Builder(index, tags, "hull.alo").BuildForObject("Pad").Affiliation);
    }

    [Fact]
    public void BuildForObject_LeavesTheAffiliationNullWhenTheObjectDeclaresNone()
    {
        var index = Index([Sym("Ship", "SpaceUnit")]);
        var tags = new FakeVariantTagSource().With("Ship", Tag("Space_Model_Name", "hull.alo"));

        Assert.Null(Builder(index, tags, "hull.alo").BuildForObject("Ship").Affiliation);
    }

    [Fact]
    public void BuildForObject_CarriesTheSubjectsOwnNoColorizationColour()
    {
        // The colour a unit wears when NO faction colour applies - which the user's word is the
        // usual case, since faction colour is a skirmish thing. 25 shipped objects declare one and
        // it is object-specific: a TIE is 75,75,75 whoever owns it, an indigenous Bantha is
        // 128,101,79, and ten of the 25 write pure white, which is the identity for the
        // `Colorization` multiply - "leave my texture alone".
        var index = Index([Sym("TIE_Fighter", "SpaceUnit")]);
        var tags = new FakeVariantTagSource().With("TIE_Fighter",
            Tag("Space_Model_Name", "hull.alo"),
            Tag("No_Colorization_Color", "75, 75, 75, 255"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("TIE_Fighter");

        Assert.Equal(new PreviewRgba(75, 75, 75, 255), scene.NoColorizationColor);
    }

    [Fact]
    public void BuildForObject_LeavesTheNoColorizationColourNullWhenTheObjectDeclaresNone()
    {
        // Absent is NOT white: the client falls back on its own, and inventing a colour here would
        // stop it telling "declared untinted" from "said nothing".
        var index = Index([Sym("Ship", "SpaceUnit")]);
        var tags = new FakeVariantTagSource().With("Ship", Tag("Space_Model_Name", "hull.alo"));

        Assert.Null(Builder(index, tags, "hull.alo").BuildForObject("Ship").NoColorizationColor);
    }

    private static GameSymbol Sym(string id, string typeName, string? variantBaseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, variantBaseId);
    }

    private static GameSymbol SymAt(string id, string typeName, string file, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{file}", line, 0), null);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static PreviewSceneBuilder Builder(
        GameIndex index, FakeVariantTagSource tags, params string[] resolvableAssets)
    {
        return Builder(index, tags, new FakeAssets(resolvableAssets));
    }

    private static PreviewSceneBuilder Builder(
        GameIndex index, FakeVariantTagSource tags, FakeAssets assets)
    {
        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, assets);
    }

    private static GameIndex Index(
        IEnumerable<GameSymbol> symbols,
        IReadOnlyDictionary<string, string[]>? modelBones = null)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        if (modelBones is null)
            return index;

        return index with
        {
            ModelBones = modelBones.ToImmutableDictionary(
                kv => ModelBoneKey.From(kv.Key), kv => kv.Value.ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>A destroyer with one turret hardpoint, wired the way the shipped data wires them.</summary>
    private static (GameIndex Index, FakeVariantTagSource Tags) Destroyer()
    {
        var index = Index(
            [Sym("Generic_Star_Destroyer", "SpaceUnit"), Sym("HP_SD_Weapon_FL", "HardPoint")],
            new Dictionary<string, string[]>
            {
                ["EV_StarDestroyer.ALO"] = ["HP_F-L_Bone", "HP_F-L_EmitDamage", "HP_F-L_Blast"]
            });

        var tags = new FakeVariantTagSource()
            .With("Generic_Star_Destroyer",
                Tag("Space_Model_Name", "EV_StarDestroyer.ALO"),
                Tag("HardPoints", "HP_SD_Weapon_FL"))
            .With("HP_SD_Weapon_FL",
                Tag("Type", "HARD_POINT_WEAPON_LASER"),
                Tag("Is_Destroyable", "Yes"),
                Tag("Health", "325.0"),
                Tag("Model_To_Attach", "EV_StarDestroyer_HP00_L-F.alo"),
                Tag("Attachment_Bone", "HP_F-L_Bone"),
                Tag("Damage_Particles", "HP_F-L_EmitDamage"),
                Tag("Damage_Decal", "HP_F-L_Blast"),
                Tag("Death_Explosion_Particles", "Large_Explosion_Space_Empire"),
                Tag("Fire_Bone_A", "FP_F-L_00"),
                Tag("Fire_Bone_B", "FP_F-L_01"),
                Tag("Fire_Cone_Width", "160"),
                Tag("Fire_Cone_Height", "130.0"),
                Tag("Fire_Range_Distance", "2000.0"));

        return (index, tags);
    }

    // ── assembly ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildForObject_MountsTheHardpointModelOnItsAttachmentBone()
    {
        var (index, tags) = Destroyer();
        var scene = Builder(index, tags,
            "EV_StarDestroyer.ALO", "EV_StarDestroyer_HP00_L-F.alo").BuildForObject("Generic_Star_Destroyer");

        var hull = Assert.Single(scene.Parts, p => p.Origin == PreviewPartOrigin.Hull);
        var turret = Assert.Single(scene.Parts, p => p.Origin == PreviewPartOrigin.Hardpoint);

        Assert.Equal("EV_StarDestroyer.ALO", hull.ModelRef);
        Assert.Null(hull.AttachToPartId);

        Assert.Equal("EV_StarDestroyer_HP00_L-F.alo", turret.ModelRef);
        Assert.Equal("hull", turret.AttachToPartId);
        Assert.Equal("HP_F-L_Bone", turret.AttachBone);
        Assert.Equal("HP_SD_Weapon_FL", turret.HardpointId);
        Assert.True(turret.Resolved);
    }

    [Fact]
    public void BuildForObject_InheritsHardpointsThroughAVariant()
    {
        // A Variant_Of_Existing_Type unit mounts whatever its base mounts. Reading the raw node
        // instead of the effective object would assemble every variant as a bare hull.
        var (baseIndex, tags) = Destroyer();
        var index = Index(
            baseIndex.WorkspaceDefinitions.SelectMany(kv => kv.Value)
                .Append(Sym("Star_Destroyer_Elite", "SpaceUnit", "Generic_Star_Destroyer")),
            new Dictionary<string, string[]>
            {
                ["EV_StarDestroyer.ALO"] = ["HP_F-L_Bone", "HP_F-L_EmitDamage", "HP_F-L_Blast"]
            });

        var scene = Builder(index, tags, "EV_StarDestroyer.ALO", "EV_StarDestroyer_HP00_L-F.alo")
            .BuildForObject("Star_Destroyer_Elite");

        Assert.Single(scene.Hardpoints);
        Assert.Contains(scene.Parts, p => p.Origin == PreviewPartOrigin.Hardpoint);
    }

    [Fact]
    public void BuildForObject_KeepsAHardpointThatAttachesNoModel()
    {
        // Two of the Star Destroyer's ten hardpoints attach nothing. They still occupy a bone and are
        // still destroyable, so they must appear - just without a part.
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Shield", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Shield"))
            .With("HP_Shield", Tag("Attachment_Bone", "HP_SHIELD"), Tag("Is_Destroyable", "Yes"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        Assert.Single(scene.Hardpoints);
        Assert.Null(scene.Hardpoints[0].PartId);
        Assert.DoesNotContain(scene.Parts, p => p.Origin == PreviewPartOrigin.Hardpoint);
    }

    // ── damage wiring ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildForObject_CarriesTheDamageWiringADestroyedHardpointNeeds()
    {
        // Every field here drives one step of the destroy chain: hide the model, show the proxies
        // under the Damage_Particles bone, show the decal, play the explosion once.
        var (index, tags) = Destroyer();
        var scene = Builder(index, tags, "EV_StarDestroyer.ALO", "EV_StarDestroyer_HP00_L-F.alo")
            .BuildForObject("Generic_Star_Destroyer");

        var hardpoint = Assert.Single(scene.Hardpoints);

        Assert.True(hardpoint.IsDestroyable);
        Assert.Equal(325f, hardpoint.Health);
        Assert.Equal("HP_F-L_EmitDamage", hardpoint.DamageParticlesBone);
        Assert.Equal("HP_F-L_Blast", hardpoint.DamageDecalBone);
        Assert.Equal("Large_Explosion_Space_Empire", hardpoint.DeathExplosionParticles);
        Assert.Equal("hp:HP_SD_Weapon_FL", hardpoint.PartId);
    }

    [Fact]
    public void BuildForObject_ReportsAHardpointAsNotDestroyableWhenItSaysSo()
    {
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Fixed", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Fixed"))
            .With("HP_Fixed", Tag("Is_Destroyable", "No"));

        Assert.False(Builder(index, tags, "hull.alo").BuildForObject("Ship").Hardpoints[0].IsDestroyable);
    }

    // ── turrets and arcs ──────────────────────────────────────────────────────

    [Fact]
    public void BuildForObject_ReadsTheFiringArcButLeavesAnUnspecifiedConeNull()
    {
        // Null, not zero: a hardpoint that declares no cone and one with a zero-degree cone are
        // different, and drawing the second for the first would be a confident lie.
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Gun", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Gun"))
            .With("HP_Gun", Tag("Fire_Bone_A", "FP_00"));

        var weapon = Assert.Single(Builder(index, tags, "hull.alo").BuildForObject("Ship").Weapons);

        Assert.Equal(["FP_00"], weapon.FireBones);
        Assert.Null(weapon.ConeWidthDegrees);
        Assert.Null(weapon.Range);
    }

    // ── particle systems ──────────────────────────────────────────────────────

    [Fact]
    public void BuildForModel_RecognisesAParticleSystem()
    {
        // Both formats ship as .alo, so the only way to know which the user opened is to look. Getting
        // this wrong hands a particle system to the model reader and reports a format error for a file
        // that is perfectly well-formed.
        var assets = new FakeAssets("p_smoke.alo").WithRootChunk("p_smoke.alo", 0x900);

        var scene = Builder(GameIndex.Empty, new FakeVariantTagSource(), assets)
            .BuildForModel("p_smoke.alo");

        Assert.Equal(PreviewSceneKind.Particle, scene.Kind);
        Assert.Empty(scene.Problems);
    }

    [Fact]
    public void BuildForModel_TreatsAModelAsAModel()
    {
        var assets = new FakeAssets("hull.alo").WithRootChunk("hull.alo", 0x200);

        Assert.Equal(PreviewSceneKind.Model,
            Builder(GameIndex.Empty, new FakeVariantTagSource(), assets).BuildForModel("hull.alo").Kind);
    }

    [Fact]
    public void BuildForModel_ReportsAFileThatIsNeither()
    {
        // Says what is wrong rather than letting the model reader fail obscurely further downstream.
        var assets = new FakeAssets("junk.alo").WithRootChunk("junk.alo", 0xDEAD);

        var scene = Builder(GameIndex.Empty, new FakeVariantTagSource(), assets)
            .BuildForModel("junk.alo");

        Assert.Equal(PreviewSceneKind.Model, scene.Kind);
        Assert.Contains(scene.Problems, p => p.Severity == "error" && p.Message.Contains("not an"));
    }

    [Fact]
    public void BuildForModel_ListsTheAnimationsThatBelongToTheModel()
    {
        // Previewing a MODEL offered no clips at all: nothing could enumerate the animations, so the
        // picker was empty by construction while opening the .ala directly played fine. They sit
        // beside their model under a name prefix, which is the same rule the reverse lookup uses.
        var index = Index([], new Dictionary<string, string[]> { ["ei_bobafett.alo"] = ["Root"] })
            with
            {
                AssetFiles = MergedAssetFileIndex.Merge([], [
                    "data/art/models/ei_bobafett_fly_00.ala",
                    "data/art/models/ei_bobafett_land_00.ala",
                    // Another unit's, and one whose stem merely starts the same way without the
                    // separator - neither belongs to this model.
                    "data/art/models/ev_stardestroyer_idle.ala",
                    "data/art/models/ei_bobafettish_wave.ala"
                ])
            };

        var scene = Builder(index, new FakeVariantTagSource(), "ei_bobafett.alo")
            .BuildForModel("ei_bobafett.alo");

        Assert.Equal(
            ["ei_bobafett_fly_00.ala", "ei_bobafett_land_00.ala"],
            scene.Animations.Order());
    }

    [Fact]
    public void BuildForModel_ListsNoAnimationsWhenTheCatalogueHasNone()
    {
        var index = Index([], new Dictionary<string, string[]> { ["lonely.alo"] = ["Root"] });

        Assert.Empty(Builder(index, new FakeVariantTagSource(), "lonely.alo")
            .BuildForModel("lonely.alo").Animations);
    }

    // ── animation overrides ───────────────────────────────────────────────────

    [Fact]
    public void BuildForObject_ReportsTheModelAnimationsAreBorrowedFrom()
    {
        // Land_Model_Anim_Override_Name hands a unit another model's animation set. The clips are then
        // named after the OVERRIDE model, so a preview looking under the hull's name finds none at all
        // while the game plays a full set.
        var index = Index(
            [Sym("Trooper", "GroundUnit")],
            new Dictionary<string, string[]>
            {
                ["ei_trooper_body.alo"] = ["Root", "Spine"],
                ["EI_TROOPER.ALO"] = ["Root", "Spine"]
            });

        var tags = new FakeVariantTagSource().With("Trooper",
            Tag("Land_Model_Name", "ei_trooper_body.alo"),
            Tag("Land_Model_Anim_Override_Name", "EI_TROOPER.ALO"));

        var scene = Builder(index, tags, "ei_trooper_body.alo").BuildForObject("Trooper");

        Assert.Equal("EI_TROOPER.ALO", scene.AnimationSource);
        Assert.DoesNotContain(scene.Problems, p => p.Message.Contains("skeleton"));
    }

    [Fact]
    public void BuildForObject_HonoursTheSpaceOverrideToo()
    {
        // Unused by any shipped object, but it is in the schema and in the engine's own parser, so a
        // mod may well use it.
        var index = Index([Sym("Fighter", "SpaceUnit")]);
        var tags = new FakeVariantTagSource().With("Fighter",
            Tag("Space_Model_Name", "hull.alo"),
            Tag("Space_Model_Anim_Override_Name", "other.alo"));

        Assert.Equal("other.alo",
            Builder(index, tags, "hull.alo").BuildForObject("Fighter").AnimationSource);
    }

    [Fact]
    public void BuildForObject_WarnsWhenTheOverrideSkeletonDoesNotMatch()
    {
        // The engine can only swap animation sets because the skeletons are identical - true of every
        // one of the 20 shipped land overrides. A mismatch plays the wrong bones, and nothing else in
        // the toolchain checks it.
        var index = Index(
            [Sym("Trooper", "GroundUnit")],
            new Dictionary<string, string[]>
            {
                ["ei_trooper_body.alo"] = ["Root", "Spine"],
                ["ni_ewok.alo"] = ["Root", "Spine", "Tail"]
            });

        var tags = new FakeVariantTagSource().With("Trooper",
            Tag("Land_Model_Name", "ei_trooper_body.alo"),
            Tag("Land_Model_Anim_Override_Name", "ni_ewok.alo"));

        var scene = Builder(index, tags, "ei_trooper_body.alo").BuildForObject("Trooper");

        Assert.Equal("ni_ewok.alo", scene.AnimationSource);
        Assert.Contains(scene.Problems,
            p => p.Severity == "warning" && p.Message.Contains("skeleton"));
    }

    [Fact]
    public void BuildForObject_LeavesAnimationSourceNullWithoutAnOverride()
    {
        var index = Index([Sym("Ship", "SpaceUnit")]);
        var tags = new FakeVariantTagSource().With("Ship", Tag("Space_Model_Name", "hull.alo"));

        Assert.Null(Builder(index, tags, "hull.alo").BuildForObject("Ship").AnimationSource);
    }

    [Fact]
    public void BuildForObject_OmitsTheArcEntirelyWhenNoFireBoneIsNamed()
    {
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Hull", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Hull"))
            .With("HP_Hull", Tag("Attachment_Bone", "HP_00"));

        Assert.Empty(Builder(index, tags, "hull.alo").BuildForObject("Ship").Weapons);
    }

    [Fact]
    public void BuildForObject_ReadsTurretPoseOnlyForATurret()
    {
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_T", "HardPoint"), Sym("HP_F", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_T, HP_F"))
            .With("HP_T",
                Tag("Is_Turret", "Yes"),
                Tag("Turret_Rest_Angle", "45"),
                Tag("Turret_Rotate_Extent_Degrees", "180"),
                Tag("Turret_Bone_Name", "TURRET_01"))
            .With("HP_F", Tag("Attachment_Bone", "HP_00"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        var turret = scene.Hardpoints.Single(h => h.Id == "HP_T").Turret;
        Assert.NotNull(turret);
        Assert.Equal(45f, turret.RestAngle);
        Assert.Equal(180f, turret.RotateExtentDegrees);
        Assert.Equal("TURRET_01", turret.TurretBone);

        Assert.Null(scene.Hardpoints.Single(h => h.Id == "HP_F").Turret);
    }

    // ── problems ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildForObject_StillEmitsAPartForAHardpointModelThatIsMissing()
    {
        // Dropping it would hide exactly the authoring mistake this preview exists to surface.
        var (index, tags) = Destroyer();
        var scene = Builder(index, tags, "EV_StarDestroyer.ALO").BuildForObject("Generic_Star_Destroyer");

        var turret = Assert.Single(scene.Parts, p => p.Origin == PreviewPartOrigin.Hardpoint);
        Assert.False(turret.Resolved);
        Assert.Contains(scene.Problems,
            p => p.HardpointId == "HP_SD_Weapon_FL" && p.Message.Contains("was not found"));
    }

    [Fact]
    public void BuildForObject_FlagsAnAttachmentBoneTheHullDoesNotHave()
    {
        var index = Index(
            [Sym("Ship", "SpaceUnit"), Sym("HP_Bad", "HardPoint")],
            new Dictionary<string, string[]> { ["hull.alo"] = ["ROOT"] });
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Bad"))
            .With("HP_Bad", Tag("Attachment_Bone", "NOT_A_BONE"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        Assert.Contains(scene.Problems, p => p.Message.Contains("NOT_A_BONE"));
    }

    [Fact]
    public void BuildForObject_FlagsAMountedHardpointThatIsNotDefined()
    {
        var index = Index([Sym("Ship", "SpaceUnit")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Ghost"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        Assert.Empty(scene.Hardpoints);
        Assert.Contains(scene.Problems, p => p.HardpointId == "HP_Ghost");
    }

    [Fact]
    public void BuildForObject_ForAnUnknownObject_SaysSoRatherThanReturningAnEmptyScene()
    {
        var scene = Builder(Index([]), new FakeVariantTagSource()).BuildForObject("Nope");

        Assert.Empty(scene.Parts);
        Assert.Contains(scene.Problems, p => p.Severity == "error" && p.Message.Contains("Nope"));
    }

    [Fact]
    public void BuildForObject_WithNoGameDirectoryConfigured_ExplainsWhyNothingResolved()
    {
        // The difference between "your model is missing" and "you never told the extension where the
        // game is" is the whole value of the tier report.
        var (index, tags) = Destroyer();
        var scene = Builder(index, tags).BuildForObject("Generic_Star_Destroyer");

        Assert.Contains(scene.Problems, p => p.Message.Contains("baseGameDirectory"));
    }

    // ── factions ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildForObject_CollectsEveryFactionsColours()
    {
        // All of them, not just the object's own: the preview compares a unit in two factions' colours
        // side by side, and shipping the set makes that a local switch.
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("Rebel", "Faction"), Sym("Empire", "Faction")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"))
            .With("Rebel",
                Tag("Color", "185, 40, 39, 255"),
                Tag("No_Colorization_Color", "199, 105, 59, 255"))
            .With("Empire", Tag("Color", "54, 134, 242, 255"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        var rebel = Assert.Single(scene.Factions, f => f.Name == "Rebel");
        Assert.Equal(new PreviewRgba(185, 40, 39, 255), rebel.Color);
        Assert.Equal(new PreviewRgba(199, 105, 59, 255), rebel.NoColorizationColor);

        var empire = Assert.Single(scene.Factions, f => f.Name == "Empire");
        Assert.Equal(new PreviewRgba(54, 134, 242, 255), empire.Color);
        Assert.Null(empire.NoColorizationColor);
    }

    [Fact]
    public void BuildForObject_ListsFactionsInTheOrderTheXmlDeclaresThem()
    {
        // The palette is a row of swatches, and a row that comes back in a different order on every
        // reload cannot be learned - the reader reaches for the position they used last time and
        // gets someone else's colour. The order came from a dictionary, which has none to give.
        //
        // Ids chosen so alphabetical order is NOT declaration order: sorting by name would pass a
        // weaker version of this test and still be wrong about the file.
        var index = Index([
            Sym("Ship", "SpaceUnit"),
            SymAt("Zann", "Faction", "factions.xml", 10),
            SymAt("Empire", "Faction", "factions.xml", 30),
            SymAt("Rebel", "Faction", "factions.xml", 20)
        ]);

        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"))
            .With("Zann", Tag("Color", "1, 1, 1, 255"))
            .With("Empire", Tag("Color", "2, 2, 2, 255"))
            .With("Rebel", Tag("Color", "3, 3, 3, 255"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        Assert.Equal(["Zann", "Rebel", "Empire"], scene.Factions.Select(f => f.Name));
    }

    [Fact]
    public void BuildForObject_OrdersFactionsAcrossFilesWithoutMixingThemUp()
    {
        // Two files, and each one's own declaration order kept inside it. Whichever file comes
        // first, the answer has to be the SAME every time - that is the whole point.
        var index = Index([
            Sym("Ship", "SpaceUnit"),
            SymAt("Second", "Faction", "b_more.xml", 5),
            SymAt("Third", "Faction", "b_more.xml", 40),
            SymAt("First", "Faction", "a_core.xml", 90)
        ]);

        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"))
            .With("First", Tag("Color", "1, 1, 1, 255"))
            .With("Second", Tag("Color", "2, 2, 2, 255"))
            .With("Third", Tag("Color", "3, 3, 3, 255"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        Assert.Equal(["First", "Second", "Third"], scene.Factions.Select(f => f.Name));
    }

    // ── single files ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildForModel_ProducesAOnePartSceneWithTheSameShape()
    {
        // A bare .alo is a scene with one part, so the client has one renderer rather than two paths.
        var scene = Builder(Index([]), new FakeVariantTagSource(), "Ev_stardestroyer.alo")
            .BuildForModel("Ev_stardestroyer.alo");

        Assert.Equal(PreviewSceneKind.Model, scene.Kind);
        var part = Assert.Single(scene.Parts);
        Assert.Equal(PreviewPartOrigin.Hull, part.Origin);
        Assert.True(part.Resolved);
        Assert.Empty(scene.Problems);
    }
}
