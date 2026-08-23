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
    // The proxy's own default. Separate from the damage question below, and separable: an ability
    // proxy ships switched off precisely BECAUSE its ability is what turns it on, so a caller that
    // has a better answer than the default asks `hardpointGateAllows` instead.
    if (!particle.startsVisible) {
        return false;
    }

    return hardpointGateAllows(particle, destroyed);
}

/**
 * The DAMAGE half of the question alone: is the mount this effect belongs to on the right side of
 * its gate?
 *
 * Without the `startsVisible` default, which is a different question. An ability proxy - and
 * `prs_at-aa_fx` on the real AT-AA is one - ships `startsVisible: false`, so reading that as a veto
 * meant no ability could ever light its own effect however many times its row was ticked. The
 * damage gate still applies either way: smoke that belongs to a destroyed mount must not appear
 * just because an ability is on.
 */
export function hardpointGateAllows(particle: EffectGate, destroyed: Destroyed): boolean {
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
 * The collision hulls the hardpoints declare, lowercased for matching.
 *
 * The engine consumes a <c>Collision_Mesh</c> for hit testing and never draws it, in EITHER damage
 * state - which is what separates it from a decal, and why nothing here consults the destroyed set.
 *
 * Most of this geometry is already switched off without any help: measured on the shipped models,
 * 186 of 187 collision hulls are marked hidden in the file and the material rules gate the rest off
 * by name. This covers the one that is not, and the mod that ships its hulls visible - a hardpoint
 * saying "this mesh is my collision hull" is the author's own word, and it outranks a guess made
 * from a name.
 *
 * A hardpoint that gives `Collision_Mesh` the same value as its `Attachment_Bone` is skipped. Two of
 * the eaw Star Destroyer's mounts do exactly that - `HP_trac_bone` and `SPAWN_00` - and hiding an
 * attach bone would prune the whole subtree standing on it, which is the mount.
 */
export function collisionMeshNames(
    hardpoints: readonly PreviewHardpoint[],
): ReadonlySet<string> {
    const names = new Set<string>();

    for (const hardpoint of hardpoints) {
        const mesh = (hardpoint.collisionMeshBone ?? '').toLowerCase();

        if (mesh !== '' && mesh !== (hardpoint.attachBone ?? '').toLowerCase()) {
            names.add(mesh);
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

