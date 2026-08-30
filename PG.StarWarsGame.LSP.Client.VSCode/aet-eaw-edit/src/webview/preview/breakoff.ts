// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The wreckage a hardpoint sheds when it is shot off.
//
// 167 of foc's 355 hardpoints name a `Death_Breakoff_Prop`, 147 distinct - each a SpaceProp with its
// own model and debris behaviour. The server has resolved them since H1; nothing on the client read
// them, so destroying a hardpoint made it vanish and drop nothing.
//
// Kept free of three.js so the motion can be read against the XML it comes from.

import type { PreviewBreakoffProp, PreviewHardpoint, PreviewVector3 } from '../../protocol/modelPreview';

/** Where a piece of debris is, relative to the hardpoint it came off. */
export interface BreakoffPose {
    offset: PreviewVector3;
    /** Degrees about each axis. */
    rotation: PreviewVector3;
}

/** Where a piece of wreckage is dropped: a part, or one named bone of a part. */
export interface BreakoffAnchor {
    partId: string;
    /** Absent when the PART itself is the place - which is the normal case. See `breakoffAnchor`. */
    bone?: string;
    boneIndex?: number;
}

/**
 * Where a destroyed hardpoint's wreckage is dropped.
 *
 * On the HARDPOINT, whenever the hardpoint has one. The hardpoint part is already standing on the hull bone
 * the hardpoint names, so its root is that place by construction - and the wreck is the hardpoint's
 * replacement, so "where the hardpoint was" is literally the question being asked.
 *
 * Asking for the bone by NAME inside the hardpoint is what this replaces, and it was wrong in a way
 * that only one kind of model reveals. An Executor hardpoint model carries a copy of the WHOLE hull
 * skeleton - 228 nodes, `HP_L_Trb00_Bone` among them - so the name resolved a SECOND time, inside a
 * part already standing on that bone, and the offset was applied twice:
 * `HP_EXECUTOR_LEFT_TURBO_00` dropped its wreck 2093 units from the hardpoint it came off, on a hull
 * whose bones are thousands of units from the origin. A Star Destroyer's hardpoint carries 22 nodes and
 * no such bone, so the lookup MISSED and the silent fallback - the part's own root - happened to be
 * the right answer. The bug was invisible on every ship whose parts do not carry the hull's bones,
 * and its size is the bone's distance from the origin.
 */
export function breakoffAnchor(
    hullPartId: string, hardpoint: PreviewHardpoint,
): BreakoffAnchor {
    if (hardpoint.partId !== null && hardpoint.partId !== undefined) {
        return { partId: hardpoint.partId };
    }

    // No hardpoint model - 32 of foc's hardpoints are like this, a tractor beam or a fighter bay that
    // names a bone and nothing to stand on it. Then the hull's own bone is the place.
    return hardpoint.attachBone === null || hardpoint.attachBone === undefined
        ? { partId: hullPartId }
        : { partId: hullPartId, bone: hardpoint.attachBone };
}

/**
 * The prop a destroyed hardpoint drops, or null when it drops nothing.
 *
 * Null for the 188 hardpoints that name none - they simply vanish, which is what the engine does -
 * and for one whose prop is named but never defined. The scene still LISTS an unresolved prop, so
 * the author sees the typo; there is just no model to drop.
 */
export function breakoffFor(
    hardpoint: PreviewHardpoint, props: readonly PreviewBreakoffProp[],
): PreviewBreakoffProp | null {
    const wanted = (hardpoint.deathBreakoffProp ?? '').trim().toLowerCase();
    if (wanted === '') {
        return null;
    }

    const prop = props.find(candidate => candidate.id.toLowerCase() === wanted);

    return prop === undefined || !prop.resolved || (prop.modelRef ?? '') === ''
        ? null
        : prop;
}

/** A lifetime with nothing declared still has to end, or the debris field grows without bound. */
const DEFAULT_LIFETIME_SECONDS = 20;

