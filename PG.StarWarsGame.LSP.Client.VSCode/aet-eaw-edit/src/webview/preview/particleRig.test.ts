// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { emitter, properties, testSystem } from "./particleFixture";
import { PLAIN_LOOK } from "./cloakFade";
import { ParticleRig } from "./particleRig";
import { standInContext } from "./viewportContext.testing";
import { TextureStore } from "./textureStore";

/**
 * A rig over a stand-in viewport: a model root to hang systems on, nothing translated, no part to
 * attach to. Every system lands on the model root, which is where an unattached one goes - or, given
 * `anchor`, on that node, as a system on a bone does.
 */
function rigWith(anchor?: THREE.Object3D): {
    particles: ParticleRig;
    modelRoot: THREE.Group;
} {
    const modelRoot = new THREE.Group();
    if (anchor !== undefined) {
        modelRoot.add(anchor);
    }
    const context = standInContext({
        levels: { alt: 0, altDescending: false, lod: 0 },
        cameraRig: {
            fitDistance: 1,
            cameraPosition: { x: 0, y: 0, z: 10 },
            boundingSphere: () => ({ center: { x: 0, y: 0, z: 0 }, radius: 1 }),
            applyClipPlanes: () => undefined,
        },
        modelRoot,
        parts: new Map(),
        textures: new TextureStore(),
        assembly: {
            hiddenParts: new Set(),
            attachmentFor: () => anchor ?? modelRoot,
            partRootOf: () => null,
        },
        materials: {
            shaderStates: new Map(),
            translated: new Map(),
            translatedShaders: false,
        },
        rows: { refreshRows: () => undefined },
        // Every system here is the scene's own, drawn as it is.
        animation: { lookOf: () => PLAIN_LOOK },
    });
    return { particles: new ParticleRig(context), modelRoot };
}

const systemsUnder = (root: THREE.Object3D): number =>
    root.children.filter((child) => child.userData.aetParticleSystem === true)
        .length;

/** A continuous emitter whose particles live half a second, so a stopped one drains in a few frames. */
const fire = (): ReturnType<typeof testSystem> =>
    testSystem([
        emitter({
            name: "Fire",
            properties: properties({ particlesPerSecond: 30, lifetime: 0.5 }),
        }),
    ]);

/** The particles in the air for one system. */
const liveOf = (particles: ParticleRig, id: string): number =>
    particles.particleSystems.get(id)!.instance.emitterParticles(0).length;

/** Runs the rig `frames` steps on at 30 Hz, and returns the live count after each. */
function run(particles: ParticleRig, id: string, frames: number): number[] {
    const counts: number[] = [];
    for (let at = 0; at < frames; at++) {
        particles.step(1 / 30, new THREE.Matrix4());
        counts.push(liveOf(particles, id));
    }
    return counts;
}

/** Hangs a triangle on `root` and counts how often its vertices are read. */
function countedGeometryOn(root: THREE.Object3D): { reads: () => number } {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute(
        "position",
        new THREE.BufferAttribute(
            new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 0]),
            3,
        ),
    );
    let reads = 0;
    const read = geometry.getAttribute.bind(geometry);
    geometry.getAttribute = ((name: string) => {
        reads++;
        return read(name);
    }) as typeof geometry.getAttribute;
    root.add(new THREE.Mesh(geometry));
    return { reads: () => reads };
}

