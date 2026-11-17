// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { emitter, testSystem } from "./particleFixture";
import { type ParticleViewport, ParticleHost } from "./particleHost";

/**
 * A host over a stand-in viewport: a model root to hang systems on, nothing translated, no part to
 * attach to. Every system lands on the model root, which is where an unattached one goes.
 */
function hostWith(): { particles: ParticleHost; modelRoot: THREE.Group } {
    const modelRoot = new THREE.Group();
    const host = {
        alt: 0,
        altDescending: false,
        lod: 0,
        fitDistance: 1,
        cameraPosition: { x: 0, y: 0, z: 10 },
        modelRoot,
        parts: new Map(),
        hiddenParts: new Set(),
        missingTextures: new Set(),
        shaderStates: new Map(),
        translated: new Map(),
        translatedShaders: false,
        attachmentFor: () => modelRoot,
        partRootOf: () => null,
        boundingSphere: () => ({ center: { x: 0, y: 0, z: 0 }, radius: 1 }),
        applyClipPlanes: () => undefined,
        markMissing: async () => undefined,
        refreshParticleVisibility: () => undefined,
        refreshRows: () => undefined,
    } as unknown as ParticleViewport;
    return { particles: new ParticleHost(host), modelRoot };
}

const systemsUnder = (root: THREE.Object3D): number =>
    root.children.filter((child) => child.userData.aetParticleSystem === true)
        .length;

describe("ParticleHost", () => {
    it("puts a system on the model and lists it by id, with its emitters by name", () => {
        const { particles, modelRoot } = hostWith();

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
        const { particles } = hostWith();

        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
        });
        particles.removeParticleSystem("fx");

        assert.deepEqual(particles.particleSystemIds(), []);
    });

    it("has no weather without a weather emitter", () => {
        const { particles } = hostWith();

        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
        });

        assert.equal(particles.hasWeather(), false);
    });

    it("steps nothing while paused", () => {
        const { particles } = hostWith();

        particles.addParticleSystem("fx", testSystem([emitter()]), {
            origin: "model",
        });
        particles.setParticlesPaused(true);

        assert.doesNotThrow(() => particles.step(1 / 30, new THREE.Matrix4()));
        assert.deepEqual(particles.particleSystemIds(), ["fx"]);
    });
});
