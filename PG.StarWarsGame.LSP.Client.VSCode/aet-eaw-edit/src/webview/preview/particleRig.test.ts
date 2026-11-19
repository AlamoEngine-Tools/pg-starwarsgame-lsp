// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { emitter, properties, testSystem } from "./particleFixture";
import { ParticleRig } from "./particleRig";
import { standInContext } from "./viewportContext.testing";
import { TextureStore } from "./textureStore";

/**
 * A rig over a stand-in viewport: a model root to hang systems on, nothing translated, no part to
 * attach to. Every system lands on the model root, which is where an unattached one goes.
 */
function rigWith(): { particles: ParticleRig; modelRoot: THREE.Group } {
    const modelRoot = new THREE.Group();
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
            attachmentFor: () => modelRoot,
            partRootOf: () => null,
        },
        materials: {
            shaderStates: new Map(),
            translated: new Map(),
            translatedShaders: false,
        },
        rows: { refreshRows: () => undefined },
    });
    return { particles: new ParticleRig(context), modelRoot };
}

const systemsUnder = (root: THREE.Object3D): number =>
    root.children.filter((child) => child.userData.aetParticleSystem === true)
        .length;

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

        particles.gateByLevel(0, 0, false);
        assert.equal(entry.altVisible, false);
        assert.equal(entry.lodVisible, true);

        particles.gateByLevel(1, 0, false);
        assert.equal(entry.altVisible, true);
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
