// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Preview.Sections;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview.Sections;

/// <summary>
///     The weapon section on its own (#314): the unit's own weapon off its muzzle bones, a
///     hardpoint's weapon off its fire bones, and the turret either carries.
/// </summary>
public sealed class WeaponSectionBuilderTest
{
    private static (GameIndex Index, EffectiveObject Unit, FakeVariantTagSource Tags) Setup(
        (string Tag, string Value)[] tags, params string[] hullBones)
    {
        var tagSource = new FakeVariantTagSource().With("Unit",
            tags.Select(t => new VariantTag(t.Tag, t.Value, $"<{t.Tag}>{t.Value}</{t.Tag}>", 0)).ToArray());
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Unit",
            [
                new GameSymbol("Unit", GameSymbolKind.XmlObject, "SpaceUnit",
                    new FileOrigin("file:///unit.xml", 0, 0), null, null)
            ]),
            ModelBones = hullBones.Length == 0
                ? GameIndex.Empty.ModelBones
                : GameIndex.Empty.ModelBones.Add(ModelBoneKey.From("hull.alo"), [.. hullBones])
        };
        var unit = new EffectiveObjectResolver(index, new SchemaProviderBuilder().Build(), tagSource).Resolve("Unit");
        return (index, unit, tagSource);
    }

    private static WeaponSectionBuilder Builder(FakeVariantTagSource tags)
    {
        return new WeaponSectionBuilder(tags, TestCatalogues.English);
    }

    [Fact]
    public void UnitWeapon_IsNull_WithoutAWeaponBehaviour()
    {
        var (index, unit, tags) = Setup([("SpaceBehavior", "SELECTABLE")], "MuzzleA_00");
        var problems = new List<PreviewProblem>();

        Assert.Null(Builder(tags).UnitWeapon(index, unit, "hull.alo", problems));
        Assert.Empty(problems);
    }

    [Fact]
    public void UnitWeapon_TakesTheMuzzleABank_InNumberOrder_WithoutFlashesOrTurretBones()
    {
        var (index, unit, tags) = Setup(
            [("SpaceBehavior", "SELECTABLE, WEAPON"), ("Projectile_Types", "Proj_Red, Proj_Blue"), ("Damage", "5")],
            "MuzzleA_02", "MuzzleA_00_flash", "MuzzleB_00", "MuzzleA_00", "MuzzleA_01");
        var problems = new List<PreviewProblem>();

        var weapon = Builder(tags).UnitWeapon(index, unit, "hull.alo", problems);

        Assert.NotNull(weapon);
        Assert.Equal("weapon:A", weapon.Id);
        Assert.Equal(PreviewWeaponSource.Unit, weapon.Source);
        Assert.Equal(["MuzzleA_00", "MuzzleA_01", "MuzzleA_02"], weapon.FireBones);
        Assert.Equal("Proj_Red", weapon.ProjectileType);
        Assert.Equal(5f, weapon.Damage);
        // Unwritten extents are the engine's 360 and 180 half-angles: a full turn either way.
        Assert.Equal(360f, weapon.ConeWidthDegrees);
        Assert.Equal(360f, weapon.ConeHeightDegrees);
        Assert.Empty(problems);
    }

    [Fact]
    public void UnitWeapon_WithNoMuzzleBones_IsNull_AndNoticed()
    {
        var (index, unit, tags) = Setup([("SpaceBehavior", "WEAPON")], "ROOT");
        var problems = new List<PreviewProblem>();

        Assert.Null(Builder(tags).UnitWeapon(index, unit, "hull.alo", problems));
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticIds.PreviewNoMuzzleBones, problem.DiagnosticId);
    }

    [Fact]
    public void UnitWeapon_FiringForward_HasNoArc()
    {
        var (index, unit, tags) = Setup([("SpaceBehavior", "WEAPON"), ("Fires_Forward", "Yes")], "MuzzleA_00");

        var weapon = Builder(tags).UnitWeapon(index, unit, "hull.alo", []);

        Assert.NotNull(weapon);
        Assert.True(weapon.FiresForward);
        Assert.Null(weapon.ConeWidthDegrees);
        Assert.Null(weapon.ConeHeightDegrees);
    }

    [Fact]
    public void Weapon_ReadsAHardpointsFireBones_AndItsFirePointMode()
    {
        var (index, hardpoint, tags) = Setup([
            ("Type", "HARD_POINT_WEAPON_LASER"), ("Fire_Bone_A", "FP_00"), ("Fire_Bone_B", "FP_01"),
            ("Randomize_Between_Fire_Bones", "Yes"), ("Fire_Projectile_Type", "Proj_Red"),
            ("Fire_Range_Distance", "1000")
        ]);

        var weapon = Builder(tags).Weapon(index, hardpoint, "HP_Laser");

        Assert.NotNull(weapon);
        Assert.Equal("hardpoint:HP_Laser", weapon.Id);
        Assert.Equal(PreviewWeaponSource.Hardpoint, weapon.Source);
        Assert.Equal(["FP_00", "FP_01"], weapon.FireBones);
        Assert.Equal(PreviewFirePointMode.RandomAlongLine, weapon.FirePointMode);
        Assert.Equal("Proj_Red", weapon.ProjectileType);
        Assert.Equal(1000f, weapon.Range);
        Assert.Null(weapon.Turret);
    }

    [Fact]
    public void Weapon_IsNull_ForAHardpointWithoutFireBones()
    {
        var (index, hardpoint, tags) = Setup([("Type", "HARD_POINT_ENGINE")]);

        Assert.Null(Builder(tags).Weapon(index, hardpoint, "HP_Engine"));
    }

    [Fact]
    public void Turret_ReadsTheHardpointsBones_WithTheHardpointDefaults()
    {
        var (_, hardpoint, _) = Setup([
            ("Is_Turret", "Yes"), ("Turret_Bone_Name", "TURRET_00"), ("Barrel_Bone_Name", "BARREL_00")
        ]);

        var turret = PreviewFireArcs.Turret(hardpoint);

        Assert.NotNull(turret);
        Assert.Equal("TURRET_00", turret.TurretBone);
        Assert.Equal("BARREL_00", turret.BarrelBone);
        Assert.Null(PreviewFireArcs.Turret(Setup([("Turret_Bone_Name", "TURRET_00")]).Unit));
    }
}