describe("ParticleRig", () => {
    it("prepares systems that differ only in the stored system name once", () => {
        const { particles } = rigWith();

        particles.addParticleSystem(
            "a",
            testSystem([emitter({ name: "Glow" })], "P_engine_a"),
            { origin: "model" },
        );
        particles.addParticleSystem(
            "b",
            testSystem([emitter({ name: "Glow" })], "P_engine_b"),
            { origin: "model" },
        );
        particles.addParticleSystem(
            "c",
            testSystem([emitter({ name: "Smoke" })], "P_smoke"),
            { origin: "model" },
        );

        assert.equal(particles.preparations.prepared, 2);
        assert.deepEqual(particles.particleSystemIds(), ["a", "b", "c"]);

        particles.forgetSystems();
        assert.equal(particles.preparations.prepared, 0);
    });

    it("reads the attached geometry only for a system with an emitter born on a mesh", () => {
        const { particles, modelRoot } = rigWith();
        const geometry = countedGeometryOn(modelRoot);

        particles.addParticleSystem("plain", testSystem([emitter()]), {
            origin: "model",
        });
        assert.equal(geometry.reads(), 0);

        particles.addParticleSystem(
            "wash",
            testSystem([
                emitter({
                    properties: properties({ emitFromMesh: "EveryVertex" }),
                }),
            ]),
            { origin: "model" },
        );
        assert.ok(geometry.reads() > 0);
    });

    it("puts a system on the model and lists it by id, with its emitters by name", () => {
        const { particles, modelRoot } = rigWith();

        particles.addParticleSystem(
            "fx",
            testSystem([
                emitter({ name: "Spark" }),
                emitter({ name: "Smoke" }),
            ]),
            {
                origin: "model",
            },
        );

        assert.deepEqual(particles.particleSystemIds(), ["fx"]);
        assert.deepEqual(particles.emitterNames("fx"), ["Spark", "Smoke"]);
        assert.equal(systemsUnder(modelRoot), 1);
    });

    it("forgets a system it is told to remove", () => {
        const { particles } = rigWith();

        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
        });
        particles.removeParticleSystem("fx");

        assert.deepEqual(particles.particleSystemIds(), []);
    });

    it("has no weather without a weather emitter", () => {
        const { particles } = rigWith();

        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
        });

        assert.equal(particles.hasWeather(), false);
    });

    it("steps nothing while paused", () => {
        const { particles } = rigWith();

        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
        });
        particles.setParticlesPaused(true);

        assert.doesNotThrow(() => particles.step(1 / 30, new THREE.Matrix4()));
        assert.deepEqual(particles.particleSystemIds(), ["fx"]);
    });

    // #289: Stop holds every emitter and lets what is already in the air finish its life.
    it("stops every system spawning, and lets the particles in the air finish", () => {
        const { particles } = rigWith();
        particles.addParticleSystem("fx", fire(), { origin: "model" });
        run(particles, "fx", 15);
        const before = liveOf(particles, "fx");
        assert.ok(before > 0, "nothing spawned to begin with");

        particles.stopParticles();
        const after = run(particles, "fx", 30);

        // Not cut: a frame on, all but the ones whose lives ran out are still in the air.
        assert.ok(after[0] >= before - 2, "the stop cut particles short");
        for (let at = 1; at < after.length; at++) {
            assert.ok(
                after[at] <= after[at - 1],
                `a particle was born at ${at}`,
            );
        }
        assert.equal(after[after.length - 1], 0);
        // Held, not removed: the system stays addressable for a restart.
        assert.deepEqual(particles.particleSystemIds(), ["fx"]);
        assert.ok(
            particles.particleSystems
                .get("fx")!
                .instance.heldBy.includes("stopped"),
        );
    });

    it("holds a system that arrives while stopped", () => {
        const { particles } = rigWith();
        particles.stopParticles();

        particles.addParticleSystem("fx", fire(), { origin: "model" });

        assert.equal(Math.max(...run(particles, "fx", 15)), 0);
    });

    it("plays a stopped system again from nothing on restart", () => {
        const { particles } = rigWith();
        particles.addParticleSystem("fx", fire(), { origin: "model" });
        run(particles, "fx", 15);
        particles.stopParticles();
        run(particles, "fx", 30);

        particles.restartParticles();

        assert.ok(
            liveOf(particles, "fx") === 0,
            "the restart kept old particles",
        );
        assert.ok(Math.max(...run(particles, "fx", 5)) > 0);
    });

    it("forgets the stop with the scene", () => {
        const { particles } = rigWith();
        particles.stopParticles();
        particles.forgetSystems();

        particles.addParticleSystem("fx", fire(), { origin: "model" });

        assert.ok(Math.max(...run(particles, "fx", 5)) > 0);
    });

    it("keeps its own record of the textures that will never arrive", () => {
        // The marker is decoded through the DOM; here the decode never settles, so nothing is drawn
        // and nothing rejects. What is under test is that the viewport is not asked for a set.
        const image = globalThis as unknown as { Image?: unknown };
        const saved = image.Image;
        image.Image = class {
            src = "";
            decode(): Promise<void> {
                return new Promise(() => undefined);
            }
        };
        try {
            const { particles } = rigWith();

            particles.setParticleTextureMissing("P_Spark.tga");
            particles.setParticleTexture("P_Spark.tga", new THREE.Texture());

            assert.deepEqual(particles.particleTextureNames(), []);
        } finally {
            image.Image = saved;
        }
    });

    it("shows a system only at the level its proxy is tagged for", () => {
        const { particles } = rigWith();
        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
            levels: { alt: 1, lod: null, altDecreaseStayHidden: false },
        });
        const entry = particles.particleSystems.get("fx")!;

        particles.gateByLevel(0, false);
        assert.equal(entry.altVisible, false);

        particles.gateByLevel(1, false);
        assert.equal(entry.altVisible, true);
    });

    // Measured in the game: detail level gates meshes only - an effect runs at every LOD, whatever
    // `_LOD` tag its proxy's name carries (#450). No shipped effect carries one; mods do. The rig is
    // not told the detail level at all, which is what "every LOD" comes down to.
    it("runs an _LOD-tagged effect at every detail level", () => {
        const { particles } = rigWith();
        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
            levels: { alt: null, lod: 1, altDecreaseStayHidden: true },
        });
        const entry = particles.particleSystems.get("fx")!;

        for (const alt of [0, 1, 2]) {
            for (const descending of [false, true]) {
                particles.gateByLevel(alt, descending);

                assert.equal(entry.altVisible, true, `ALT ${alt}`);
            }
        }
        assert.equal("lodVisible" in entry, false);
    });

    it("forgets every system, for a new scene", () => {
        const { particles, modelRoot } = rigWith();
        particles.addParticleSystem("a", testSystem([emitter()]), {
            origin: "model",
        });
        particles.addParticleSystem("b", testSystem([emitter()]), {
            origin: "model",
        });

        particles.forgetSystems();

        assert.deepEqual(particles.particleSystemIds(), []);
        void modelRoot;
    });

    it("hides the effects from a shot without particles, and puts them back", () => {
        const { particles } = rigWith();

        const restore = particles.stageForCapture({
            width: 64,
            height: 64,
            background: null,
            grid: false,
            floor: true,
            particles: false,
            annotations: false,
        });
        assert.equal(particles.particlesVisible, false);

        restore();
        assert.equal(particles.particlesVisible, true);
    });
});

