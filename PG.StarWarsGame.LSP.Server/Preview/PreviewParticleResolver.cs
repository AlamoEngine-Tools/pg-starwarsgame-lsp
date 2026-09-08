// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Server.Abilities;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>What decides whether a particle system is playing.</summary>
public enum PreviewParticleGate
{
    /// <summary>Plays whenever the model is shown, subject to the proxy's own visibility flag.</summary>
    Always,

    /// <summary>Damage smoke: off until its hardpoint is destroyed.</summary>
    HardpointDestroyed,

    /// <summary>
    ///     Engine glow: on until its hardpoint dies, and only when that hardpoint says its death puts
    ///     the glow out.
    /// </summary>
    HardpointAlive
}

/// <summary>One particle system attached to a bone of a loaded part.</summary>
/// <param name="Id">Unique within the scene. Proxy names repeat, so the name alone will not do.</param>
/// <param name="SystemRef">The particle system to fetch, as the proxy names it.</param>
/// <param name="PartId">The part whose skeleton carries the bone.</param>
/// <param name="Bone">The bone to hang it off, so it follows the model as it animates.</param>
/// <param name="HardpointId">The hardpoint that switches it, when one does.</param>
/// <param name="StartsVisible">The proxy's own flag. Some effects ship switched off.</param>
/// <param name="Alt">
///     Damage state this effect belongs to, or null when it is untagged and therefore always shown.
///     Read off the proxy NAME's <c>_ALT&lt;n&gt;</c> suffix.
/// </param>
/// <param name="Lod">Detail level, same convention as <paramref name="Alt" />.</param>
/// <param name="AltDecreaseStayHidden">
///     Keeps the effect hidden while the damage state is winding DOWN. This is the repair asymmetry:
///     fire lit on the way to destruction should not flicker back on as a hardpoint is repaired.
/// </param>
/// <param name="ClaimsAbility">
///     The ability this proxy's BONE NAME claims by its prefix, or null for the 5404 that claim
///     nothing. See <see cref="AbilityProxyPrefix" />.
///     <para>
///         A claim is not a binding. The object has to declare the type as well, and when it does
///         not the engine simply never shows the effect - <c>Tartan_Patrol_Cruiser</c> declares
///         POWER_TO_WEAPONS and carries <c>Pte_tartanengine_lrg</c> for a TURBO it does not have.
///         Carried on the wire because the client is where visibility is decided, and without it an
///         unbound engine proxy fell through to the ordinary path and lit up on open.
///     </para>
/// </param>
public sealed record PreviewParticle(
    string Id,
    string SystemRef,
    string PartId,
    string Bone,
    /// <summary>
    ///     The bone's index in the model, which is the only thing that tells two same-named bones
    ///     apart. Boba Fett carries two <c>p_boba_jetpack</c> bones and the client keys its own by
    ///     name, so with a name alone both effects landed on one jet.
    /// </summary>
    int BoneIndex,
    PreviewParticleGate Gate,
    string? HardpointId,
    bool StartsVisible,
    int? Alt = null,
    int? Lod = null,
    bool AltDecreaseStayHidden = false,
    string? ClaimsAbility = null);

/// <summary>
///     Works out which particle system is attached to which bone, and what switches it on.
/// </summary>
/// <remarks>
///     <para>
///         A model carries its effects as proxies: a named reference to a particle system, pinned to a
///         bone. Nothing in the XML names a proxy - the hardpoint names a HULL bone through
///         <c>Damage_Particles</c>, and the smoke is every proxy whose bone's PARENT is that bone.
///         That join is what lets the preview blow a hardpoint off and light exactly the right smoke,
///         which is the part no standalone tool can do.
///     </para>
///     <para>
///         Measured on <c>Ev_stardestroyer.alo</c>: 22 proxies, twenty of them
///         <c>p_hp_imperial_damage</c> under the <c>HP_*_EmitDamage</c> bones, one
///         <c>pe_stardestroyerengines</c> under <c>engines_big</c>, and one <c>pi_damage_elec_SD00</c>
///         the file itself marks invisible.
///     </para>
/// </remarks>
public static class PreviewParticleResolver
{
    public static IReadOnlyList<PreviewParticle> Resolve(
        string partId,
        IReadOnlyList<AlamoModelBone> bones,
        IReadOnlyList<AlamoProxy> proxies,
        IReadOnlyList<PreviewHardpoint> hardpoints)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(proxies);
        ArgumentNullException.ThrowIfNull(hardpoints);

