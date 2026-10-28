// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Preview.Sections;

/// <summary>
///     Turrets and the arcs they fire through, as the engine reads them: a hardpoint's turret and a
///     unit's, the full angles the preview draws from the half-angle extents the XML writes, and the
///     defaults for the extents an object leaves unwritten (#314). Pure functions of the object.
/// </summary>
public static class PreviewFireArcs
{
    /// <summary>
    ///     The engine's defaults for the two turret extents, from its object type constructor.
    /// </summary>
    /// <remarks>
    ///     Both comparisons in the cone-of-fire test are then always true, so an object that
    ///     sets neither fires in ANY direction. 175 of the 291 objects with a <c>WEAPON</c>
    ///     behaviour set neither, which makes unrestricted the COMMON case - reading an absent tag
    ///     as zero gave the majority of armed units the narrowest arc possible.
    /// </remarks>
    private const float DefaultTurretRotateExtentDegrees = 360f;

    private const float DefaultTurretElevateExtentDegrees = 180f;

    /// <summary>
    ///     The same two defaults for a HARDPOINT turret, which are not the same numbers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>HardPointDataClass</c> writes
    ///         <c>TurretRotateExtentDegrees = 180.0</c> and
    ///         <c>TurretElevateExtentDegrees = 90.0</c>. A hardpoint's turret fields are a parallel
    ///         set to the unit's rather than the same ones, and so are their defaults.
    ///     </para>
    ///     <para>
    ///         180 is the exact threshold at which
    ///         the turret-angle calculation skips its motion clamp - the
    ///         gate is <c>extent &lt; 180.0 &amp;&amp; extent &gt; 0.0</c> - so the default yaw is a
    ///         free turn and the default pitch is a real stop at plus or minus 90.
    ///     </para>
    /// </remarks>
    private const float DefaultHardpointRotateExtentDegrees = 180f;

    private const float DefaultHardpointElevateExtentDegrees = 90f;

    /// <summary>A full angle covers everything at 360; there is nothing past a whole turn.</summary>
    private const float FullTurnDegrees = 360f;

    /// <summary>The turret pose, or null when the hardpoint is not one.</summary>
    public static PreviewTurret? Turret(EffectiveObject effective)
    {
        if (!effective.BoolOf("Is_Turret"))
            return null;

        // EFFECTIVE, not authored: the client clamps a drag handle to these, and the two owners
        // default differently. See DefaultHardpointRotateExtentDegrees.
        return new PreviewTurret(
            effective.FloatOf("Turret_Rest_Angle"),
            effective.FloatOf("Turret_Rotate_Extent_Degrees")
            ?? DefaultHardpointRotateExtentDegrees,
            effective.FloatOf("Turret_Elevate_Extent_Degrees")
            ?? DefaultHardpointElevateExtentDegrees,
            effective.ValueOf("Turret_Bone_Name"),
            effective.ValueOf("Barrel_Bone_Name"));
    }

    /// <summary>
    ///     The turret pose for a unit weapon, or null when the unit names no turret bone.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Turret" />, which gates on a hardpoint's <c>Is_Turret</c> flag -
    ///     a tag the unit case does not have. Here the bone names are the evidence.
    /// </remarks>
    public static PreviewTurret? UnitTurret(EffectiveObject effective)
    {
        var turretBone = effective.ValueOf("Turret_Bone_Name");
        var barrelBone = effective.ValueOf("Barrel_Bone_Name");

        if (string.IsNullOrEmpty(turretBone) && string.IsNullOrEmpty(barrelBone))
            return null;

        // EFFECTIVE, like the hardpoint's - but off the UNIT's defaults, which are 360 and 180. The
        // deployed pair has the same defaults, and Adjust_Turret_Facing swaps it in
        // whenever the unit is deployed - so only a unit that can deploy gets one.
        var canDeploy = CanDeploy(effective);
        return new PreviewTurret(
            null,
            effective.FloatOf("Turret_Rotate_Extent_Degrees")
            ?? DefaultTurretRotateExtentDegrees,
            effective.FloatOf("Turret_Elevate_Extent_Degrees")
            ?? DefaultTurretElevateExtentDegrees,
            string.IsNullOrEmpty(turretBone) ? null : turretBone,
            string.IsNullOrEmpty(barrelBone) ? null : barrelBone,
            canDeploy
                ? effective.FloatOf("Deployed_Turret_Rotate_Extent_Degrees") ?? DefaultTurretRotateExtentDegrees
                : null,
            canDeploy
                ? effective.FloatOf("Deployed_Turret_Elevate_Extent_Degrees") ?? DefaultTurretElevateExtentDegrees
                : null);
    }

    /// <summary>
    ///     Whether the engine can ever put this unit in the deployed state.
    /// </summary>
    /// <remarks>
    ///     the is-deployed test requires the type's <c>Deploys</c> flag
    ///     (the is-deployable test, field <c>+0xf6</c>) and then asks the locomotor. The base
    ///     the is-deployed test returns false; only
    ///     the WALK locomotor behaviour answers yes, in <c>LST_WALK_DEPLOYED</c>. Any other locomotor
    ///     with <c>Deploys</c> set is never deployed, so it has no second arc to draw.
    /// </remarks>
    public static bool CanDeploy(EffectiveObject effective)
    {
        return effective.BoolOf("Deploys")
               && ObjectBehaviors.Has(effective, "WALK_LOCOMOTOR");
    }