/**
 * #190, pinned: an effect keeps pace with the bone it is anchored on.
 *
 * The motion is the report's: the Slave 1 takeoff moves the whole ship by its `Root` bone, up to
 * 9.3 units per game frame. The bone is moved BEFORE each step, as the animation mixer moves it
 * before the particle pass, and the gaps are read in the simulation space (the model root).
 */
describe("ParticleRig on a bone in flight (#190)", () => {
    /** Units per 30 Hz frame. */
    const TRAVEL = 9.3;
    const FRAMES = 45;

    /** Per frame: the gap from the bone to the newest particle, and to the centroid of the live ones. */
    function flight(
        linkToSystem: boolean,
    ): { newest: number; centroid: number; live: number }[] {
        const bone = new THREE.Object3D();
        const { particles } = rigWith(bone);
        particles.addParticleSystem(
            "engine",
            testSystem([
                emitter({
                    name: "Engine",
                    properties: properties({
                        linkToSystem,
                        particlesPerSecond: 30,
                        lifetime: 1,
                    }),
                }),
            ]),
            { origin: "model" },
        );
        const instance = particles.particleSystems.get("engine")!.instance;

        const frames: { newest: number; centroid: number; live: number }[] = [];
        for (let at = 0; at < FRAMES; at++) {
            bone.position.x += TRAVEL;
            particles.step(1 / 30, new THREE.Matrix4());

            const live = instance.emitterParticles(0);
            if (live.length === 0) {
                continue;
            }
            const gap = (p: { x: number; y: number; z: number }): number =>
                Math.hypot(
                    p.x - bone.position.x,
                    p.y - bone.position.y,
                    p.z - bone.position.z,
                );
            const newest = live.reduce((a, b) => (b.age < a.age ? b : a));
            const sum = live.reduce(
                (s, p) => ({
                    x: s.x + p.position.x,
                    y: s.y + p.position.y,
                    z: s.z + p.position.z,
                }),
                { x: 0, y: 0, z: 0 },
            );
            frames.push({
                newest: gap(newest.position),
                centroid: gap({
                    x: sum.x / live.length,
                    y: sum.y / live.length,
                    z: sum.z / live.length,
                }),
                live: live.length,
            });
        }
        return frames;
    }

    it("keeps a linked effect on the bone, newest particle and centroid alike, every frame", () => {
        const frames = flight(true);

        assert.ok(frames.length >= FRAMES - 1, `${frames.length} frames`);
        for (const [at, frame] of frames.entries()) {
            assert.ok(frame.newest < 1e-3, `newest ${frame.newest} at ${at}`);
            assert.ok(
                frame.centroid < 1e-2,
                `centroid ${frame.centroid} at ${at}`,
            );
        }
    });

    // The other direction: an emitter the file does not link is a world-space one, and its particles
    // stay where they were born. A second's worth of them spans a second of travel, so their centroid
    // trails by half of it - 30 x 9.3 / 2 = 139.5 units. Carrying them along would be the regression.
    it("leaves an unlinked effect behind by half a lifetime of travel", () => {
        const frames = flight(false);
        const settled = frames.slice(35);

        assert.ok(settled.length > 0);
        for (const frame of settled) {
            // Born on the bone all the same: only the carrying differs.
            assert.ok(frame.newest < 1e-3, `newest ${frame.newest}`);
            assert.ok(
                Math.abs(frame.centroid - 139.5) < 0.5,
                `centroid ${frame.centroid}`,
            );
        }
    });
});
