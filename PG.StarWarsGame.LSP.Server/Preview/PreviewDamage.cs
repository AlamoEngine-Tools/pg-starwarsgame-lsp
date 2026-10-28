// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>Three floats as the XML writes them, for a direction or an axis of spin.</summary>
public sealed record PreviewVector3(float X, float Y, float Z);

/// <summary>
///     The wreckage a hardpoint sheds when it is destroyed.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Death_Breakoff_Prop</c> names an ordinary <c>SpaceProp</c> with its own model and a
///         <c>DEBRIS</c> behaviour, so the hardpoint does not simply vanish - it breaks off and tumbles
///         away burning. 167 of foc's 355 hardpoints name one, 147 of them distinct.
///     </para>
///     <para>
///         Carried once per scene rather than inline on the hardpoint: mirrored hardpoints share a prop,
///         and a copy each would have the client instantiate the same wreck twice.
///     </para>
/// </remarks>
/// <param name="Resolved">
///     Whether the named prop is defined. An unresolved one is still listed - a renamed prop with a
///     hardpoint left pointing at the old name is exactly the authoring mistake worth surfacing.
/// </param>
/// <param name="MovementVector">
///     The direction the debris drifts, from <c>Debris_Movement_Vector</c>. Model space.
/// </param>
/// <param name="FacingRotateVector">Its axis of tumble, from <c>Debris_Facing_Rotate_Vector</c>.</param>
/// <param name="Animations">
///     The clips of the PROP'S OWN model, by file name. See <see cref="PreviewDeathClone" /> for why
///     a passive subject has to carry its own.
/// </param>
/// <param name="Particles">
///     The proxies the prop's own model carries - a burning piece of debris trails its own fire.
///     See <see cref="PreviewDeathClone" /> for how the client reads their <c>PartId</c>.
/// </param>
public sealed record PreviewBreakoffProp(
    string Id,
    string? ModelRef,
    bool Resolved,
    PreviewVector3? MovementVector,
    PreviewVector3? FacingRotateVector,
    float? MinLifetimeSeconds,
    float? MaxLifetimeSeconds,
    string? AttachedParticle,
    string? DeathExplosions,
    bool RemoveUponDeath,
    IReadOnlyList<string>? Animations = null,
    IReadOnlyList<PreviewParticle>? Particles = null)
{
    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<string> Animations { get; init; } = Animations ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewParticle> Particles { get; init; } = Particles ?? [];
}

/// <summary>
///     The automated death clone: a unit that declares none keeps flying and comes apart.
/// </summary>
/// <remarks>
///     <para>
///         The user's account of the mechanic: with <c>Spin_Away_On_Death</c> set and NO
///         <c>Death_Clone</c> declared, the unit carries on along its current vector at its current
///         speed, corkscrewing, and explodes at the end.
///     </para>
///     <para>
///         Measured over both shipped trees:
///         <b>
///             34 objects declare it, all Yes, and not one of them
///             also declares a Death_Clone.
///         </b>
///         The rule holds in the data. The engine's own parameter
///         table names five tags in the family, so the time, the chance and the explosion are read
///         rather than invented; only the corkscrew itself has no number in any file.
///     </para>
/// </remarks>
/// <param name="TimeSeconds">
///     <c>Spin_Away_On_Death_Time</c>. 31 of the 34 write <c>2.0f</c> and 3 write <c>1.0f</c> - note
///     the <c>f</c> suffix, which is in the files and has to be parsed off.
/// </param>
/// <param name="Chance">
///     <c>Spin_Away_On_Death_Chance</c>, 0 to 1. Shipped values are 0.2 (21) and 0.4 (13), so most
///     deaths do NOT spin. The preview always spins and READS THIS OUT instead - a tool pressed to
///     see a thing has to show the thing, and a one-in-five roll looks broken four times out of five.
/// </param>
/// <param name="Explosion">
///     <c>Spin_Away_On_Death_Explosion</c> - its OWN explosion, fired at the end of the spin, and a
///     different tag from the object's <c>Death_Explosions</c>.
/// </param>
/// <param name="MaxSpeed">
///     <c>Max_Speed</c>, which all 34 declare. **Per FRAME, not per second** - the engine runs at
///     30Hz, so 4.5 here is 135 units a second. The client does that multiplication.
/// </param>
public sealed record PreviewSpinAway(
    float TimeSeconds,
    float Chance,
    string? Explosion,
    float MaxSpeed);

