// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The fighter case: a <c>WEAPON</c> behaviour on the unit itself.
/// </summary>
/// <remarks>
///     188 objects across the two trees are armed this way against 68 with hardpoints, and until now
///     the preview showed none of them an armament. Nothing in the XML says where the shot leaves
///     from: the engine cycles the hull's <c>MuzzleA_#</c> bones, 0..X, one per volley.
/// </remarks>
public sealed class PreviewUnitWeaponTest
{
    [Fact]
    public void BuildForObject_ReadsTheWeaponBehaviourTags()
    {
        var weapon = Assert.Single(Fighter().Weapons);

        Assert.Equal(PreviewWeaponSource.Unit, weapon.Source);
        Assert.Equal("bank:A", weapon.Id);
        Assert.Null(weapon.HardpointId);

        Assert.Equal(450f, weapon.Range);
        Assert.Equal("Proj_Ship_Small_Laser_Cannon_Red", weapon.ProjectileType);
        Assert.Equal(5f, weapon.Damage);
        Assert.Equal(3, weapon.PulseCount);
        Assert.Equal(0.1f, weapon.PulseDelaySeconds!.Value, 3);
        Assert.Equal(2f, weapon.RechargeSeconds);
        Assert.Equal(20f, weapon.ConeWidthDegrees);
        Assert.Equal(40f, weapon.ConeHeightDegrees);
    }

    [Fact]
    public void BuildForObject_DiscoversTheMuzzleABankFromTheHullInNumberOrder()
    {
        var weapon = Assert.Single(Fighter().Weapons);

        // Ordered by the number, not by the order the file happens to store them in.
        Assert.Equal(["MuzzleA_00", "MuzzleA_01", "MuzzleA_02"], weapon.FireBones);
    }

    [Fact]
    public void BuildForObject_TakesOnlyTheABank()
    {
        // MuzzleB is a naming convention rather than something the engine consumes for this case,
        // and MuzzleC is a single bone on one model in the whole corpus.
        var weapon = Assert.Single(Fighter().Weapons);

        Assert.DoesNotContain(weapon.FireBones, b => b.StartsWith("MuzzleB", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(weapon.FireBones, b => b.StartsWith("MuzzleC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildForObject_ExcludesFlashGeometryFromTheBank()
    {
        // MuzzleA_00_flash is the flash MESH beside the fire point, driven by the clip's own
        // visibility tracks. Firing from it would double every shot.
        var weapon = Assert.Single(Fighter().Weapons);

        Assert.DoesNotContain(weapon.FireBones, b => b.Contains("flash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildForObject_DoesNotRepeatABoneAlreadyNamedAsTurretOrBarrel()
    {
        // T2B_Tank names MuzzleA_00 as its Barrel_Bone_Name. The two conventions overlap and the
        // same point must not be reported twice.
        var scene = Build(
            hull: "RV_T2BTank.alo",
            bones: ["Turret_00", "MuzzleA_00", "MuzzleA_01"],
            unitTags:
            [
                ("SpaceBehavior", "WEAPON"),
                ("Targeting_Max_Attack_Distance", "200"),
                ("Turret_Bone_Name", "Turret_00"),
                ("Barrel_Bone_Name", "MuzzleA_00"),
                ("Turret_Rotate_Extent_Degrees", "360"),
                ("Turret_Elevate_Extent_Degrees", "40")
            ]);

        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(["MuzzleA_01"], weapon.FireBones);
        Assert.NotNull(weapon.Turret);
        Assert.Equal("Turret_00", weapon.Turret!.TurretBone);
        Assert.Equal("MuzzleA_00", weapon.Turret.BarrelBone);
    }

    [Fact]
    public void BuildForObject_AlwaysCyclesForAUnitWeapon()
    {
        // Randomize_Between_Fire_Bones is a HardPoint parameter; it is not on GameObjectType at all,
        // so this case has no other branch.
        Assert.Equal(PreviewFirePointMode.CycleBones, Assert.Single(Fighter().Weapons).FirePointMode);
    }

    [Fact]
    public void BuildForObject_CarriesFiresForward()
    {
        var scene = Build(
            hull: "rv_XWing.ALO",
            bones: ["MuzzleA_00"],
            unitTags:
            [
                ("SpaceBehavior", "WEAPON"),
                ("Targeting_Max_Attack_Distance", "450"),
                ("Fires_Forward", "Yes")
            ]);

        Assert.True(Assert.Single(scene.Weapons).FiresForward);
    }

    [Fact]
    public void BuildForObject_ReportsAWeaponBehaviourUnitWhoseHullHasNoMuzzleBones()
    {
        // A real authoring gap: the unit is armed but the artist shipped no fire points, so the
        // engine has nowhere to put the bolt.
        var scene = Build(
            hull: "bare.alo",
            bones: ["Root"],
            unitTags: [("SpaceBehavior", "WEAPON"), ("Targeting_Max_Attack_Distance", "450")]);

        Assert.Empty(scene.Weapons);
        Assert.Contains(scene.Problems, p =>
            p.Message.Contains("MuzzleA", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildForObject_IgnoresAUnitWithNoWeaponBehaviour()
    {
        var scene = Build(
            hull: "transport.alo",
            bones: ["MuzzleA_00"],
            unitTags: [("SpaceBehavior", "DUMMY_STARSHIP, SELECTABLE")]);

        Assert.Empty(scene.Weapons);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Fighter()
    {
        return Build(
            hull: "rv_XWing.ALO",
            bones:
            [
                "MuzzleA_02", "MuzzleA_00", "MuzzleA_01", "MuzzleA_00_flash", "MuzzleB_00",
                "MuzzleC_00", "Root"
            ],
            unitTags:
            [
                ("SpaceBehavior", "DUMMY_STARSHIP, WEAPON, SELECTABLE"),
                ("Targeting_Max_Attack_Distance", "450"),
                ("Projectile_Types", "Proj_Ship_Small_Laser_Cannon_Red"),
                ("Damage", "5.0"),
                ("Projectile_Fire_Pulse_Count", "3"),
                ("Projectile_Fire_Pulse_Delay_Seconds", "0.1"),
                ("Projectile_Fire_Recharge_Seconds", "2.0"),
                ("Turret_Rotate_Extent_Degrees", "20"),
                ("Turret_Elevate_Extent_Degrees", "40")
            ]);
    }

    private static PreviewScene Build(
        string hull, string[] bones, (string Name, string Value)[] unitTags)
    {
        var index = Index([Sym("Unit", "SpaceUnit")], new Dictionary<string, string[]> { [hull] = bones });

        var tags = new FakeVariantTagSource().With("Unit",
            unitTags.Select(t => Tag(t.Name, t.Value))
                .Prepend(Tag("Space_Model_Name", hull))
                .ToArray());

        return new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
                tags, new FakeAssets(hull))
            .BuildForObject("Unit");
    }

    private static GameSymbol Sym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin($"file:///{id}.xml", 0, 0), null, null);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private static GameIndex Index(
        IEnumerable<GameSymbol> symbols, IReadOnlyDictionary<string, string[]> modelBones)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase),
            ModelBones = modelBones.ToImmutableDictionary(
                kv => ModelBoneKey.From(kv.Key),
                kv => kv.Value.ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
