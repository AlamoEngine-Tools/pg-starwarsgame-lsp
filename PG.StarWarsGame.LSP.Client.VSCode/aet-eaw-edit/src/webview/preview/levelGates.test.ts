// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { LevelGates } from "./levelGates";
import { setMeshDrawn } from "./meshVisibility";
import { standInContext } from "./viewportContext.testing";

/**
 * Gates over a stand-in viewport: one shadow-volume mesh, no parts. What the particles, the rows and the
 * shadow pass are told is recorded.
 */
function gatesWith(): {
    levels: LevelGates;
    modelRoot: THREE.Group;
    gated: [number, number, boolean][];
    counting: boolean[];
    refreshed: () => number;
    cloak: (on: boolean) => void;
} {
    const modelRoot = new THREE.Group();
    const volume = new THREE.Mesh(
        new THREE.BufferGeometry(),
        new THREE.MeshBasicMaterial(),
    );
    volume.userData.alamo = {
        alamoShader: "MeshShadowVolume.fx",
        alamoMesh: "Shadow",
    };
    modelRoot.add(volume);
    const gated: [number, number, boolean][] = [];
    const counting: boolean[] = [];
    let refreshes = 0;
    let stealthed = false;
    const context = standInContext({
        modelRoot,
        parts: new Map(),
        rules: { isStealthed: () => stealthed },
        particles: {
            gateByLevel: (alt: number, lod: number, descending: boolean) =>
                gated.push([alt, lod, descending]),
        },
        assembly: {
            forEachPartMesh: (visit: (mesh: THREE.Mesh) => void) =>
                visit(volume),
            applyOwnFileVisibility: () => undefined,
        },
        environment: {
            shadowVolumes: {
                setCounting: (_mesh: THREE.Mesh, on: boolean) =>
                    counting.push(on),
            },
        },
        rows: {
            refreshRows: () => {
                refreshes++;
            },
        },
    });
    return {
        levels: new LevelGates(context),
        modelRoot,
        gated,
        counting,
        refreshed: () => refreshes,
        cloak: (on: boolean) => {
            stealthed = on;
        },
    };
}

describe("LevelGates", () => {
    it("moves to a level, hands it to the effects, and refreshes the rows", () => {
        const { levels, gated, refreshed } = gatesWith();

        levels.setLevels(2, 1);
        levels.setLevels(1, 1);

        assert.deepEqual(gated, [
            [2, 1, false],
            [1, 1, true],
        ]);
        assert.equal(refreshed(), 2);
    });

    it("knows which way the damage level moved", () => {
        const { levels } = gatesWith();

        levels.setLevels(2, 0);
        assert.equal(levels.altDescending, false);

        levels.setLevels(1, 0);
        assert.equal(levels.altDescending, true);
    });

    it("stops a shadow volume counting while the unit is stealthed", () => {
        const { levels, counting, cloak } = gatesWith();

        cloak(true);
        levels.applyLevels();
        assert.equal(counting.at(-1), false);

        cloak(false);
        levels.applyLevels();
        assert.equal(counting.at(-1), true);
    });

    it("draws a mesh only while it and every ancestor up to the model are shown", () => {
        const { levels, modelRoot } = gatesWith();
        const group = new THREE.Group();
        const mesh = new THREE.Mesh(
            new THREE.BufferGeometry(),
            new THREE.MeshBasicMaterial(),
        );
        group.add(mesh);
        modelRoot.add(group);

        assert.equal(levels.drawn(mesh), true);

        group.visible = false;
        assert.equal(levels.drawn(mesh), false);

        group.visible = true;
        setMeshDrawn(mesh, false);
        assert.equal(levels.drawn(mesh), false);
    });
});
