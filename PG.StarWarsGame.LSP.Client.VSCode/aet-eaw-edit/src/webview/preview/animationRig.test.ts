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
});
