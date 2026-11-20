// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a clip ends, and what it holds once it has.
//
// Measured in the game: a clip that does not loop advances to frame count - 2, is then DONE, and holds
// that frame - pose and visibility alike; the clip stays the one running. A looping clip wraps at
// frame count - 1. The last frame of every clip is the loop's copy of the first, so a clip at rest never
// shows it. An overlay clip that is done is dropped, and the clips under it decide again.

import * as THREE from "three";

import { type RunningClip } from "./boneVisibility";
import { type SubjectAnimation } from "./subjectAnimation";

/** Each clip's play-once copy, and each copy's clip, so a loop change can swap one for the other. */
const ONCE = new WeakMap<THREE.AnimationClip, THREE.AnimationClip>();
const LOOPED = new WeakMap<THREE.AnimationClip, THREE.AnimationClip>();

/**
 * The time of a clip's second-to-last frame: where it comes to rest when it does not loop.
 *
 * Read off the keys, not computed from a rate: the exporter writes a key on every frame, so the
 * second-to-last key IS that frame. Zero for a clip of one frame or none.
 */
export function heldEnd(clip: THREE.AnimationClip): number {
    let end = 0;

    for (const track of clip.tracks) {
        if (track.times.length >= 2) {
            end = Math.max(end, track.times[track.times.length - 2]);
        }
    }

    return end;
}

/**
 * The clip as it plays when it does not loop: the same tracks, ending on its held frame.
 *
 * A copy rather than a change to the clip, because the loop is the reader's to switch and the same
 * clip has to wrap at its last frame again the moment it is. Made once per clip; the tracks are
 * shared, not copied.
 */
export function playedOnce(clip: THREE.AnimationClip): THREE.AnimationClip {
    const looped = LOOPED.get(clip) ?? clip;
    let once = ONCE.get(looped);

    if (once === undefined) {
        once = new THREE.AnimationClip(
            looped.name,
            heldEnd(looped),
            looped.tracks,
            looped.blendMode,
        );
        ONCE.set(looped, once);
        LOOPED.set(once, looped);
    }

    return once;
}

/** The clip as it plays when it loops: the clip itself, for its play-once copy as for itself. */
export function playedLooping(clip: THREE.AnimationClip): THREE.AnimationClip {
    return LOOPED.get(clip) ?? clip;
}

/** Whether an action has played its clip through and now holds its end. A looping one never has. */
export function clipDone(action: THREE.AnimationAction): boolean {
    return (
        action.loop === THREE.LoopOnce &&
        action.time >= action.getClip().duration
    );
}

/**
 * The clips running on a subject right now, for the animation switch - see `clipsSay`.
 *
 * One at most: the preview runs one clip per subject. A finished clip is still running - it holds its
 * frame - and says so, which is what lets an overlay be dropped when it is done.
 */
export function runningClips(subject: SubjectAnimation): RunningClip[] {
    const action = subject.action;

    return action === null
        ? []
        : [
              {
                  track: subject.visibility.get(action.getClip().name),
                  seconds: action.time,
                  done: clipDone(action),
              },
          ];
}