        var damageOwners = OwnersByBone(hardpoints, h => h.DamageParticlesBone);

        // Only the hardpoints whose death actually puts the glow out; the rest leave it burning, and
        // gating those would switch off an effect the engine never touches.
        var engineOwners = OwnersByBone(
            hardpoints.Where(h => h.EngineDeathHidesEngineParticles).ToList(),
            h => h.EngineParticlesBone);

        var particles = new List<PreviewParticle>();

        for (var i = 0; i < proxies.Count; i++)
        {
            var proxy = proxies[i];

            // A file-controlled index. One bad proxy costs its own effect, not the preview.
            if (proxy.BoneIndex < 0 || proxy.BoneIndex >= bones.Count)
                continue;

            var bone = bones[proxy.BoneIndex];
            var parent = bone.ParentIndex >= 0 && bone.ParentIndex < bones.Count
                ? bones[bone.ParentIndex].Name
                : null;

            var (gate, hardpointId) = Gate(parent, damageOwners, engineOwners);

            particles.Add(new PreviewParticle(
                // Indexed, not named: twenty of the Star Destroyer's proxies share one name, and
                // keying on that would collapse them into a ship smoking from a single corner.
                $"{partId}#{i}",
                proxy.Name,
                partId,
                bone.Name,
                proxy.BoneIndex,
                gate,
                hardpointId,
                proxy.IsVisible,
                proxy.Alt,
                proxy.Lod,
                proxy.AltDecreaseStayHidden,

                // Off the BONE, as `AbilityProxyPrefix.Bind` reads it - a proxy may name a system
                // that carries no prefix while riding a bone that does.
                AbilityProxyPrefix.AbilityFor(bone.Name)));
        }

        return particles;
    }

    private static (PreviewParticleGate Gate, string? HardpointId) Gate(
        string? parentBone,
        IReadOnlyDictionary<string, string> damageOwners,
        IReadOnlyDictionary<string, string> engineOwners)
    {
        if (parentBone is null)
            return (PreviewParticleGate.Always, null);

        if (damageOwners.TryGetValue(parentBone, out var damaged))
            return (PreviewParticleGate.HardpointDestroyed, damaged);

        if (engineOwners.TryGetValue(parentBone, out var engine))
            return (PreviewParticleGate.HardpointAlive, engine);

        // The engine MESH, for the models whose glow is not attached to the tagged bone at all.
        if (IsEngineMeshBone(parentBone) && engineOwners.Count > 0)
            return (PreviewParticleGate.HardpointAlive, engineOwners.Values.First());

        return (PreviewParticleGate.Always, null);
    }

    /// <summary>
    ///     Whether a bone is the engine geometry a glow proxy is attached to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         INFERRED FROM THE MODELS, not told to us by anyone who owns the engine, and worth
    ///         confirming. Every one of the eleven <c>Engine_Particles</c> tags in foc names
    ///         <c>HP_E_MAINENGINES</c>, and on three of the four capital ships measured, nothing at
    ///         all is parented to that bone - the glow sits under a mesh instead:
    ///         <c>pe_stardestroyerengines</c> under <c>engines_big</c>, <c>pe_nebulonengines</c>
    ///         under <c>engines</c>, <c>PE_Corvetteengines</c> under <c>engines</c>. The Mon Cal
    ///         carries both shapes at once - <c>pe_moncalengines_mid</c> IS under the tagged bone,
    ///         while its big and small engines are under <c>engines_big</c> and
    ///         <c>engines_small</c> - which is why both joins are needed rather than one replacing
    ///         the other.
    ///     </para>
    ///     <para>
    ///         Only ever consulted when the subject HAS an engine hardpoint that hides its glow, so
    ///         a model with no engine hardpoint keeps its effects ungated whatever its bones are called.
    ///     </para>
    /// </remarks>
    private static bool IsEngineMeshBone(string bone)
    {
        return bone.StartsWith("engines", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Which hardpoint owns each named bone.
    /// </summary>
    /// <remarks>
    ///     First one wins. Two hardpoints naming the same bone is an authoring mistake rather than a
    ///     shape to model, and the validator is where that belongs.
    /// </remarks>
    private static Dictionary<string, string> OwnersByBone(
        IReadOnlyList<PreviewHardpoint> hardpoints, Func<PreviewHardpoint, string?> boneOf)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hardpoint in hardpoints)
            if (boneOf(hardpoint) is { Length: > 0 } bone)
                owners.TryAdd(bone, hardpoint.Id);

        return owners;
    }
}
