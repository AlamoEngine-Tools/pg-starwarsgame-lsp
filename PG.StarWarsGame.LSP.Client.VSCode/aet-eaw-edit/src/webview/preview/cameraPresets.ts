// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A camera preset: a shot that can be applied to any subject.
//
// Stated the way the ENGINE states one. Read off the shipped campaign scripts, a cinematic key is
// `Set_Cinematic_Camera_Key(object, distance, pitch, yaw, 1, 0, 0, 0)` - so distance, pitch and yaw
// in degrees, with pitch freely negative. Borrowing that vocabulary means a preset a modder builds
// here reads like the thing they already write, and can be copied straight out as one.
//
// The one deliberate difference: distance is stored as a MULTIPLE OF THE SUBJECT'S RADIUS rather
// than in world units. A preset exists to frame a roster the same way, and the corpus spans a
// trooper two units across to a command centre thousands - an absolute distance would be a
// different shot on every one of them. It is resolved back to world units at the moment it is
// written out as Lua, which is the only place the engine's own units are wanted.

import { type BoundingSphere, type Vec3 } from './framing';

/** A saved shot. */
export interface CameraPreset {
    id: string;
    name: string;

    /** How far out, as a multiple of the subject's radius. */
    distance: number;

    /** Degrees above the horizon; negative looks up from below, as the shipped keys do. */
    pitch: number;

    /** Degrees around, measured from +Z like every other angle in this panel. */
    yaw: number;

    /**
     * A bone to frame instead of the whole subject, or null for its centre.
     *
     * Reserved for the binding work; nothing sets it yet. Stored from the start so a preset saved
     * today survives the version that starts using it.
     */
    bone: string | null;
}

/** Where a preset puts the camera, and what it looks at. */
export interface PresetPose {
    position: Vec3;
    target: Vec3;
}

/** A subject with no measurable size still has to be framed from somewhere. */
const MIN_RADIUS = 0.001;

const RADIANS = Math.PI / 180;

/** Turns a saved shot into a camera position for this subject. */
export function poseFromPreset(preset: CameraPreset, sphere: BoundingSphere): PresetPose {
    const radius = Math.max(sphere.radius, MIN_RADIUS);
    const distance = Math.max(preset.distance, 0) * radius;

    const pitch = preset.pitch * RADIANS;
    const yaw = preset.yaw * RADIANS;
    const horizontal = distance * Math.cos(pitch);

    return {
        position: {
            x: sphere.center.x + horizontal * Math.sin(yaw),
            y: sphere.center.y + distance * Math.sin(pitch),
            z: sphere.center.z + horizontal * Math.cos(yaw),
        },
        target: { ...sphere.center },
    };
}

/**
 * Turns the camera's current position into a saved shot.
 *
 * The inverse of {@link poseFromPreset} for the same subject, so saving and applying land in the
 * same place - a Save button that moved the camera would be worse than no Save button.
 */
export function presetFromPose(
    name: string, position: Vec3, sphere: BoundingSphere,
): CameraPreset {
    const radius = Math.max(sphere.radius, MIN_RADIUS);
    const dx = position.x - sphere.center.x;
    const dy = position.y - sphere.center.y;
    const dz = position.z - sphere.center.z;

    const distance = Math.hypot(dx, dy, dz);
    const horizontal = Math.hypot(dx, dz);

    return {
        id: `preset-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`,
        name,
        distance: distance / radius,
        pitch: Math.atan2(dy, horizontal) / RADIANS,

        // 0 to 360, which is how the shipped keys are written - `(Pirate_Frigate2, 220, -12, 290,
        // ...)` rather than -70.
        yaw: ((Math.atan2(dx, dz) / RADIANS) % 360 + 360) % 360,
        bone: null,
    };
}

/**
 * The preset as a line of Lua.
 *
 * Distance resolved into world units, because the engine's key takes them and the preset does not
 * hold them: a key written for a trooper is not a key for a Star Destroyer.
 */
export function luaFor(
    preset: CameraPreset, sphere: BoundingSphere, objectName: string | null,
): string {
    const radius = Math.max(sphere.radius, MIN_RADIUS);
    const distance = round(Math.max(preset.distance, 0) * radius);

    // A model preview has no game object to name. An obvious hole beats a plausible wrong guess:
    // the line is still worth copying, and what has to be filled in is unmissable.
    const object = objectName === null || objectName.trim() === '' ? '<object>' : objectName.trim();

    return `Set_Cinematic_Camera_Key(${object}, ${distance}, ${round(preset.pitch)}, `
        + `${round(preset.yaw)}, 1, 0, 0, 0)`;
}

/** Two decimals at most: this is going into a script a person reads and edits. */
function round(value: number): number {
    return Math.round(value * 100) / 100;
}
