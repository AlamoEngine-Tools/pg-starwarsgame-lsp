// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     One weapon list, wherever the weapon is declared.
/// </summary>
/// <remarks>
///     The arc used to live on <see cref="PreviewHardpoint" />, which meant a fighter - whose
///     armament sits on the unit itself - could never have one. 188 objects across the two trees are
///     in that position against 68 with hardpoints, so the mounted case was the minority all along.
///     This chunk moves the arc onto <c>PreviewScene.Weapons</c> without adding the unit case yet.
/// </remarks>
public sealed class PreviewWeaponTest
{
    [Fact]
    public void BuildForObject_EmitsTheHardpointsWeaponOnTheSceneNotTheHardpoint()
    {
        var scene = Build();

        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(PreviewWeaponSource.Hardpoint, weapon.Source);
        Assert.Equal("HP_SD_Weapon_FL", weapon.HardpointId);
        Assert.Equal("hardpoint:HP_SD_Weapon_FL", weapon.Id);
        Assert.Equal("HARD_POINT_WEAPON_LASER", weapon.Label);

        Assert.Equal(["FP_F-L_00", "FP_F-L_01"], weapon.FireBones);
        Assert.Equal(160f, weapon.ConeWidthDegrees);
        Assert.Equal(130f, weapon.ConeHeightDegrees);
        Assert.Equal(2000f, weapon.Range);
        Assert.Equal(7, weapon.PulseCount);
        Assert.Equal(0.2f, weapon.PulseDelaySeconds!.Value, 3);
    }

    [Fact]
    public void BuildForObject_LinksTheWeaponBackToItsHardpoint()
    {
        var scene = Build();

        var hardpoint = Assert.Single(scene.Hardpoints);
        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(hardpoint.Id, weapon.HardpointId);
    }

    [Fact]
    public void BuildForObject_ReadsTheFieldsTheOldFireArcDropped()
    {
        var scene = Build();
        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(200f, weapon.MinRange);
        Assert.Equal(45f, weapon.Damage);
        Assert.Equal("Damage_Ship_Laser", weapon.DamageType);
        Assert.Equal("Proj_Ship_Small_Laser_Cannon_Green", weapon.ProjectileType);
        Assert.Equal("SFX_Laser", weapon.FireSfxEvent);
        Assert.Equal(3f, weapon.RechargeSeconds);
    }

    [Fact]
    public void BuildForObject_ReportsTheFireModeGatesThatAreSet()
    {
        var scene = Build();
        var weapon = Assert.Single(scene.Weapons);

        // A mount that cannot fire in the state being previewed should not have its arc drawn as if
        // it could, so the gates travel rather than being collapsed to a boolean here.
        Assert.Contains("Fire_When_Deployed", weapon.FireModes);
        Assert.DoesNotContain("Fire_When_Undeployed", weapon.FireModes);
    }

    [Fact]
    public void BuildForObject_DefaultsToCyclingItsFireBones()
    {
        var weapon = Assert.Single(Build().Weapons);

        // Randomize_Between_Fire_Bones is a HARDPOINT tag and says No on all 8 shipped uses, so
        // cycling is the case every real mount takes.
        Assert.Equal(PreviewFirePointMode.CycleBones, weapon.FirePointMode);
    }

    [Fact]
    public void BuildForObject_HonoursRandomizeBetweenFireBones()
    {
        var scene = Build(("Randomize_Between_Fire_Bones", "Yes"));
        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(PreviewFirePointMode.RandomAlongLine, weapon.FirePointMode);
    }

