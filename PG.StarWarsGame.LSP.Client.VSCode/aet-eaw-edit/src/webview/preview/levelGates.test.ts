// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type LevelsViewport, LevelGates } from "./levelGates";
import { setMeshDrawn } from "./meshVisibility";

/**
 * Gates over a stand-in viewport: one tagged particle system, one shadow-volume mesh, no parts. What the
 * rows and the shadow pass are told is recorded.
 */
function gatesWith(): {
    levels: LevelGates;
    modelRoot: THREE.Group;
    system: { levels: unknown; levelVisible: boolean };
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
    // Drawn only at ALT 1: a damage-level effect.
    const system = {
        levels: { alt: 1, lod: null, altDecreaseStayHidden: false },
        levelVisible: true,
    };
    const counting: boolean[] = [];
    let refreshes = 0;
    const host = {
        modelRoot,
        parts: new Map(),
        particles: { particleSystems: new Map([["fx", system]]) },
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
    } as unknown as LevelsViewport;
    return {
        levels: new LevelGates(host),
        modelRoot,
        system,
        counting,
        refreshed: () => refreshes,
    };
}

describe("LevelGates", () => {
    it("moves to a level, gates the effects tagged for it, and refreshes the rows", () => {
        const { levels, system, refreshed } = gatesWith();

        levels.setLevels(0, 0);
        assert.equal(system.levelVisible, false);

        levels.setLevels(1, 0);
        assert.equal(system.levelVisible, true);
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
