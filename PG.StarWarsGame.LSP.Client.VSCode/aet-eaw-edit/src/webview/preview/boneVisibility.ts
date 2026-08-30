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

        if (typeof name !== 'string' || typeof fps !== 'number' || !(fps > 0)) {
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
            if (typeof bits === 'string' && bits.length > 0) {
                byNode.set(node, bits);
            }
        }

        if (byNode.size > 0) {
            tracks.set(name, { fps, bones: byNode });
        }
    }

    return tracks;
}

/**
 * Whether a bone is hidden at a moment in a clip.
 *
 * The frame is FLOORED, not rounded: a frame holds until the next one begins, which is both what the
 * engine does and what stops a bone flickering on for half a frame at every boundary. Times outside
 * the clip clamp to its ends, which is what a clamped last frame needs.
 */
export function hiddenAt(bits: string, fps: number, seconds: number): boolean {
    if (bits.length === 0) {
        return false;
    }

    const frame = Math.min(bits.length - 1, Math.max(0, Math.floor(seconds * fps)));

    return bits[frame] !== '1';
}

function record(value: unknown): Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
        ? value as Record<string, unknown>
        : {};
}
