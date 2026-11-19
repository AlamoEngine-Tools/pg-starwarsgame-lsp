// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import {
    clipDone,
    heldEnd,
    playedLooping,
    playedOnce,
    runningClips,
} from "./clipEnd";
import { type SubjectAnimation } from "./subjectAnimation";

/** A clip of `frames` frames at 30 fps, a key on every frame, as the exporter writes one. */
function clipOf(frames: number): THREE.AnimationClip {
    const times = Array.from({ length: frames }, (_, i) => i / 30);
    return new THREE.AnimationClip("Hull_die_00", -1, [
        new THREE.NumberKeyframeTrack(
            "Hull.scale[x]",
            times,
            times.map(() => 1),
        ),
    ]);
}

describe("heldEnd", () => {
    it("is the second-to-last frame", () => {
        assert.equal(heldEnd(clipOf(31)), Math.fround(29 / 30));
    });

    it("is the first frame for a clip of two frames or fewer", () => {
        assert.equal(heldEnd(clipOf(2)), 0);
        assert.equal(heldEnd(clipOf(1)), 0);
        assert.equal(heldEnd(new THREE.AnimationClip("Empty", 0, [])), 0);
    });
});

describe("playedOnce", () => {
    it("ends on the held frame and shares the clip's tracks", () => {
        const clip = clipOf(31);
        const once = playedOnce(clip);

        assert.equal(once.name, clip.name);
        assert.equal(once.duration, Math.fround(29 / 30));
        assert.equal(once.tracks[0], clip.tracks[0]);
        assert.ok(
            Math.abs(clip.duration - 1) < 1e-6,
            "the clip itself still runs to its last frame",
        );
    });

    it("is made once per clip, and leads back to the clip", () => {
        const clip = clipOf(31);
        const once = playedOnce(clip);

        assert.equal(playedOnce(clip), once);
        assert.equal(playedOnce(once), once);
        assert.equal(playedLooping(once), clip);
        assert.equal(playedLooping(clip), clip);
    });
});

describe("clipDone and runningClips", () => {
    function subjectPlaying(
        loop: THREE.AnimationActionLoopStyles,
    ): SubjectAnimation {
        const root = new THREE.Group();
        const hull = new THREE.Object3D();
        hull.name = "Hull";
        root.add(hull);
        const clip = clipOf(4);
        const mixer = new THREE.AnimationMixer(root);
        const action = mixer.clipAction(
            loop === THREE.LoopOnce ? playedOnce(clip) : clip,
        );
        action.setLoop(loop, Infinity);
        action.clampWhenFinished = true;
        action.play();
        return {
            mixer,
            clips: [clip],
            visibility: new Map([
                [
                    "Hull_die_00",
                    { fps: 30, bones: new Map([["Hull#1", "1100"]]) },
                ],
            ]),
            nodeByName: new Map(),
            restPose: new Map(),
            action,
        };
    }

    it("calls a play-once clip done on its held frame, and says so to the animation switch", () => {
        const subject = subjectPlaying(THREE.LoopOnce);

        assert.equal(clipDone(subject.action!), false);
        subject.mixer!.update(10);

        assert.equal(clipDone(subject.action!), true);
        const [running] = runningClips(subject);
        assert.equal(running.done, true);
        assert.equal(running.seconds, Math.fround(2 / 30));
        assert.equal(running.track?.bones.get("Hull#1"), "1100");
    });

    it("never calls a looping clip done", () => {
        const subject = subjectPlaying(THREE.LoopRepeat);
        subject.mixer!.update(10);

        assert.equal(clipDone(subject.action!), false);
    });

    it("has nothing running on a subject with no clip", () => {
        const subject = subjectPlaying(THREE.LoopOnce);
        subject.action = null;

        assert.deepEqual(runningClips(subject), []);
    });
});