/**
 * How long the debris lasts, in seconds.
 *
 * The MIDPOINT of the declared range. The engine randomises between the two ends; a preview picks
 * the middle instead, because a debris field that lasted a different time on every run would make
 * two looks at the same hardpoint disagree, and there is nothing to be learned from the dice.
 */
export function breakoffLifetime(prop: PreviewBreakoffProp): number {
    const min = prop.minLifetimeSeconds ?? null;
    const max = prop.maxLifetimeSeconds ?? null;

    if (min !== null && max !== null) {
        return (min + max) / 2;
    }

    return min ?? max ?? DEFAULT_LIFETIME_SECONDS;
}

/**
 * How long one Alamo logic frame lasts, in seconds.
 *
 * MEASURED rather than assumed: `CONST_FRAME_TIME` is an `S_CONSTANT` record in the shipped debug
 * `StarWarsI.pdb`, carrying the float `0.03333` - a 30 Hz simulation.
 */
export const ALAMO_FRAME_SECONDS = 0.03333;

/** Logic frames in one second, which is what a per-frame rate has to be multiplied by. */
const FRAMES_PER_SECOND = 1 / ALAMO_FRAME_SECONDS;

/**
 * Where the debris has drifted to, `seconds` after it broke off.
 *
 * `Debris_Movement_Vector` and `Debris_Facing_Rotate_Vector` are per LOGIC FRAME, not per second.
 * Reading them as per-second is what made every wreck crawl: all 90 shipped props declare
 * magnitudes between 0.42 and 0.71, so a piece of a 600-unit hull would drift about ten units over
 * its entire twenty-second life and read as stationary.
 *
 * The unit is INFERRED, and the inference is worth stating. What is measured is the frame length
 * above, and that the engine's own accessors for data-driven rates of this kind are named for the
 * frame - `GameObjectTypeClass::Get_Projectile_Acceleration_Per_Frame`,
 * `HardPointDataClass::Get_Repair_Amount_Per_Frame` - and that a debris lifetime is stored as an
 * expiration FRAME (`DEBRIS_LIFETIME_EXPIRATION_FRAME_MICRO_CHUNK`), so seconds are converted to
 * frames somewhere. The same reading applies to `Max_Speed`, where it is the only one that makes
 * sense of a turbolaser bolt declaring 25 against 2200 units of flight: 88 seconds per shot as
 * units-per-second, 2.9 as units-per-frame.
 *
 * Both are in ALAMO axes, where Z is up, and the scene runs in the model's glTF space, where Y is -
 * so both are turned by `(x, y, z) -> (x, z, -y)` on the way out. Applied raw, a piece told to fall
 * DOWNWARD slid sideways instead, and one told to tumble end over end spun about the wrong axis.
 * The same turn is what `accelerationIn` exists for on the particle side.
 *
 * A prop declaring no vector simply stays where the hardpoint was, which is ordinary authoring.
 */
export function breakoffPose(prop: PreviewBreakoffProp, seconds: number): BreakoffPose {
    const frames = seconds * FRAMES_PER_SECOND;

    return {
        offset: intoSceneAxes(scaled(prop.movementVector, frames)),
        rotation: intoSceneAxes(scaled(prop.facingRotateVector, frames)),
    };
}

/**
 * An Alamo vector in the scene's axes.
 *
 * Right for a rotation as well as a translation: the map is a rigid change of basis, so a spin
 * about Alamo Z - the up axis, a flat yaw - comes out as a spin about glTF Y, which is also flat.
 */
function intoSceneAxes(vector: PreviewVector3): PreviewVector3 {
    return { x: vector.x, y: vector.z, z: -vector.y + 0 };
}

function scaled(vector: PreviewVector3 | null | undefined, by: number): PreviewVector3 {
    if (vector === null || vector === undefined) {
        return { x: 0, y: 0, z: 0 };
    }

    // `+ 0` normalises the negative zero that -20 * 0 produces. It is never a meaningful offset,
    // and it reads as a real value everywhere it surfaces.
    return { x: vector.x * by + 0, y: vector.y * by + 0, z: vector.z * by + 0 };
}
