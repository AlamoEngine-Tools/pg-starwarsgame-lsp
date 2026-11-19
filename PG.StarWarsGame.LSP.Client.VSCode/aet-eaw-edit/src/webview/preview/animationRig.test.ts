// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { AnimationRig } from "./animationRig";
import { ACTIVE_SUBJECT } from "./loadedPart";
import { standInContext } from "./viewportContext.testing";

/**
 * A rig over a stand-in viewport whose active subject has a mixer on a root with one bone, its rest pose
 * recorded, and one clip that moves the bone two units along X over two seconds. No particle systems; the
 * row and level calls are counted.
 */
function rigWith(): {
    animation: AnimationRig;
    bone: THREE.Object3D;
    cleared: () => number;
    reposed: () => number;
    composed: () => number;
} {
    let clears = 0;
    let reposes = 0;
    let composes = 0;
    const context = standInContext({
        turret: {
            reposeTurrets: () => {
                reposes++;
            },
            composeOnClip: (step: () => void) => {
                composes++;
                step();
            },
        },
        assembly: { setPartHidden: () => undefined },
        rows: {
            clearRowOverrides: () => {
                clears++;
            },
        },
        particles: { particleSystems: new Map() },
        levels: { applyLevels: () => undefined },
    });
    const animation = new AnimationRig(context);

    const root = new THREE.Group();
    const bone = new THREE.Object3D();
    bone.name = "Turret";
    root.add(bone);
    const subject = animation.subjectAnimation(ACTIVE_SUBJECT);
    subject.mixer = new THREE.AnimationMixer(root);
    subject.clips.push(
        new THREE.AnimationClip("Move", 2, [
            new THREE.VectorKeyframeTrack(
                "Turret.position",
                [0, 2],
                [0, 0, 0, 2, 0, 0],
            ),
        ]),
    );
    subject.restPose.set(bone, {
        position: bone.position.clone(),
        quaternion: bone.quaternion.clone(),
        scale: bone.scale.clone(),
    });
    return {
        animation,
        bone,
        cleared: () => clears,
        reposed: () => reposes,
        composed: () => composes,
    };
}

/**
 * A four-frame clip at one frame a second, as the exporter writes one: a key on every frame, the last
 * a copy of the first so a loop is seamless. The bone walks 0, 1, 2 along X and is back at 0 on the
 * last frame, so the frame on screen can be read off its position.
 */
function walk(name: string): THREE.AnimationClip {
    return new THREE.AnimationClip(name, -1, [
        new THREE.VectorKeyframeTrack(
            "Turret.position",
            [0, 1, 2, 3],
            [0, 0, 0, 1, 0, 0, 2, 0, 0, 0, 0, 0],
        ),
    ]);
}

/** A rig whose active subject also carries {@link walk} as `Walk`. */
function walkingRig(): { animation: AnimationRig; bone: THREE.Object3D } {
    const rig = rigWith();
    rig.animation.active.clips.push(walk("Walk"));
    return rig;
}

function near(actual: number, expected: number, what: string): void {
    assert.ok(
        Math.abs(actual - expected) < 1e-6,
        `${what} ${actual}, expected ${expected}`,
    );
}

