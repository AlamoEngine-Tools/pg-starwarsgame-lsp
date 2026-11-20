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

import { type PreviewHardpoint } from "../../protocol/modelPreview";

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

/** What one hardpoint's death writes, lowercased for matching; null where it writes nothing. */
export interface HardpointDeath {
    /** The hardpoint that died, which is what places its death on the unit's timeline. */
    hardpoint: string;
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
                hardpoint: id,
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

/**
 * Whether an effect is running before the unit's rules write over it: the proxy's own flag.
 *
 * Measured in the game: a unit is created with every attached effect running that no switch hides.
 * The switches are the unit's rules - the name groups (`pte`, `pptw` and `prs` hidden on creation,
 * the abilities' groups), the hardpoints' bones, the cloak - and they write over this answer in
 * `UnitRules`. The preview used to hold back every effect that did not look like an engine on top
 * of that; the game holds back none.
 *
 * One predicate, because three call sites ask this question - attaching a system, reacting to a
 * hardpoint blowing up, and seeding the dock's rows - and any of them disagreeing shows up directly
 * as the list claiming "off" about something visibly burning.
 */
export function effectPlaysNow(effect: EffectGate): boolean {
    return effect.startsVisible;
}
