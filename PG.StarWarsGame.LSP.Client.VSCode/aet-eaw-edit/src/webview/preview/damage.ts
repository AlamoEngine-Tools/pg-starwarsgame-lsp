// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a destroyed hardpoint looks like.
//
// This is the part no standalone tool can do: AloViewer sees one .alo and knows nothing about the
// XML, so it cannot say which smoke belongs to which mount. Every rule here is read off the
// hardpoint's own tags, never guessed:
//
//   destroyed
//     |- hide  Model_To_Attach            the turret breaks off
//     |- show  the proxies under Damage_Particles
//     |- show  the Damage_Decal mesh      the scorch mark
//     |- once  Death_Explosion_Particles
//     '- if Engine_Death_Hide_Engine_Particles: put the engine glow out

import { PREVIEW_PARTICLE_GATE, type PreviewHardpoint } from '../../protocol/modelPreview';

/** The ids of the hardpoints currently blown off. */
export type Destroyed = ReadonlySet<string>;

/** Whether a mounted part should be hidden, because the hardpoint holding it is gone. */
export function partHidden(
    partId: string, hardpoints: readonly PreviewHardpoint[], destroyed: Destroyed,
): boolean {
    return hardpoints.some(
        hardpoint => hardpoint.partId === partId && destroyed.has(hardpoint.id));
}

/**
 * What decides whether an effect plays: the gate, its owner, and the model's own flag.
 *
 * Structural on purpose. Both a single {@link PreviewParticle} and a whole dock row satisfy it, so
 * the list and the viewport cannot drift into disagreeing about what is lit.
 */
export interface EffectGate {
    /** See `PREVIEW_PARTICLE_GATE`. */
    gate: string;
    hardpointId?: string | null;
    startsVisible: boolean;
}

/**
 * Whether a particle system is playing.
 *
 * `startsVisible` always has the final say: an effect the model itself ships switched off - like
 * `pi_damage_elec_SD00` - must not appear just because something was destroyed.
 */
export function effectPlays(particle: EffectGate, destroyed: Destroyed): boolean {
    if (!particle.startsVisible) {
        return false;
    }

    const owner = particle.hardpointId ?? null;

    if (particle.gate === PREVIEW_PARTICLE_GATE.hardpointDestroyed) {
        return owner !== null && destroyed.has(owner);
    }

    if (particle.gate === PREVIEW_PARTICLE_GATE.hardpointAlive) {
        return owner === null || !destroyed.has(owner);
    }

    return true;
}

/**
 * The decal meshes that should be showing, lowercased for matching.
 *
 * `Damage_Decal` names a mesh that shares its bone's name - measured on `Ev_stardestroyer.alo`,
 * where `HP_F-L_Blast` is both. The file ships those meshes VISIBLE, so the engine must hide them
 * until the mount is destroyed; the preview starts them hidden for the same reason.
 */
export function decalNames(
    hardpoints: readonly PreviewHardpoint[], destroyed: Destroyed,
): ReadonlySet<string> {
    const names = new Set<string>();

    for (const hardpoint of hardpoints) {
        const decal = hardpoint.damageDecalBone ?? '';
        if (decal !== '' && destroyed.has(hardpoint.id)) {
            names.add(decal.toLowerCase());
        }
    }

    return names;
}

/**
 * The hardpoints worth offering a destroy button for.
 *
 * The rest are still listed, marked as indestructible rather than quietly inert - a shield generator
 * that cannot be shot off is a fact the author wants to see.
 */
export function destroyable(hardpoints: readonly PreviewHardpoint[]): PreviewHardpoint[] {
    return hardpoints.filter(hardpoint => hardpoint.isDestroyable);
}

/** What decides whether an effect is running the moment the model opens. */
export interface OpeningEffect extends EffectGate {
    /** The particle system's name, e.g. `p_engine_glow_small`. */
    systemRef: string;
    /** The bone it hangs off. */
    bone: string;
    /** Damage state the proxy is tagged for, from its `_ALT<n>` suffix, or null when untagged. */
    alt?: number | null;
    /** Detail level, same convention. */
    lod?: number | null;
}

/**
 * Names that mark a proxy as engine glow when nothing else does.
 *
 * A bare `.alo` has no XML behind it, so no hardpoint gates its engines and every proxy arrives as
 * `Always`. The naming is a firm convention in the shipped data - proxies are `p_engine_*` and the
 * bones they hang off are `ENGINE*` - and it is the only signal left for the commonest case there
 * is, a fighter opened on its own.
 */
const ENGINE_NAME = /engine/i;

/**
 * Whether an effect is playing when the model first appears.
 *
 * A model opens QUIET, showing what an intact one is actually doing: engines lit, nothing else.
 * Playing everything meant a capital ship opened inside its own damage fires and dust plumes, and
 * the one-shot systems had finished before the reader could look at them. Everything held back
 * here is still one tick away in the tree.
 *
 * Deliberately NOT the same question as {@link effectPlays}, which answers what a given damage
 * state implies. This is the opening state only.
 */
export function playsOnOpen(effect: OpeningEffect): boolean {
    // The model's own switch still has the final say, exactly as it does everywhere else.
    if (!effect.startsVisible) {
        return false;
    }

    // The engine gate, assigned by the server wherever a hardpoint's `Engine_Particles` names the
    // bone. Anything gated on DESTRUCTION is off by definition - the model opens undamaged.
    if (effect.gate === PREVIEW_PARTICLE_GATE.hardpointAlive) {
        return true;
    }

    if (effect.gate === PREVIEW_PARTICLE_GATE.hardpointDestroyed) {
        return false;
    }

    // A LEVEL-tagged proxy answers to the ALT/LOD gate and to nothing here. It is already hidden at
    // ALT 0, so the model still opens quiet - and holding it back on top of that made the ALT
    // control do nothing on every model whose damage states are carried by proxies rather than by
    // meshes, which is most of them.
    if ((effect.alt ?? null) !== null || (effect.lod ?? null) !== null) {
        return true;
    }

    return ENGINE_NAME.test(effect.systemRef) || ENGINE_NAME.test(effect.bone);
}

/**
 * Whether an effect is running right now: the damage state AND the opening rule, together.
 *
 * One predicate rather than two, because three call sites ask this question - attaching a system,
 * reacting to a hardpoint blowing up, and seeding the dock's rows - and any of them disagreeing
 * shows up directly as the list claiming "off" about something visibly burning.
 *
 * An ungated effect is the interesting case. It plays only if it would play on open, so a hull's
 * fires and dust stay off until they are asked for, while its engines run. A gated one is left to
 * {@link effectPlays}: damage smoke lights when its hardpoint dies, however it is named.
 */
export function effectPlaysNow(effect: OpeningEffect, destroyed: Destroyed): boolean {
    if (!effectPlays(effect, destroyed)) {
        return false;
    }

    return effect.gate === PREVIEW_PARTICLE_GATE.always ? playsOnOpen(effect) : true;
}
