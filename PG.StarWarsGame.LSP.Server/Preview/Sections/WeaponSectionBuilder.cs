// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Preview.Sections;

/// <summary>
///     What a scene fires with: a hardpoint's weapon and turret, the unit's own weapon for the
///     objects carrying a <c>WEAPON</c> behaviour, their fire arcs and inaccuracy (#314). The arc
///     defaults are the engine's, measured; see each constant.
/// </summary>
public sealed class WeaponSectionBuilder(IVariantTagSource tagSource, IMessageCatalog messages)
{
    private static readonly string[] BehaviorTags = ["SpaceBehavior", "LandBehavior"];

    private static readonly string[] FireModeTags =
    [
        "Fire_When_Deployed", "Fire_When_Undeployed", "Fire_When_In_Rocket_Attack_Mode",
        "Fire_When_In_Normal_Attack_Mode", "Fire_When_In_Defend_Mode"
    ];

    /// <summary>
    ///     The hardpoint's weapon, or null when it names no fire bone.
    /// </summary>
    /// <remarks>
    ///     A shield generator or a docking bay is a hardpoint with no armament, so an absent weapon
    ///     is the normal case for roughly half of them, not a failure.
    /// </remarks>
    public PreviewWeapon? Weapon(GameIndex index, EffectiveObject effective, string hardpointId)
    {
        var bones = new[] { effective.ValueOf("Fire_Bone_A"), effective.ValueOf("Fire_Bone_B") }
            .Where(b => !string.IsNullOrEmpty(b))
            .Select(b => b!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bones.Count == 0)
            return null;

        return new PreviewWeapon(
            $"hardpoint:{hardpointId}",
            PreviewWeaponSource.Hardpoint,
            hardpointId,
            effective.ValueOf("Type") ?? hardpointId,
            bones,
            effective.BoolOf("Randomize_Between_Fire_Bones")
                ? PreviewFirePointMode.RandomAlongLine
                : PreviewFirePointMode.CycleBones,
            // Fires_Forward lives on GameObjectType, never on a hardpoint.
            false,
            effective.ValueOf("Fire_Projectile_Type"),
            effective.FloatOf("Projectile_Damage"),
            effective.ValueOf("Damage_Type"),
            effective.FloatOf("Fire_Range_Distance"),
            effective.FloatOf("Fire_Min_Range_Distance"),
            PreviewFireArcs.HardpointArcYawDegrees(effective),
            PreviewFireArcs.HardpointArcPitchDegrees(effective),
            Inaccuracy(index, hardpointId),
            effective.IntOf("Fire_Pulse_Count"),
            effective.FloatOf("Fire_Pulse_Delay_Seconds"),
            effective.FloatOf("Fire_Min_Recharge_Seconds") ??
            effective.FloatOf("Fire_Max_Recharge_Seconds"),
            FireModes(effective),
            PreviewFireArcs.Turret(effective),
            effective.ValueOf("Fire_SFXEvent"));
    }

