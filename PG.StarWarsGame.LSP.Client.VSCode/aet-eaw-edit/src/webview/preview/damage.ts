// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a destroyed hardpoint looks like.
//
// This is the part no standalone tool can do: AloViewer sees one .alo and knows nothing about the
// XML, so it cannot say which smoke belongs to which hardpoint. Every rule here is read off the
// hardpoint's own tags, never guessed:
//
//   destroyed
//     |- hide  Model_To_Attach            the turret breaks off
//     |- show  every part on the Damage_Particles bone or below it
//     |- show  the Damage_Decal mesh      the scorch mark
//     |- once  Death_Explosion_Particles
//     '- if Engine_Death_Hide_Engine_Particles: hide every part on the Engine_Particles bone or below it
//
// The two bone rules are writes of each part's state switch, so they live with the unit's other state
// writers in `UnitRules`; this module only names the bones - see `damageParticleBones` and
// `engineParticleBones`. A particle's gate only names the hardpoint that owns it, for the dock.

import {
    PREVIEW_PARTICLE_GATE,
    type PreviewHardpoint,
} from "../../protocol/modelPreview";

/** The ids of the hardpoints currently blown off. */
export type Destroyed = ReadonlySet<string>;

/** Whether an attached part should be hidden, because the hardpoint holding it is gone. */
export function partHidden(
    partId: string,
    hardpoints: readonly PreviewHardpoint[],
    destroyed: Destroyed,
): boolean {
    return hardpoints.some(
        (hardpoint) =>
            hardpoint.partId === partId && destroyed.has(hardpoint.id),
    );
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
 * Whether the effect rules have a particle system playing.
 *
 * `startsVisible` has the final say HERE: an effect the model itself ships switched off - like
 * `pi_damage_elec_SD00` - does not appear just because something was destroyed. The one rule that
 * shows it is the Damage_Particles rule, for an effect on or below a destroyed hardpoint's bone, and
 * that writes over this answer in the unit's rules - see `UnitRules.setDamageParticles`.
 *
 * An ability proxy ships switched off precisely BECAUSE its ability is what turns it on, so a caller
 * that has a better answer than the default - an ability's - does not ask this.
 */
export function effectPlays(particle: EffectGate): boolean {
    return particle.startsVisible;
}

/**
 * Each hardpoint's `Damage_Particles` bone, lowercased, and whether a hardpoint naming it is destroyed.
 *
 * Every part on that bone or at any depth below it is hidden while the hardpoint lives and shown once
 * it is destroyed - see `UnitRules.setDamageParticles`. Two hardpoints naming one bone show it once
 * either is destroyed: a destruction only ever writes shown.
 */
export function damageParticleBones(
    hardpoints: readonly PreviewHardpoint[],
    destroyed: Destroyed,
): ReadonlyMap<string, boolean> {
    const bones = new Map<string, boolean>();

    for (const hardpoint of hardpoints) {
        const bone = (hardpoint.damageParticlesBone ?? "").trim().toLowerCase();

        if (bone !== "") {
            bones.set(
                bone,
                (bones.get(bone) ?? false) || destroyed.has(hardpoint.id),
            );
        }
    }

    return bones;
}

/**
 * The `Engine_Particles` bones, lowercased, of every destroyed hardpoint whose death hides them.
 *
 * Every part on such a bone or at any depth below it is hidden once the hardpoint dies - see
 * `UnitRules.setEngineParticles`. `engineDeathHidesEngineParticles` is true unless the XML denies
 * it, as the game reads the tag; a living hardpoint names nothing, because the rule writes nothing
 * until it dies.
 */
export function engineParticleBones(
    hardpoints: readonly PreviewHardpoint[],
    destroyed: Destroyed,
): ReadonlySet<string> {
    const bones = new Set<string>();

    for (const hardpoint of hardpoints) {
        const bone = (hardpoint.engineParticlesBone ?? "").trim().toLowerCase();

        if (
            bone !== "" &&
            hardpoint.engineDeathHidesEngineParticles &&
            destroyed.has(hardpoint.id)
        ) {
            bones.add(bone);
        }
    }

    return bones;
}

/**
 * The decal meshes that should be showing, lowercased for matching.
 *
 * `Damage_Decal` names a mesh that shares its bone's name - measured on `Ev_stardestroyer.alo`,
 * where `HP_F-L_Blast` is both. The file ships those meshes VISIBLE, so the engine must hide them
 * until the hardpoint is destroyed; the preview starts them hidden for the same reason.
 */
export function decalNames(
    hardpoints: readonly PreviewHardpoint[],
    destroyed: Destroyed,
): ReadonlySet<string> {
    const names = new Set<string>();

    for (const hardpoint of hardpoints) {
        const decal = hardpoint.damageDecalBone ?? "";
        if (decal !== "" && destroyed.has(hardpoint.id)) {
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
 * the eaw Star Destroyer's hardpoints do exactly that - `HP_trac_bone` and `SPAWN_00` - and hiding an
 * attach bone would prune the whole subtree standing on it, which is the hardpoint.
 */
export function collisionMeshNames(
    hardpoints: readonly PreviewHardpoint[],
): ReadonlySet<string> {
    const names = new Set<string>();

    for (const hardpoint of hardpoints) {
        const mesh = (hardpoint.collisionMeshBone ?? "").toLowerCase();

        if (
            mesh !== "" &&
            mesh !== (hardpoint.attachBone ?? "").toLowerCase()
        ) {
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
export function destroyable(
    hardpoints: readonly PreviewHardpoint[],
): PreviewHardpoint[] {
    return hardpoints.filter((hardpoint) => hardpoint.isDestroyable);
}

/** What decides whether an effect is running the moment the model opens. */
export interface OpeningEffect extends EffectGate {
    /** The particle system's name, e.g. `p_engine_glow_small`. */
    systemRef: string;
    /** The bone it is attached to. */
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
 * bones they are attached to are `ENGINE*` - and it is the only signal left for the commonest case there
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
 * Deliberately NOT the same question as {@link effectPlays}, which is the proxy's own flag. This is
 * the opening state only.
 */
export function playsOnOpen(effect: OpeningEffect): boolean {
    // The model's own switch still has the final say, exactly as it does everywhere else.
    if (!effect.startsVisible) {
        return false;
    }

    // An effect a hardpoint owns is left to that hardpoint's bone rules: an engine glow runs until
    // its hardpoint dies, damage smoke is hidden until it does - see `UnitRules`.
    if (
        effect.gate === PREVIEW_PARTICLE_GATE.hardpointAlive ||
        effect.gate === PREVIEW_PARTICLE_GATE.hardpointDestroyed
    ) {
        return true;
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
 * Whether the effect rules have an effect running: its own flag AND the opening rule, together.
 *
 * One predicate rather than two, because three call sites ask this question - attaching a system,
 * reacting to a hardpoint blowing up, and seeding the dock's rows - and any of them disagreeing
 * shows up directly as the list claiming "off" about something visibly burning.
 *
 * An ungated effect is the interesting case. It plays only if it would play on open, so a hull's
 * fires and dust stay off until they are asked for, while its engines run. A gated one is left to
 * its hardpoint's bone rules, however it is named. The destroyed hardpoints are not asked here: the
 * bone rules in `UnitRules` write over this answer.
 */
export function effectPlaysNow(effect: OpeningEffect): boolean {
    if (!effectPlays(effect)) {
        return false;
    }

    return effect.gate === PREVIEW_PARTICLE_GATE.always
        ? playsOnOpen(effect)
        : true;
}
