// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
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
        // `weapon:`, not `bank:`. "Bank" is not the game's word for this and it is not free of
        // meaning either - <Bank_Turn_Angle> is a real tag about how a ship rolls in a turn.
        Assert.Equal("weapon:A", weapon.Id);
        Assert.Null(weapon.HardpointId);

        Assert.Equal(450f, weapon.Range);
        Assert.Equal("Proj_Ship_Small_Laser_Cannon_Red", weapon.ProjectileType);
        Assert.Equal(5f, weapon.Damage);
        Assert.Equal(3, weapon.PulseCount);
        Assert.Equal(0.1f, weapon.PulseDelaySeconds!.Value, 3);
        Assert.Equal(2f, weapon.RechargeSeconds);

        // The two arc angles are deliberately NOT asserted here. They are not a pass-through of the
        // authored tags - the extents are plus-or-minus bounds and the DTO carries a full angle -
        // so they belong with the test that states that conversion and its reason, rather than as
        // two bare numbers in a tag-reading test.
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
            "RV_T2BTank.alo",
            ["Turret_00", "MuzzleA_00", "MuzzleA_01"],
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
            "rv_XWing.ALO",
            ["MuzzleA_00"],
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
            "bare.alo",
            ["Root"],
            [("SpaceBehavior", "WEAPON"), ("Targeting_Max_Attack_Distance", "450")]);

        Assert.Empty(scene.Weapons);
        Assert.Contains(scene.Problems, p =>
            p.Message.Contains("MuzzleA", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildForObject_IgnoresAUnitWithNoWeaponBehaviour()
    {
        var scene = Build(
            "transport.alo",
            ["MuzzleA_00"],
            [("SpaceBehavior", "DUMMY_STARSHIP, SELECTABLE")]);

        Assert.Empty(scene.Weapons);
    }

    // ── the firing arc: engine defaults and the half/full angle convention ────

    /// <summary>
    ///     A unit weapon's arc comes from the turret EXTENTS, which are a plus-or-minus bound - so
    ///     the full angle the DTO carries is twice the authored number.
    /// </summary>
    /// <remarks>
    ///     <c>WeaponBehaviorClass::Is_In_Cone_Of_Fire</c> tests
    ///     <c>|yaw| &gt; Turret_Rotate_Extent_Degrees</c> WITHOUT halving it, while the hardpoint
    ///     path tests <c>|yaw| &gt; Fire_Cone_Width / 2.0</c>. Two conventions on one DTO field, so
    ///     the conversion has to happen here rather than at a consumer that cannot tell them apart.
    /// </remarks>
    [Fact]
    public void BuildForObject_DoublesTheTurretExtentIntoAFullAngle()
    {
        var weapon = Assert.Single(Fighter().Weapons);

        Assert.Equal(40f, weapon.ConeWidthDegrees);
        Assert.Equal(80f, weapon.ConeHeightDegrees);
    }

    /// <summary>
    ///     An object that sets no extent fires in ANY direction, because the constructor defaults
    ///     are 360 and 180 - not zero.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Measured at <c>00a87243</c> and <c>00a87256</c>: <c>Turret_Rotate_Extent_Degrees</c>
    ///         starts at 360.0 and <c>Turret_Elevate_Extent_Degrees</c> at 180.0, so both
    ///         comparisons are always true. 175 of the 291 objects with a <c>WEAPON</c> behaviour
    ///         set neither, which makes this the COMMON case rather than an edge.
    ///     </para>
    ///     <para>
    ///         Doubling those would give 720 and 360; a full angle saturates at 360, which is
    ///         already "everything", so the clamp loses nothing.
    ///     </para>
    /// </remarks>
    [Fact]
    public void BuildForObject_TreatsAnAbsentExtentAsUnrestricted()
    {
        var scene = Build(
            "rv_XWing.ALO",
            ["MuzzleA_00"],
            [
                ("SpaceBehavior", "WEAPON"),
                ("Targeting_Max_Attack_Distance", "450")
            ]);

        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(360f, weapon.ConeWidthDegrees);
        Assert.Equal(360f, weapon.ConeHeightDegrees);
    }

    /// <summary>
    ///     An authored zero is NOT the default. It is a weapon that can only fire dead ahead, and
    ///     conflating the two is what made an absent tag read as the most restrictive arc possible.
    /// </summary>
    [Fact]
    public void BuildForObject_KeepsAnAuthoredZeroExtent()
    {
        var scene = Build(
            "rv_XWing.ALO",
            ["MuzzleA_00"],
            [
                ("SpaceBehavior", "WEAPON"),
                ("Targeting_Max_Attack_Distance", "450"),
                ("Turret_Rotate_Extent_Degrees", "0"),
                ("Turret_Elevate_Extent_Degrees", "0")
            ]);

        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(0f, weapon.ConeWidthDegrees);
        Assert.Equal(0f, weapon.ConeHeightDegrees);
    }

    /// <summary>
    ///     A <c>Fires_Forward</c> weapon has no arc AT ALL, even when it authors extents.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Calculate_Projectile_Facing</c> returns the object's own facing and returns TRUE
    ///         before <c>Is_In_Cone_Of_Fire</c> is ever called, so the extents are not widened or
    ///         narrowed - they are not consulted. Null says "no arc concept here", which is a
    ///         different statement from 360 ("unrestricted") and from 0 ("dead ahead only").
    ///     </para>
    ///     <para>
    ///         This exact combination ships: <c>Landbombingrununits.xml</c> sets
    ///         <c>Fires_Forward</c> together with a 20/20 arc, and carries a comment saying the flag
    ///         exists to skip the check that would read it. Without this guard the preview would
    ///         draw that vanilla object a 40-degree cone it does not have.
    ///     </para>
    /// </remarks>
    [Fact]
    public void BuildForObject_GivesAFiresForwardWeaponNoArcEvenWhenExtentsAreAuthored()
    {
        var scene = Build(
            "rv_XWing.ALO",
            ["MuzzleA_00"],
            [
                ("SpaceBehavior", "WEAPON"),
                ("Targeting_Max_Attack_Distance", "450"),
                ("Fires_Forward", "Yes"),
                ("Turret_Rotate_Extent_Degrees", "20"),
                ("Turret_Elevate_Extent_Degrees", "20")
            ]);

        var weapon = Assert.Single(scene.Weapons);

        Assert.True(weapon.FiresForward);
        Assert.Null(weapon.ConeWidthDegrees);
        Assert.Null(weapon.ConeHeightDegrees);
    }

    /// <summary>
    ///     <c>Turret_XY_Only</c> drops the pitch test ENTIRELY rather than flattening it, so the
    ///     elevation is unbounded however the extent is authored.
    /// </summary>
    [Fact]
    public void BuildForObject_TreatsXyOnlyAsUnboundedPitch()
    {
        var scene = Build(
            "rv_XWing.ALO",
            ["MuzzleA_00"],
            [
                ("SpaceBehavior", "WEAPON"),
                ("Targeting_Max_Attack_Distance", "450"),
                ("Turret_Rotate_Extent_Degrees", "30"),
                ("Turret_Elevate_Extent_Degrees", "5"),
                ("Turret_XY_Only", "Yes")
            ]);

        var weapon = Assert.Single(scene.Weapons);

        Assert.Equal(60f, weapon.ConeWidthDegrees);
        Assert.Equal(360f, weapon.ConeHeightDegrees);
    }

    // ── the deployed arc (P5) ──────────────────────────────────────────────────

    /// <summary>
    ///     A deploying walker carries a second arc, and unwritten it is UNRESTRICTED.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Measured: <c>GameObjectClass::Is_Deployed</c> needs the type's <c>Deploys</c> flag and
    ///         the locomotor's answer, and only <c>WalkLocomotorBehaviorClass</c> ever answers yes. While
    ///         it does, the shot (<c>Is_In_Cone_Of_Fire</c>), the swing (<c>Adjust_Turret_Facing</c>)
    ///         and <c>Can_Point_At</c> all read <c>Deployed_Turret_*_Extent_Degrees</c> instead - whose
    ///         constructor defaults are 360 and 180.
    ///     </para>
    ///     <para>
    ///         So the AT-AT, at 55 / 60 normally and writing no deployed pair, fires in every direction
    ///         while deployed. 19 foc and 8 eaw deploying walkers are in that shape.
    ///     </para>
    /// </remarks>
    [Fact]
    public void BuildForObject_GivesADeployingWalkerAnUnrestrictedDeployedArcByDefault()
    {
        var weapon = Assert.Single(Walker().Weapons);

        Assert.Equal(110f, weapon.ConeWidthDegrees);
        Assert.Equal(360f, weapon.DeployedConeWidthDegrees);
        Assert.Equal(360f, weapon.DeployedConeHeightDegrees);
        Assert.Equal(360f, weapon.Turret!.DeployedRotateExtentDegrees);
        Assert.Equal(180f, weapon.Turret.DeployedElevateExtentDegrees);
    }

    /// <summary>An authored deployed pair follows the same conventions as the normal one.</summary>
    [Fact]
    public void BuildForObject_DoublesAnAuthoredDeployedPairLikeTheNormalOne()
    {
        var weapon = Assert.Single(Walker(("Deployed_Turret_Rotate_Extent_Degrees", "30"),
            ("Deployed_Turret_Elevate_Extent_Degrees", "20")).Weapons);

        Assert.Equal(60f, weapon.DeployedConeWidthDegrees);
        Assert.Equal(40f, weapon.DeployedConeHeightDegrees);
        Assert.Equal(30f, weapon.Turret!.DeployedRotateExtentDegrees);
        Assert.Equal(20f, weapon.Turret.DeployedElevateExtentDegrees);
    }

    // Deploys alone is not enough: the base locomotor always answers "not deployed".
    [Fact]
    public void BuildForObject_GivesNoDeployedArcWithoutAWalkLocomotor()
    {
        var weapon = Assert.Single(Build(
            "ev_atat.alo",
            ["MuzzleA_00"],
            [
                ("LandBehavior", "WEAPON, TURRET"),
                ("Deploys", "Yes"),
                ("Turret_Rotate_Extent_Degrees", "55")
            ]).Weapons);

        Assert.Null(weapon.DeployedConeWidthDegrees);
        Assert.Null(weapon.DeployedConeHeightDegrees);
    }

    [Fact]
    public void BuildForObject_GivesNoDeployedArcToAWalkerThatDoesNotDeploy()
    {
        var weapon = Assert.Single(Build(
            "ev_atat.alo",
            ["MuzzleA_00"],
            [
                ("LandBehavior", "WALK_LOCOMOTOR, WEAPON, TURRET"),
                ("Turret_Bone_Name", "Turret"),
                ("Turret_Rotate_Extent_Degrees", "55")
            ]).Weapons);

        Assert.Null(weapon.DeployedConeWidthDegrees);
        Assert.Null(weapon.Turret!.DeployedRotateExtentDegrees);
    }

    // Both switches apply to the deployed test exactly as to the normal one.
    [Fact]
    public void BuildForObject_AppliesFiresForwardAndXyOnlyToTheDeployedArc()
    {
        Assert.Null(Assert.Single(Walker(("Fires_Forward", "Yes")).Weapons).DeployedConeWidthDegrees);
        Assert.Equal(360f, Assert.Single(Walker(("Turret_XY_Only", "Yes"),
            ("Deployed_Turret_Elevate_Extent_Degrees", "10")).Weapons).DeployedConeHeightDegrees);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewScene Walker(params (string Name, string Value)[] extra)
    {
        return Build(
            "ev_atat.alo",
            ["MuzzleA_00", "MuzzleA_01", "Turret"],
            [
                ("LandBehavior", "WALK_LOCOMOTOR, WEAPON, TURRET, SELECTABLE"),
                ("Deploys", "Yes"),
                ("Targeting_Max_Attack_Distance", "500"),
                ("Turret_Bone_Name", "Turret"),
                ("Turret_Rotate_Extent_Degrees", "55"),
                ("Turret_Elevate_Extent_Degrees", "60"),
                .. extra
            ]);
    }

    private static PreviewScene Fighter()
    {
        return Build(
            "rv_XWing.ALO",
            [
                "MuzzleA_02", "MuzzleA_00", "MuzzleA_01", "MuzzleA_00_flash", "MuzzleB_00",
                "MuzzleC_00", "Root"
            ],
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
            new FileOrigin($"file:///{id}.xml", 0, 0), null);
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