    [Fact]
    public void BuildForObject_EmitsNoWeaponForAMountThatNamesNoFireBone()
    {
        // A shield generator is a hardpoint with no armament. It still has to appear as a mount.
        var index = Index([Sym("Ship", "SpaceUnit"), Sym("HP_Shield", "HardPoint")]);
        var tags = new FakeVariantTagSource()
            .With("Ship", Tag("Space_Model_Name", "hull.alo"), Tag("HardPoints", "HP_Shield"))
            .With("HP_Shield",
                Tag("Type", "HARD_POINT_SHIELD_GENERATOR"),
                Tag("Attachment_Bone", "HP_SHIELD"));

        var scene = Builder(index, tags, "hull.alo").BuildForObject("Ship");

        Assert.Single(scene.Hardpoints);
        Assert.Empty(scene.Weapons);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Build(params (string Name, string Value)[] extraHardpointTags)
    {
        var index = Index([Sym("Generic_Star_Destroyer", "SpaceUnit"), Sym("HP_SD_Weapon_FL", "HardPoint")]);

        var hardpointTags = new List<VariantTag>
        {
            Tag("Type", "HARD_POINT_WEAPON_LASER"),
            Tag("Attachment_Bone", "HP_F-L_BONE"),
            Tag("Fire_Bone_A", "FP_F-L_00"),
            Tag("Fire_Bone_B", "FP_F-L_01"),
            Tag("Fire_Cone_Width", "160.0"),
            Tag("Fire_Cone_Height", "130.0"),
            Tag("Fire_Range_Distance", "2000.0"),
            Tag("Fire_Min_Range_Distance", "200.0"),
            Tag("Fire_Pulse_Count", "7"),
            Tag("Fire_Pulse_Delay_Seconds", "0.2"),
            Tag("Fire_Min_Recharge_Seconds", "3.0"),
            Tag("Fire_Projectile_Type", "Proj_Ship_Small_Laser_Cannon_Green"),
            Tag("Projectile_Damage", "45.0"),
            Tag("Damage_Type", "Damage_Ship_Laser"),
            Tag("Fire_SFXEvent", "SFX_Laser"),
            Tag("Fire_When_Deployed", "Yes"),
            Tag("Fire_When_Undeployed", "No")
        };

        hardpointTags.AddRange(extraHardpointTags.Select(t => Tag(t.Name, t.Value)));

        var tags = new FakeVariantTagSource()
            .With("Generic_Star_Destroyer",
                Tag("Space_Model_Name", "EV_StarDestroyer.ALO"),
                Tag("HardPoints", "HP_SD_Weapon_FL"))
            .With("HP_SD_Weapon_FL", hardpointTags.ToArray());

        return Builder(index, tags, "EV_StarDestroyer.ALO").BuildForObject("Generic_Star_Destroyer");
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, null);
    }

    [Fact]
    public void BuildForObject_ReadsTheInaccuracyOfEveryTargetCategory()
    {
        // `Fire_Inaccuracy_Distance` is a per-category ROW, repeated - 1017 occurrences in the
        // shipped tree and EVERY ONE of them is `Category, Distance`. It was read as a single
        // number, which cannot parse `Fighter, 30.0`, so it has always come back null.
        var scene = Build(
            ("Fire_Inaccuracy_Distance", "Fighter, 30.0"),
            ("Fire_Inaccuracy_Distance", "Capital, 70.0"),
            ("Fire_Inaccuracy_Distance", " Corvette , 7 "));

        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(["Fighter", "Capital", "Corvette"], weapon.Inaccuracy.Select(i => i.Category));
        Assert.Equal([30f, 70f, 7f], weapon.Inaccuracy.Select(i => i.Distance));
    }

    [Fact]
    public void BuildForObject_DropsAnInaccuracyRowItCannotRead()
    {
        // A half-written row means something different from a row with a blank field, and padding
        // it would invent a category. Same rule `RepeatedTagReader.Rows` applies to the field count.
        var scene = Build(
            ("Fire_Inaccuracy_Distance", "Fighter, 30.0"),
            ("Fire_Inaccuracy_Distance", "Bomber, not a number"),
            ("Fire_Inaccuracy_Distance", "70.0"),
            ("Fire_Inaccuracy_Distance", ", 12"));

        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(["Fighter"], weapon.Inaccuracy.Select(i => i.Category));
    }

    [Fact]
    public void BuildForObject_HasNoInaccuracyWhenTheMountDeclaresNone()
    {
        Assert.Empty(Assert.Single(Build().Weapons).Inaccuracy);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static GameIndex Index(IEnumerable<GameSymbol> symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static PreviewSceneBuilder Builder(
        GameIndex index, FakeVariantTagSource tags, params string[] resolvableAssets)
    {
        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets(resolvableAssets));
    }
}
