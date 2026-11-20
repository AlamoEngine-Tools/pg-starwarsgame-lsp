// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Bones an animation hides.
//
// An Alamo animation can switch a bone off per frame, and half the shipped corpus does: 662 of the
// 1363 animations hide at least one bone, across 4052 bone tracks. 2285 of those tracks are particle
// proxies, so the dominant use is TIMING AN EFFECT - the rancor's death explosion is attached to
// `P_ATST_Die`, which is hidden for all 61 frames of `attack_00` and visible for 35 of the 61 frames
// of `die_00`. Ignore the track and every effect on the model fires from frame zero of every clip,
// which is what the preview used to do.
//
// glTF has no visibility channel, so the exporter writes the tracks as extras on the animation:
//
//     "extras": { "alamoVisibility": { "fps": 30, "bones": { "P_ATST_Die#12": "0011" } } }
//
// One character per frame. Keys are glTF node names, which carry the `#index` suffix that makes a
// repeated bone name unique.

import { type BoneId, resolveBoneId } from "./boneIds";
import type * as THREE from "three";
import { type AnimationSwitch } from "./visibility";

/** One clip's tracks. */
export interface VisibilityTrack {
    fps: number;
    /** Node name to one character per frame: `1` visible, anything else hidden. */
    bones: Map<string, string>;
}

/**
 * Reads every clip's tracks out of a parsed glTF's JSON.
 *
 * Nothing here throws. The extras are written by this project's own exporter, but they arrive as
 * untyped JSON across a webview boundary, and one malformed clip must cost that clip rather than the
 * whole preview.
 */
export function visibilityTracks(json: unknown): Map<string, VisibilityTrack> {
    const tracks = new Map<string, VisibilityTrack>();
    const animations = record(json).animations;

    if (!Array.isArray(animations)) {
        return tracks;
    }

    for (const animation of animations) {
        const entry = record(animation);
        const name = entry.name;
        const track = record(record(entry.extras).alamoVisibility);
        const fps = track.fps;
        const bones = record(track.bones);

        if (typeof name !== "string" || typeof fps !== "number" || !(fps > 0)) {
            continue;
        }

        // Filed under the CANONICAL BONE ID, exactly as the extras carry it - `Name#index`, where
        // the index is the ALO skeleton index the ALA itself references. Never under the stripped
        // name: two bones can share a name, and stripping throws away the only thing that separates
        // them. Callers holding a bare name resolve it through `boneIds.resolveBoneId`.
        //
        // Both spellings have been tried here, and each fixed one consumer while breaking the
        // other. The id is the identity; the name is for reading.
        const byNode = new Map<string, string>();
        for (const [node, bits] of Object.entries(bones)) {
            if (typeof bits === "string" && bits.length > 0) {
                byNode.set(node, bits);
            }
        }

        if (byNode.size > 0) {
            tracks.set(name, { fps, bones: byNode });
        }
    }

    return tracks;
}

/** How far short of a frame a playhead may be and still count as on it, in frames. */
const FRAME_SLACK = 1e-3;

/**
 * Whether a bone is hidden at a moment in a clip.
 *
 * The frame is FLOORED, not rounded: a frame holds until the next one begins, which is both what the
 * engine does and what stops a bone flickering on for half a frame at every boundary. Times outside
 * the clip clamp to its ends, which is what a clamped last frame needs.
 *
 * Floored with a thousandth of a frame to spare. Keyframe times reach here as 32-bit floats, and
 * many land a hair short of their frame - 17/30 does - so a playhead resting exactly on a frame, as a
 * finished clip's does, read the frame before it.
 */
export function hiddenAt(bits: string, fps: number, seconds: number): boolean {
    if (bits.length === 0) {
        return false;
    }

    const frame = Math.min(
        bits.length - 1,
        Math.max(0, Math.floor(seconds * fps + FRAME_SLACK)),
    );

    return bits[frame] !== "1";
}

/**
 * What one clip's visibility track says about one bone at a moment: true for shown, false for
 * hidden, undefined when the clip has no track for that bone.
 *
 * The track is keyed by CANONICAL id, and so is the question; `resolveBoneId` is the fallback for a
 * bone asked for by its bare name. A bone the clip has no track for is SILENT, which is not hidden:
 * while the clip runs, nothing hides that bone's parts.
 */
export function trackSays(
    track: VisibilityTrack | undefined,
    id: BoneId | undefined,
    seconds: number,
): boolean | undefined {
    if (id === undefined || track === undefined) {
        return undefined;
    }

    const key = track.bones.has(id)
        ? id
        : resolveBoneId(track.bones.keys(), id);
    const bits = key === undefined ? undefined : track.bones.get(key);

    return bits === undefined ? undefined : !hiddenAt(bits, track.fps, seconds);
}

/** One clip running on a model, at its own playhead. */
export interface RunningClip {
    track: VisibilityTrack | undefined;
    seconds: number;
    /** An overlay plays over the clips under it rather than blending with them. */
    overlay?: boolean;
    /**
     * Whether the clip has played through and holds its end - see `clipEnd.ts`. A finished clip
     * still speaks for its held frame; a finished OVERLAY is dropped.
     */
    done?: boolean;
}

/**
 * What every clip running on a model says about one bone together.
 *
 * Measured in the game: two blended clips show a part if either shows it, and an overlay clip
 * overrides the clips under it wherever it has a track, until it is done - then it is dropped and
 * the clips under it decide again. Undefined when no running clip speaks for the bone. The preview
 * runs one clip at a time today, so the list has one entry.
 */
export function clipsSay(
    running: readonly RunningClip[],
    id: BoneId | undefined,
): boolean | undefined {
    let under: boolean | undefined;
    let over: boolean | undefined;

    for (const clip of running) {
        if (clip.overlay === true && clip.done === true) {
            continue;
        }

        const said = trackSays(clip.track, id, clip.seconds);

        if (said === undefined) {
            continue;
        }

        if (clip.overlay === true) {
            over = said;
        } else {
            under = (under ?? false) || said;
        }
    }

    return over ?? under;
}

/**
 * The animation switch while NO clip runs: the model stands in its base pose, where the model file's
 * own visible flag on the part's bone decides. Hidden for a bone the file hides; silent, which is
 * shown, for every other one - and for every bone once a clip runs, which is the clip's to decide.
 *
 * Not the state switch: a file-hidden bone never stops a clip from showing its parts.
 */
export function poseSays(
    bone: THREE.Object3D | undefined,
): AnimationSwitch | undefined {
    return bone?.userData.alamoBoneHidden === true
        ? { shown: false, source: "pose" }
        : undefined;
}

function record(value: unknown): Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value)
        ? (value as Record<string, unknown>)
        : {};
}