    /// <summary>
    ///     Whether the weapon skips the arc check entirely, in which case it HAS no arc.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         the projectile-facing calculation returns the object's own facing and returns true
    ///         before the cone-of-fire test is reached, so authored extents are not widened or
    ///         narrowed - they are never read. Null then means "no arc concept", which is a
    ///         different statement from 360 ("unrestricted") and 0 ("dead ahead only").
    ///     </para>
    ///     <para>
    ///         The guard is not hypothetical: <c>Landbombingrununits.xml</c> ships
    ///         <c>Fires_Forward</c> alongside a 20/20 arc, with a comment saying the flag exists to
    ///         skip the check that reads it.
    ///     </para>
    /// </remarks>
    public static bool FiresForward(EffectiveObject effective)
    {
        return effective.BoolOf("Fires_Forward");
    }

    /// <summary>
    ///     The unit weapon's horizontal arc as a FULL angle, which is what
    ///     <see cref="PreviewWeapon.ConeWidthDegrees" /> carries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The conversion is the point. the cone-of-fire test tests
    ///         <c>|yaw| &gt; Turret_Rotate_Extent_Degrees</c> WITHOUT halving, so the extent is a
    ///         plus-or-minus bound and the swept angle is twice it. The hardpoint path on the same
    ///         DTO field tests <c>|yaw| &gt; Fire_Cone_Width / 2.0</c>, so THAT number is already a
    ///         full angle. Two conventions, one field - so the normalisation belongs here, where
    ///         both are known, rather than at a consumer that cannot tell which it was handed.
    ///     </para>
    ///     <para>
    ///         An authored zero survives as zero: a weapon that can only fire dead ahead is a real
    ///         thing and must not be confused with the default.
    ///     </para>
    /// </remarks>
    /// <param name="effective">The unit, with its variant chain resolved.</param>
    /// <param name="deployed">
    ///     The arc while deployed: <c>Deployed_Turret_Rotate_Extent_Degrees</c>, whose default is the same
    ///     360, and null for a unit that is never deployed (<see cref="CanDeploy" />).
    /// </param>
    public static float? UnitArcYawDegrees(EffectiveObject effective, bool deployed = false)
    {
        if (FiresForward(effective) || (deployed && !CanDeploy(effective))) return null;

        var tag = deployed ? "Deployed_Turret_Rotate_Extent_Degrees" : "Turret_Rotate_Extent_Degrees";
        return FullAngle(effective.FloatOf(tag) ?? DefaultTurretRotateExtentDegrees);
    }

    /// <summary>The vertical arc, as a full angle.</summary>
    /// <remarks>
    ///     <c>Turret_XY_Only</c> makes the cone-of-fire test skip the pitch test ENTIRELY rather
    ///     than flattening it, so the elevation is unbounded however the extent is authored.
    /// </remarks>
    /// <param name="effective">The unit, with its variant chain resolved.</param>
    /// <param name="deployed">As for <see cref="UnitArcYawDegrees" />.</param>
    public static float? UnitArcPitchDegrees(EffectiveObject effective, bool deployed = false)
    {
        if (FiresForward(effective) || (deployed && !CanDeploy(effective))) return null;

        // Tested before the pitch is read in the cone-of-fire test, so it drops the deployed test too.
        if (effective.BoolOf("Turret_XY_Only"))
            return FullTurnDegrees;

        var tag = deployed ? "Deployed_Turret_Elevate_Extent_Degrees" : "Turret_Elevate_Extent_Degrees";
        return FullAngle(effective.FloatOf(tag) ?? DefaultTurretElevateExtentDegrees);
    }

    /// <summary>
    ///     A hardpoint's horizontal arc as a full angle - from its turret extent when it is one,
    ///     and from its fire cone when it is not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         the weapon-aim test branches and the two arms are EXCLUSIVE:
    ///         <c>if (Is_Turret)</c> it tests <c>Turret_Rotate_Extent_Degrees &lt; |yaw|</c>, and
    ///         the fire cone is tested only in the <c>else</c>. A turret's cone is therefore never
    ///         read - all seven shipped turret hardpoints declare one anyway, and
    ///         <c>HP_Gargantuan_Small_Turret_Front_Left</c>'s 450-degree height has survived in both
    ///         games precisely because nothing looks at it.
    ///     </para>
    ///     <para>
    ///         The extent is a plus-or-minus bound the engine does not halve; the cone is a full
    ///         angle it does. Hence one doubled and the other passed through.
    ///     </para>
    /// </remarks>
    public static float? HardpointArcYawDegrees(EffectiveObject effective)
    {
        if (!effective.BoolOf("Is_Turret"))
            return effective.FloatOf("Fire_Cone_Width");

        return FullAngle(effective.FloatOf("Turret_Rotate_Extent_Degrees") ?? 0f);
    }

    /// <summary>The vertical arc, which a turret hardpoint simply does not have.</summary>
    /// <remarks>
    ///     The turret arm of that branch tests yaw and then returns - there is no elevation test on
    ///     the SHOT at all. <c>Turret_Elevate_Extent_Degrees</c> is read only by
    ///     the turret-angle calculation, which bounds where the BARREL may point. Reporting
    ///     the barrel's limit as the shot's would draw an arc the engine does not enforce.
    /// </remarks>
    public static float? HardpointArcPitchDegrees(EffectiveObject effective)
    {
        if (!effective.BoolOf("Is_Turret"))
            return effective.FloatOf("Fire_Cone_Height");

        return FullTurnDegrees;
    }

    /// <summary>
    ///     A plus-or-minus bound as the full angle it sweeps, saturating at a whole turn.
    /// </summary>
    /// <remarks>
    ///     The defaults double to 720 and 360; both mean "everything", and a full angle cannot
    ///     usefully exceed 360, so the clamp discards nothing.
    /// </remarks>
    public static float FullAngle(float halfAngleDegrees)
    {
        return Math.Min(halfAngleDegrees * 2f, FullTurnDegrees);
    }
}
