// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { AnimationRig } from "./animationRig";
import { PLAIN_LOOK, cloakLookOf } from "./cloakFade";
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

function near(
    actual: number,
    expected: number,
    what: string,
    within = 1e-6,
): void {
    assert.ok(
        Math.abs(actual - expected) < within,
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
        const hides = { fps: 30, bones: new Map([["Hatch#2", "10"]]) };
        const model = {
            clips: [clip],
            visibility: new Map([["Open", hides]]),
        };

        animation.registerPart("wreck:1", root, model);
        const subject = animation.subjectAnimation("wreck:1");
        assert.ok(subject.restPose.has(bone));
        assert.equal(subject.nodeByName.get("Hatch#2"), bone);
        assert.deepEqual(subject.clips, [clip]);
        assert.equal(subject.visibility.get("Open"), hides);
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

    /** A death clone plays once and ends on the same frame; with no persistence and no fade it goes there. */
    it("ends a death clone's clip on its second-to-last frame", () => {
        const { animation, clone, bone, hidden } = dyingClone(true);

        animation.startDeathClip("clone:1", {
            persistenceSeconds: 0,
            fadeSeconds: 0,
            endsInExplosions: false,
        });
        animation.advancePassiveSubjects(2.5);

        assert.equal(clone.action?.time, 2);
        near(bone.position.x, 2, "x");
        assert.deepEqual(hidden, ["clone:1/part"]);
    });

    /** Measured: a type that writes neither tag stays for ever - persistence -1 never starts the fade. */
    it("keeps a death clone that writes nothing about its ending for good", () => {
        const { animation, clone, hidden } = dyingClone(true);

        animation.startDeathClip("clone:1");
        for (let i = 0; i < 60; i++) {
            animation.advancePassiveSubjects(1);
        }

        assert.deepEqual(hidden, []);
        assert.equal(clone.death?.alpha, 1);
    });

    // ── how a death clone ends ────────────────────────────────────────────────
    //
    // Measured in the game: a dying object with a death clip STAYS. Once its clip is done its fade
    // starts Death_Persistence_Duration later; then it fades out over Death_Fade_Time and is gone at
    // 0.025 or below - or, naming Death_Explosions_End, is destroyed at the fade start. The clock
    // here is the time the rig is stepped by.

    it("keeps a death clone on its held frame for its persistence, then fades it out", () => {
        const { animation, clone, bone, hidden, released, ended } =
            dyingClone(true);

        animation.startDeathClip(
            "clone:1",
            { persistenceSeconds: 5, fadeSeconds: 1, endsInExplosions: false },
            ended.push,
        );
        animation.advancePassiveSubjects(2);
        animation.advancePassiveSubjects(4.9);

        assert.deepEqual(hidden, [], "still there before its fade start");
        near(bone.position.x, 2, "x");

        animation.advancePassiveSubjects(0.6);
        near(
            clone.death?.alpha ?? -1,
            0.74,
            "alpha half a fade time in",
            0.005,
        );
        assert.deepEqual(hidden, []);

        animation.advancePassiveSubjects(0.5);
        assert.deepEqual(hidden, [], "still fading a fade time in");

        animation.advancePassiveSubjects(2);
        assert.deepEqual(hidden, ["clone:1/part"]);
        assert.equal(
            released.length,
            1,
            "its effects released when it goes, not before",
        );
        assert.equal(ended.length, 1);
    });

    it("destroys a death clone that names end explosions at its fade start, without a fade", () => {
        const { animation, hidden, ended } = dyingClone(true);

        animation.startDeathClip(
            "clone:1",
            { persistenceSeconds: 3, fadeSeconds: 1, endsInExplosions: true },
            ended.push,
        );
        animation.advancePassiveSubjects(2);
        animation.advancePassiveSubjects(2.9);
        assert.deepEqual(hidden, []);

        animation.advancePassiveSubjects(0.2);
        assert.deepEqual(hidden, ["clone:1/part"]);
        assert.equal(ended.length, 1);
    });

    it("starts the persistence at once for a death clone with no death clip", () => {
        const { animation, hidden } = dyingClone(false);

        animation.startDeathClip("clone:1", {
            persistenceSeconds: 1,
            fadeSeconds: 0,
            endsInExplosions: false,
        });
        animation.advancePassiveSubjects(0.5);
        assert.deepEqual(hidden, []);

        animation.advancePassiveSubjects(0.6);
        assert.deepEqual(hidden, ["clone:1/part"]);
    });

    // ── a model's look ────────────────────────────────────────────────────────

    it("gives the subject the cloak's look as it comes in", () => {
        const { animation } = rigWith();

        assert.deepEqual(animation.lookOf(ACTIVE_SUBJECT), PLAIN_LOOK);

        animation.cloak.setHasCloak(true);
        animation.cloak.set(
            cloakLookOf({ r: 255, g: 255, b: 255, a: 0 }, 1),
            "stealth",
        );
        animation.cloak.step(0.5);

        // The spring's value half a transition in is 0.264, and the alpha goes 1 -> 0 by it.
        near(
            animation.lookOf(ACTIVE_SUBJECT).a,
            1 - animation.cloak.value,
            "alpha",
        );
        assert.ok(animation.cloak.value > 0.2 && animation.cloak.value < 0.3);
    });

    /** The death clone's fade, drawn: its alpha is the look every mesh and effect of it takes. */
    it("gives a dying clone its death alpha, and no cloak of the subject's", () => {
        const { animation, clone } = dyingClone(false);
        animation.cloak.setHasCloak(true);
        animation.cloak.set(
            cloakLookOf({ r: 255, g: 0, b: 0, a: 0 }, 1),
            "stealth",
        );
        animation.cloak.step(1);

        assert.deepEqual(animation.lookOf("clone:1"), PLAIN_LOOK);

        animation.startDeathClip("clone:1", {
            persistenceSeconds: 0,
            fadeSeconds: 1,
            endsInExplosions: false,
        });
        animation.advancePassiveSubjects(1);

        near(animation.lookOf("clone:1").a, clone.death?.alpha ?? -1, "alpha");
        assert.ok((clone.death?.alpha ?? 1) < 1, "the fade has begun");
        assert.equal(animation.lookOf("clone:1").r, 1);
    });
});

/**
 * A rig with one passive subject, `clone:1`, carrying {@link walk} as `Hull_die_00` when asked for a
 * clip - and no mixer at all when not. What it hides, the effect releases and the end callbacks are
 * recorded.
 */
function dyingClone(withClip: boolean): {
    animation: AnimationRig;
    clone: ReturnType<AnimationRig["subjectAnimation"]>;
    bone: THREE.Object3D;
    hidden: string[];
    released: string[];
    ended: { push: () => void; length: number };
} {
    const hidden: string[] = [];
    const released: string[] = [];
    let ends = 0;
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
            releaseSubjectEffects: (id: string) => {
                released.push(id);
            },
        },
    });
    const animation = new AnimationRig(context);
    const root = new THREE.Group();
    const bone = new THREE.Object3D();
    bone.name = "Turret";
    root.add(bone);
    const clone = animation.subjectAnimation("clone:1");

    if (withClip) {
        clone.mixer = new THREE.AnimationMixer(root);
        clone.clips.push(walk("Hull_die_00"));
    }

    return {
        animation,
        clone,
        bone,
        hidden,
        released,
        ended: {
            push: () => {
                ends++;
            },
            get length() {
                return ends;
            },
        },
    };
}
