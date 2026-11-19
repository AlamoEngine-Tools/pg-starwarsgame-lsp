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
// These are writes of each part's state switch, so they live with the unit's other state writers in
// `UnitRules`; this module only names what each hardpoint writes, and in which order the hardpoints
// died - see `hardpointWrites`. A particle's gate only names the hardpoint that owns it, for the dock.

import {
    PREVIEW_PARTICLE_GATE,
    type PreviewHardpoint,
} from "../../protocol/modelPreview";

/**
 * The ids of the hardpoints currently blown off, in the ORDER they died: a set's iteration order is
 * its insertion order, and {@link withDestroyed} only ever appends a death and removes a repair.
 *
 * The order matters because a death writes the state switch of the parts its bones reach, and the
 * last write wins - where two hardpoints' bones overlap, the later death decides. Every way the
 * hardpoints come back empties it: a repair takes its hardpoint out, Repair all and a starting clip
 * take every one out, and a new scene starts from the subject's state as the panel opened it.
 */
export type Destroyed = ReadonlySet<string>;

/** The destroyed hardpoints after one is destroyed or repaired: a death goes last. */
export function withDestroyed(
    current: Destroyed,
    id: string,
    destroyed: boolean,
): Destroyed {
    if (current.has(id) === destroyed) {
        return current;
    }

    const next = new Set(current);

    if (destroyed) {
        next.add(id);
    } else {
        next.delete(id);
    }

    return next;
}

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
 * that writes over this answer in the unit's rules - see `UnitRules.setHardpoints`.
 *
 * An ability proxy ships switched off precisely BECAUSE its ability is what turns it on, so a caller
 * that has a better answer than the default - an ability's - does not ask this.
 */
export function effectPlays(particle: EffectGate): boolean {
    return particle.startsVisible;
}

/** What one hardpoint's death writes, lowercased for matching; null where it writes nothing. */
export interface HardpointDeath {
    /** The `Damage_Decal` part it shows. */
    decal: string | null;
    /** The `Damage_Particles` bone whose parts, at any depth, it shows. */
    damageBone: string | null;
    /** The `Engine_Particles` bone whose parts, at any depth, it hides - only with the flag. */
    engineBone: string | null;
}

/**
 * What the hardpoints write to the state switch of the unit's parts: on creation, and in the order
 * they died.
 */
export interface HardpointWrites {
    /**
     * Every `Damage_Decal`, hidden when the unit's type loads. `Damage_Decal` names a mesh that
     * shares its bone's name - measured on `Ev_stardestroyer.alo`, where `HP_F-L_Blast` is both -
     * and the file ships those meshes VISIBLE.
     */
    decals: ReadonlySet<string>;
    /** Every `Damage_Particles` bone, whose parts at any depth are hidden when its hardpoint is created. */
    damageBones: ReadonlySet<string>;
    /** Each destroyed hardpoint's death, in the order they died. */
    deaths: readonly HardpointDeath[];
}

/** Lowercased and trimmed, or null for a tag the hardpoint does not write. */
function named(value: string | null | undefined): string | null {
    const name = (value ?? "").trim().toLowerCase();
    return name === "" ? null : name;
}

/**
 * What the hardpoints write - see {@link HardpointWrites}. The deaths follow `destroyed`, which
 * holds them in the order they happened.
 *
 * Within one death the game writes in this order: the decal shown, the `Damage_Particles` parts
 * shown, then - with `Engine_Death_Hide_Engine_Particles`, true unless the XML denies it - the
 * `Engine_Particles` parts hidden. See `UnitRules.setHardpoints` for how they are replayed.
 */
export function hardpointWrites(
    hardpoints: readonly PreviewHardpoint[],
    destroyed: Destroyed,
): HardpointWrites {
    const byId = new Map(hardpoints.map((h) => [h.id, h]));
    const decals = new Set<string>();
    const damageBones = new Set<string>();

    for (const hardpoint of hardpoints) {
        const decal = named(hardpoint.damageDecalBone);
        const bone = named(hardpoint.damageParticlesBone);

        if (decal !== null) {
            decals.add(decal);
        }

        if (bone !== null) {
            damageBones.add(bone);
        }
    }

    const deaths: HardpointDeath[] = [];

    for (const id of destroyed) {
        const hardpoint = byId.get(id);

        if (hardpoint !== undefined) {
            deaths.push({
                decal: named(hardpoint.damageDecalBone),
                damageBone: named(hardpoint.damageParticlesBone),
                engineBone: hardpoint.engineDeathHidesEngineParticles
                    ? named(hardpoint.engineParticlesBone)
                    : null,
            });
        }
    }

    return { decals, damageBones, deaths };
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
 *
 * In the order the unit declares its hardpoints, which is the order Destroy all kills them in. The
 * game's order for hardpoints dying at the same moment is not known; the preview picks this one.
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
