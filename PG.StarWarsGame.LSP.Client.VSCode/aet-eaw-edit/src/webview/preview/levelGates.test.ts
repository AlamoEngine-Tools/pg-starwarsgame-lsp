// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { LevelGates } from "./levelGates";
import { setMeshDrawn } from "./meshVisibility";
import { type ViewportContext } from "./viewportContext";

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
    const context = {
        modelRoot,
        parts: new Map(),
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
    } as unknown as ViewportContext;
    return {
        levels: new LevelGates(context),
        modelRoot,
        gated,
        counting,
        refreshed: () => refreshes,
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
        const { levels, counting } = gatesWith();

        levels.setStealthed(true);
        assert.equal(levels.isStealthed(), true);
        assert.equal(counting.at(-1), false);

        levels.setStealthed(false);
        assert.equal(counting.at(-1), true);
    });

    it("forgets a model's decals, collision meshes and shield", () => {
        const { levels } = gatesWith();
        levels.setDecals(new Set(["hp_decal"]), new Set(["hp_decal"]));
        levels.setCollisionMeshes(new Set(["collision"]));
        levels.setShieldRevealed(true);

        levels.forgetModel();

        assert.equal(levels.knownDecals.size, 0);
        assert.equal(levels.shownDecals.size, 0);
        assert.equal(levels.collisionMeshes.size, 0);
        assert.equal(levels.shieldRevealed, false);
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