/// <summary>
///     What this object leaves behind when it dies, and what killed it decides which one.
/// </summary>
/// <remarks>
///     <para>
///         <c>&lt;Death_Clone&gt; Damage_Type, Object_Id &lt;/...&gt;</c> - 345 rows in foc, 134 in
///         eaw, and 344 of the 345 carry exactly those two fields. The damage type is what ties this
///         to the attacker panel: the weapon a reader builds decides which clone the target would
///         leave.
///     </para>
///     <para>
///         The ONE tag in the schema with <c>variantMode: merge</c> plus <c>multipleAllowed</c>, so
///         the resolver keeps every occurrence and no <c>RepeatedTagReader</c> is needed - and the
///         merge is ADDITIVE across a variant chain, so a re-skinned variant keeps its base's clones
///         as well as its own.
///     </para>
/// </remarks>
/// <param name="PlaysIdle">
///     <c>Should_Death_Clone_Play_Idle</c>, which is a Boolean on the OBJECT rather than a field on
///     the row - so it reads the same on every clone this object names. 28 shipped uses, all
///     <c>true</c>. Worth knowing that it asks for an idle the clone models do not appear to carry:
///     of eaw's 44 clone objects with a model, 42 ship only a <c>_die</c> clip and NONE ships an
///     idle.
/// </param>
/// <param name="Animations">
///     The clips of the CLONE'S OWN model, by file name.
/// </param>
/// <remarks>
///     <para>
///         Carried here rather than left to the scene's list, which describes the subject. That the
///         clone ever played at all was an accident of naming: a clone's model is conventionally the
///         hull's name plus a suffix - <c>Ev_stardestroyer_d.alo</c> beside
///         <c>Ev_stardestroyer.alo</c> - so its <c>_die_00.ala</c> matched the hull's stem prefix
///         and rode along in the hull's list, to be dropped from the hull's own GLB when it failed
///         to bind to that skeleton. A clone named anything else got no clip whatsoever.
///     </para>
/// </remarks>
/// <param name="Particles">
///     The proxies the clone's own model carries - for the Star Destroyer's wreck, eight of them:
///     the explosions, the fire smoke and the debris trails that ARE the death.
///     Their <c>PartId</c> names the CLONE OBJECT, not a part in the scene, because the server
///     cannot know what the client will call the instance it loads. It is a descriptor of a model,
///     and the client rewrites both it and the ids when it puts one in the scene.
/// </param>
public sealed record PreviewDeathClone(
    string? DamageType,
    string ObjectId,
    string? ModelFile,
    bool PlaysIdle,
    IReadOnlyList<string>? Animations = null,
    IReadOnlyList<PreviewParticle>? Particles = null,
    // The CLONE's own Scale_Factor, or 1 - not the ship's.
    // A death clone is spawned as its own object, so the engine's transform update draws it at its own
    // scale and nothing relates it to the ship's. The two usually agree but often do not:
    // measured over 265 ship-to-clone pairs in eaw/ and foc/, 206 declare the
    // same scale and 59 differ - the Millennium Falcon is 0.5 and its clone 1.0, the slave
    // infantry 1.5 against clones at 1.0.
    float ScaleFactor = 1f)
{
    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<string> Animations { get; init; } = Animations ?? [];

    /// <summary>Never null, so the client has one shape to walk.</summary>
    public IReadOnlyList<PreviewParticle> Particles { get; init; } = Particles ?? [];
}

