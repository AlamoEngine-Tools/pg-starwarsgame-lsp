// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The wreckage a hardpoint sheds when it is shot off.
//
// 167 of foc's 355 hardpoints name a `Death_Breakoff_Prop`, 147 distinct - each a SpaceProp with its
// own model and debris behaviour. The server has resolved them since H1; nothing on the client read
// them, so destroying a hardpoint made it vanish and drop nothing.
//
// Kept free of three.js so the motion can be read against the XML it comes from.

import type {
    PreviewBreakoffProp,
    PreviewHardpoint,
    PreviewVector3,
} from "../../protocol/modelPreview";

/** A rotation as a unit quaternion, kept as plain numbers so this file stays free of three.js. */
export interface BreakoffOrientation {
    x: number;
    y: number;
    z: number;
    w: number;
}

/** Where a piece of debris is and how it stands, in the scene's axes. */
export interface BreakoffPose {
    /** How far it has drifted from where the hardpoint was. */
    offset: PreviewVector3;
    /** How it stands: the ship's orientation, turned by what the prop has added to its facing. */
    orientation: BreakoffOrientation;
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
    hullPartId: string,
    hardpoint: PreviewHardpoint,
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
    hardpoint: PreviewHardpoint,
    props: readonly PreviewBreakoffProp[],
): PreviewBreakoffProp | null {
    const wanted = (hardpoint.deathBreakoffProp ?? "").trim().toLowerCase();
    if (wanted === "") {
        return null;
    }

    const prop = props.find(
        (candidate) => candidate.id.toLowerCase() === wanted,
    );

    return prop === undefined || !prop.resolved || (prop.modelRef ?? "") === ""
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
 * Where the debris is and how it stands, `seconds` after it broke off.
 *
 * MEASURED in the game: every logic frame it adds `Debris_Movement_Vector` to the wreck's position
 * and `Debris_Facing_Rotate_Vector`, in degrees, to the wreck's three facing angles. Reading the
 * two as per second is what made every wreck crawl: all 90 shipped props declare magnitudes
 * between 0.42 and 0.71, so a piece of a 600-unit hull would drift about ten units over its entire
 * twenty-second life and read as stationary.
 *
 * THE DRIFT IS IN WORLD AXES. The game turns the vector by nothing - not by the ship's heading, not
 * by the bone the hardpoint hung on - so the same prop leaves a ship outwards at one heading and
 * inwards at the opposite one. A preview has to stand its ship at SOME heading, and it stands it at
 * facing 0, the game's own zero: such a ship heads along world +X with its left on world +Y. In the
 * scene the bow is +Z, the left is +X and up is +Y, so a world `(x, y, z)` arrives as `(y, z, x)`.
 * Applied in the model's own axes instead, which is the game at a heading of 270, the Star
 * Destroyer's four weapon wrecks all slid across the hull towards its centreline.
 *
 * THE TURN IS THREE ANGLES, NOT AN AXIS. See `turnOf`.
 *
 * A prop declaring no vector simply stays where the hardpoint was, which is ordinary authoring.
 */
export function breakoffPose(
    prop: PreviewBreakoffProp,
    seconds: number,
): BreakoffPose {
    const frames = seconds * FRAMES_PER_SECOND;
    const drift = scaled(prop.movementVector, frames);

    return {
        offset: { x: drift.y, y: drift.z, z: drift.x },
        orientation: multiplied(
            SHIP_IN_SCENE,
            turnOf(scaled(prop.facingRotateVector, frames)),
        ),
    };
}

/**
 * How a ship at facing 0 stands in the scene: the exporter's turn of Alamo's up axis, Z, onto the
 * scene's, Y, and nothing else. A quarter turn back about X.
 *
 * It is the WRECK's orientation at the moment it breaks off, because the game creates a wreck with
 * its parent's facing - not with the orientation of the bone its hardpoint hung on.
 */
const SHIP_IN_SCENE: BreakoffOrientation = aboutAxis("x", -90);

/**
 * The wreck's turn away from the ship's orientation, in the wreck's own file axes, for facing
 * angles of `degrees`.
 *
 * MEASURED in the game: an object's facing is three angles, applied as a roll about its heading
 * (the X angle), then a pitch about its left (the Y angle), then a yaw about up (the Z angle). A
 * model file has its bow down -Y and its left on +X, so in the file's own axes the roll is about
 * -Y, the pitch about +X and the yaw about +Z.
 *
 * The rates used to be turned into the scene's axes and then applied in the wreck's OWN, which are
 * Alamo's: the Y and Z rates changed places, and a wreck told to yaw flat tumbled end over end.
 */
function turnOf(degrees: PreviewVector3): BreakoffOrientation {
    return multiplied(
        multiplied(aboutAxis("z", degrees.z), aboutAxis("x", degrees.y)),
        aboutAxis("y", -degrees.x),
    );
}

function aboutAxis(
    axis: "x" | "y" | "z",
    degrees: number,
): BreakoffOrientation {
    const half = (degrees * Math.PI) / 360;
    const orientation = { x: 0, y: 0, z: 0, w: Math.cos(half) };
    orientation[axis] = Math.sin(half);

    return orientation;
}

/** `first` applied after `second`, as with matrices: the product turns by `second`, then by `first`. */
function multiplied(
    first: BreakoffOrientation,
    second: BreakoffOrientation,
): BreakoffOrientation {
    return {
        x:
            first.w * second.x +
            first.x * second.w +
            first.y * second.z -
            first.z * second.y,
        y:
            first.w * second.y -
            first.x * second.z +
            first.y * second.w +
            first.z * second.x,
        z:
            first.w * second.z +
            first.x * second.y -
            first.y * second.x +
            first.z * second.w,
        w:
            first.w * second.w -
            first.x * second.x -
            first.y * second.y -
            first.z * second.z,
    };
}

function scaled(
    vector: PreviewVector3 | null | undefined,
    by: number,
): PreviewVector3 {
    if (vector === null || vector === undefined) {
        return { x: 0, y: 0, z: 0 };
    }

    // `+ 0` normalises the negative zero that -20 * 0 produces. It is never a meaningful offset,
    // and it reads as a real value everywhere it surfaces.
    return { x: vector.x * by + 0, y: vector.y * by + 0, z: vector.z * by + 0 };
}