    /// <summary>
    ///     The unit's own weapon, for the <c>WEAPON</c>-behaviour case, or null when it has none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         188 objects across the two trees are armed this way against 68 with hardpoints, so
    ///         this is the COMMON case rather than the exception - it just had no support at all.
    ///     </para>
    ///     <para>
    ///         Nothing in the XML says where the shot leaves from: the engine cycles the hull's
    ///         <c>MuzzleA_#</c> bones, one per volley. That convention is resolved here rather than in
    ///         the webview because every other naming rule in this product lives server-side, and
    ///         completion and validation on <c>Turret_Bone_Name</c> want the same knowledge.
    ///     </para>
    /// </remarks>
    public PreviewWeapon? UnitWeapon(GameIndex index,
        EffectiveObject effective, string? hull, List<PreviewProblem> problems)
    {
        if (!HasWeaponBehavior(effective))
            return null;

        var turret = PreviewFireArcs.UnitTurret(effective);
        var bones = MuzzleBones(index, hull, turret);

        // ReSharper disable once InvertIf - a guard, read as one; the weapon stays the last word.
        if (bones.Count == 0)
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewNoMuzzleBones, "warning",
                messages.Format("PreviewScene_NoMuzzleBones", effective.ObjectId)));
            return null;
        }

        return new PreviewWeapon(
            "weapon:A",
            PreviewWeaponSource.Unit,
            null,
            messages.Format("PreviewScene_Muzzle"),
            bones,
            // Randomize_Between_Fire_Bones is a HardPoint parameter and is not on GameObjectType at
            // all, so this case has no other branch.
            PreviewFirePointMode.CycleBones,
            effective.BoolOf("Fires_Forward"),
            // A list, but only 2 of 95 shipped objects name more than one.
            PreviewTagReader.FirstToken(effective.ValueOf("Projectile_Types")),
            effective.FloatOf("Damage"),
            effective.ValueOf("Damage_Type"),
            effective.FloatOf("Targeting_Max_Attack_Distance"),
            null,
            PreviewFireArcs.UnitArcYawDegrees(effective),
            PreviewFireArcs.UnitArcPitchDegrees(effective),
            Inaccuracy(index, effective.ObjectId),
            effective.IntOf("Projectile_Fire_Pulse_Count"),
            effective.FloatOf("Projectile_Fire_Pulse_Delay_Seconds"),
            effective.FloatOf("Projectile_Fire_Recharge_Seconds"),
            [],
            turret,
            null,
            PreviewFireArcs.UnitArcYawDegrees(effective, true),
            PreviewFireArcs.UnitArcPitchDegrees(effective, true));
    }

    /// <summary>Whether either behaviour list names <c>WEAPON</c>.</summary>
    private static bool HasWeaponBehavior(EffectiveObject effective)
    {
        return BehaviorTags
            .SelectMany(tag => (effective.ValueOf(tag) ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(token => token.Equals("WEAPON", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The hull's <c>MuzzleA_#</c> bones, in number order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The A bones only. <c>MuzzleB</c> is a naming convention rather than something the engine
    ///         consumes for this case, and <c>MuzzleC</c> is a single bone on <c>Rv_bwing.alo</c> in
    ///         the whole corpus.
    ///     </para>
    ///     <para>
    ///         Two exclusions, both measured. A <c>*_flash</c> name is the flash GEOMETRY beside the
    ///         fire point, driven by the clip's own visibility tracks - firing from it would double
    ///         every shot. And a bone already named as the turret or barrel is not repeated:
    ///         <c>T2B_Tank</c> names <c>MuzzleA_00</c> as its <c>Barrel_Bone_Name</c>, so the two
    ///         conventions overlap on exactly that unit.
    ///     </para>
    /// </remarks>
    private static List<string> MuzzleBones(GameIndex index, string? hull, PreviewTurret? turret)
    {
        if (string.IsNullOrEmpty(hull) ||
            !index.ModelBones.TryGetValue(ModelBoneKey.From(hull), out var names))
            return [];

        var claimed = new[] { turret?.TurretBone, turret?.BarrelBone }
            .Where(b => !string.IsNullOrEmpty(b))
            .Select(b => b!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. names
                .Where(n => n.StartsWith("MuzzleA_", StringComparison.OrdinalIgnoreCase))
                .Where(n => !n.Contains("flash", StringComparison.OrdinalIgnoreCase))
                .Where(n => !claimed.Contains(n))
                .OrderBy(MuzzleNumber)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>The trailing number of a muzzle bone, or <see cref="int.MaxValue" /> when it has none.</summary>
    private static int MuzzleNumber(string boneName)
    {
        var underscore = boneName.LastIndexOf('_');

        return underscore >= 0 &&
               int.TryParse(boneName.AsSpan(underscore + 1), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;
    }

    /// <summary>
    ///     How far this weapon's shots stray, per target category.
    /// </summary>
    /// <remarks>
    ///     Through <see cref="RepeatedTagReader" /> rather than the effective object, exactly as the
    ///     reticle and armor tables are: the resolver COLLAPSES a repeated tag to one occurrence, so
    ///     an object declaring seven categories would answer with whichever survived. It was read as
    ///     a single <c>Number</c> before, which cannot parse <c>Fighter, 30.0</c> - so the field has
    ///     never once been non-null, on any of the 1017 rows the shipped tree declares.
    /// </remarks>
    private List<PreviewInaccuracy> Inaccuracy(GameIndex index, string objectId)
    {
        var rows = new List<PreviewInaccuracy>();

        foreach (var row in RepeatedTagReader.Rows(
                     index, tagSource, objectId, "Fire_Inaccuracy_Distance", 2))
        {
            // A row naming no category, or a distance that is not a number, is a broken row rather
            // than a zero - dropping it leaves the weapon with one fewer category, which is what the
            // engine would do with a value it cannot read.
            if (row[0].Length == 0
                || !float.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var distance))
                continue;

            rows.Add(new PreviewInaccuracy(row[0], distance));
        }

        return rows;
    }

    /// <summary>The <c>Fire_When_*</c> gates this hardpoint sets.</summary>
    private static List<string> FireModes(EffectiveObject effective)
    {
        return [.. FireModeTags.Where(tag => effective.BoolOf(tag))];
    }
}