/// <summary>
///     What a hit on this subject has to get through.
/// </summary>
/// <remarks>
///     <para>
///         The previewed object is the TARGET. The attacker is a weapon the reader builds in the
///         panel, so everything defensive travels with the scene and everything offensive is theirs
///         to type.
///     </para>
///     <para>
///         The armor axis of <c>Damage_To_Armor_Mod</c> is FIXED by the target - one
///         <c>Armor_Type</c> and one <c>Shield_Armor_Type</c> - so only those two columns are sent
///         rather than all 2426 rows. The damage axis is not fixed, because the reader picks it,
///         which is why <paramref name="DamageTypes" /> carries the whole list.
///     </para>
///     <para>
///         A damage type absent from <paramref name="HullFactors" /> or
///         <paramref name="ShieldFactors" /> is <strong>1.0</strong>, not zero. The shipped table
///         names barely half of its 4293 possible pairs, so the gap is hit constantly.
///     </para>
/// </remarks>
/// <param name="IsShielded">
///     Whether any of the three behaviour lists names <c>SHIELDED</c>. Measured over foc:
///     <c>SpaceBehavior</c> 98 times, plain <c>Behavior</c> 32, <c>LandBehavior</c> 19 - so all
///     three have to be read. Without it the shield is not in play at all, whatever
///     <c>Shield_Points</c> says.
/// </param>
public sealed record PreviewTargetDefence(
    bool IsShielded,
    string? ArmorType,
    string? ShieldArmorType,
    float? ShieldPoints,
    float? TacticalHealth,
    float? EnergyCapacity,
    // The summed Health of every DESTRUCTIBLE hardpoint, or null where there are none.
    //     A unit with hardpoints cannot be targeted itself - only its hardpoints can - and it dies
    //     when the last of them does. Untargetable hardpoints count: the game warns about that
    //     combination and at least two mods use it deliberately, so an untargetable hardpoint still
    //     has to die before the unit does. Indestructible ones cannot contribute to a death and
    //     are left out.
    //     Sent ALONGSIDE TacticalHealth rather than replacing it, because
    //     the two are SEPARATE POOLS and disagree in the shipped data - the Star Destroyer
    //     declares 2000 against 4075 of hardpoint health. How the engine reconciles them was
    //     settled by decompiling the 2018 build: each pool is capped at the other's PERCENTAGE
    //     plus HullVsHardpointsConstraint, so the absolute totals never have to
    //     agree. See DiesWithHardpoints for the half that is gated.
    float? HardpointHealthTotal,
    // Should_Be_Destroyed_When_All_Hardpoints_Destroyed, defaulting to true.
    // Gates three links from the hardpoints back to the hull: dying when the last destroyable
    // hardpoint does, the per-tick pull of the hull down toward the hardpoints, and the health
    // bar's use of the hardpoint pool. With it off - U_Ground_Palace is the one vanilla
    // object that says so - hardpoints still absorb and still die, they just stop reaching the
    // hull by any route. Absent means true: exactly one shipped object writes the tag.
    bool DiesWithHardpoints,
    // Hull_Vs_Hard_Points_Health_Constraint from GameConstants, shipped at 0.2.
    // How far either pool may run ahead of the other, as a fraction. Read rather than assumed
    // because it is a global a mod can change, and the change is drastic: at 1 every cap clamps
    // to 100% and BOTH corrections stop running, leaving the pools fully independent. Drawing
    // vanilla's 0.2 for such a mod would show a leash their game does not have.
    float HullVsHardpointsConstraint,
    // Keyed by game data - a damage type name - which the camel-case naming strategy would otherwise
    // rewrite. See VerbatimKeyDictionaryConverter.
    [property: JsonConverter(typeof(VerbatimKeyDictionaryConverter))]
    IReadOnlyDictionary<string, float> HullFactors,
    [property: JsonConverter(typeof(VerbatimKeyDictionaryConverter))]
    IReadOnlyDictionary<string, float> ShieldFactors,
    IReadOnlyList<string> DamageTypes);

/// <summary>
///     One row of the damage table: the health band's upper bound, and the stage shown inside it.
/// </summary>
/// <remarks>
///     <para>
///         The thresholds are the upper and lower BOUND of each stage, not a list of trip points.
///         <c>1, 0.66, 0.33, 0</c> against <c>0, 1, 2, 3</c> reads: 100% &gt; h &gt; 66% is ALT0,
///         66% &gt; h &gt; 33% is ALT1, 33% &gt; h &gt; 0% is ALT2, and 0 is ALT3. So a row's own
///         threshold is its ceiling and the NEXT row's is its floor; the last row runs to zero.
///     </para>
///     <para>
///         Sent as ordered PAIRS because <see cref="PreviewScene.DamageStages" /> deliberately sorts
///         and de-duplicates - the right shape for "which stages exist" and useless for "which stage
///         at what health". Measured over foc with XML comments stripped: 219 objects declare the
///         table, every one has thresholds, and the two columns never disagree in length.
///     </para>
/// </remarks>
public sealed record PreviewDamageBand(float Threshold, int Stage);