describe("AnimationRig", () => {
    it("keeps one animation per subject, made on first ask", () => {
        const { animation } = rigWith();

        assert.equal(
            animation.subjectAnimation(ACTIVE_SUBJECT),
            animation.subjectAnimation(ACTIVE_SUBJECT),
        );
        assert.notEqual(
            animation.subjectAnimation("wreck:1"),
            animation.subjectAnimation(ACTIVE_SUBJECT),
        );
        assert.deepEqual(animation.animationNames, ["Move"]);
    });

    it("plays a clip by name, handing the rows back to the model, and refuses one it does not have", () => {
        const { animation, cleared } = rigWith();

        assert.equal(animation.play("Missing"), false);
        assert.equal(animation.play("Move"), true);
        assert.equal(cleared(), 2);
        assert.deepEqual(animation.animationProgress(), {
            time: 0,
            duration: 2,
            running: true,
        });
    });

    it("seeks within the clip and poses the bone there", () => {
        const { animation, bone } = rigWith();
        animation.play("Move");

        animation.seekAnimation(1);
        assert.ok(Math.abs(bone.position.x - 1) < 1e-6, `x ${bone.position.x}`);

        animation.seekAnimation(10);
        assert.equal(animation.animationProgress().time, 2);
    });

    it("keeps speed and pause across a clip change", () => {
        const { animation } = rigWith();
        animation.setAnimationSpeed(0.5);
        animation.setPaused(true);

        animation.play("Move");

        assert.equal(animation.animationProgress().running, false);
        assert.equal(animation.active.action?.timeScale, 0.5);
    });

    it("puts the bones back at rest when a clip stops", () => {
        const { animation, bone } = rigWith();
        animation.play("Move");
        animation.seekAnimation(1.5);

        animation.play(null);

        assert.equal(bone.position.x, 0);
        assert.equal(animation.active.action, null);
    });

    it("takes in what a part brings to its subject, and lets it go again", () => {
        const { animation } = rigWith();
        const root = new THREE.Group();
        const bone = new THREE.Object3D();
        bone.name = "Hatch#2";
        root.add(bone);
        const hull = new THREE.Mesh(
            new THREE.BufferGeometry(),
            new THREE.MeshBasicMaterial(),
        );
        hull.userData.alamo = {
            alamoShader: "MeshCollision.fx",
            alamoMesh: "Collision",
            alamoHidden: true,
        };
        root.add(hull);
        const clip = new THREE.AnimationClip("Open", 1, []);
        const gltf = {
            animations: [clip],
            parser: { json: {} },
        } as unknown as Parameters<typeof animation.registerPart>[2];

        animation.registerPart("wreck:1", root, gltf);
        const subject = animation.subjectAnimation("wreck:1");
        assert.ok(subject.restPose.has(bone));
        assert.equal(subject.nodeByName.get("Hatch#2"), bone);
        assert.deepEqual(subject.clips, [clip]);
        assert.notEqual(subject.mixer, null);

        animation.unregisterPart("wreck:1", [bone, hull]);
        assert.equal(subject.restPose.has(bone), false);
        assert.equal(subject.nodeByName.has("Hatch#2"), false);

        animation.forgetSubject("wreck:1");
        assert.notEqual(animation.subjectAnimation("wreck:1"), subject);
    });

    it("steps the playing clip on", () => {
        const { animation, bone } = rigWith();
        animation.play("Move");

        animation.advance(0.5);

        assert.ok(
            Math.abs(bone.position.x - 0.5) < 1e-6,
            `x ${bone.position.x}`,
        );
    });

    it("hands the turrets back their pose whenever it puts the file's pose back", () => {
        const { animation, reposed } = rigWith();

        animation.play("Move");
        assert.equal(reposed(), 1);

        animation.play(null);
        assert.equal(reposed(), 2);
    });

    it("steps a playing clip through the turret layer, and an idle mixer without it", () => {
        const { animation, bone, composed } = rigWith();

        animation.advance(0.5);
        assert.equal(composed(), 0);

        animation.play("Move");
        animation.advance(0.5);
        assert.equal(composed(), 1);
        assert.ok(
            Math.abs(bone.position.x - 0.5) < 1e-6,
            `x ${bone.position.x}`,
        );

        animation.seekAnimation(1);
        assert.equal(composed(), 2);
    });

    // ── where a clip ends ─────────────────────────────────────────────────────

    /**
     * Measured in the game: a clip that does not loop advances to frame count - 2, is then done, and
     * holds that frame. The last frame is the loop's copy of the first and is never shown at rest.
     */
    it("holds a finished non-looping clip on its second-to-last frame", () => {
        const { animation, bone } = walkingRig();
        animation.setAnimationLoop(false);
        animation.play("Walk");

        animation.advance(10);

        near(bone.position.x, 2, "x");
        assert.deepEqual(animation.animationProgress(), {
            time: 2,
            duration: 2,
            running: false,
        });
    });

    it("wraps a looping clip at its last frame", () => {
        const { animation, bone } = walkingRig();
        animation.play("Walk");

        animation.advance(3.5);

        near(animation.animationProgress().time, 0.5, "time");
        near(animation.animationProgress().duration, 3, "duration");
        near(bone.position.x, 0.5, "x");
    });

    it("keeps the playhead when the loop is switched off mid-clip, then holds", () => {
        const { animation, bone } = walkingRig();
        animation.play("Walk");
        animation.advance(1.5);

        animation.setAnimationLoop(false);

        near(animation.animationProgress().time, 1.5, "time");
        assert.equal(animation.animationProgress().running, true);

        animation.advance(10);
        near(bone.position.x, 2, "x");
    });

    it("plays on from the held frame when the loop is switched back on", () => {
        const { animation, bone } = walkingRig();
        animation.setAnimationLoop(false);
        animation.play("Walk");
        animation.advance(10);

        animation.setAnimationLoop(true);
        animation.setPaused(false);
        animation.advance(0.5);

        near(animation.animationProgress().time, 2.5, "time");
        near(bone.position.x, 1, "x");
    });

    /** The scrubber's end is the held frame: dragging to the end shows what the clip comes to rest on. */
    it("seeks no further than the held frame of a non-looping clip", () => {
        const { animation, bone } = walkingRig();
        animation.setAnimationLoop(false);
        animation.play("Walk");

        animation.seekAnimation(10);

        assert.equal(animation.animationProgress().time, 2);
        near(bone.position.x, 2, "x");
    });

    /** A death clone plays once, ends on the same frame, and is taken away there. */
    it("ends a death clone's clip on its second-to-last frame, and takes the clone away there", () => {
        const hidden: string[] = [];
        const context = standInContext({
            assembly: {
                partsOf: () => [{ id: "clone:1/part" }],
                applyOwnFileVisibility: () => undefined,
                setPartHidden: (id: string) => {
                    hidden.push(id);
                },
            },
            particles: {
                particleSystems: new Map(),
                applyEmitterGating: () => undefined,
                releaseSubjectEffects: () => undefined,
            },
        });
        const animation = new AnimationRig(context);
        const root = new THREE.Group();
        const bone = new THREE.Object3D();
        bone.name = "Turret";
        root.add(bone);
        const clone = animation.subjectAnimation("clone:1");
        clone.mixer = new THREE.AnimationMixer(root);
        clone.clips.push(walk("Hull_die_00"));

        animation.startDeathClip("clone:1");
        animation.advancePassiveSubjects(2.5);

        assert.equal(clone.action?.time, 2);
        near(bone.position.x, 2, "x");
        assert.deepEqual(hidden, ["clone:1/part"]);
    });
});
