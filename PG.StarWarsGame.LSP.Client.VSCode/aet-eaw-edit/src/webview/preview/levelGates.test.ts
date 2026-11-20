// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { LevelGates } from "./levelGates";
import { setMeshDrawn } from "./meshVisibility";
import { standInContext } from "./viewportContext.testing";

/**
 * Gates over a stand-in viewport with no parts. What the particles and the rows are told is recorded.
 */
function gatesWith(): {
    levels: LevelGates;
    modelRoot: THREE.Group;
    gated: [number, number, boolean][];
    refreshed: () => number;
    released: () => number;
} {
    const modelRoot = new THREE.Group();
    const gated: [number, number, boolean][] = [];
    let refreshes = 0;
    let releases = 0;
    const context = standInContext({
        modelRoot,
        parts: new Map(),
        animation: {
            cloak: {
                releaseVolumes: () => {
                    releases++;
                },
            },
        },
        particles: {
            gateByLevel: (alt: number, lod: number, descending: boolean) =>
                gated.push([alt, lod, descending]),
        },
        assembly: { applyOwnFileVisibility: () => undefined },
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
        refreshed: () => refreshes,
        released: () => releases,
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

    /** Measured: a detail level change gives a past stealth's shadow volumes back. */
    it("gives a past cloak's shadow volumes back when the detail level changes, and only then", () => {
        const { levels, released } = gatesWith();

        levels.setLevels(1, 0);
        assert.equal(released(), 0, "a damage change alone");

        levels.setLevels(1, 2);
        assert.equal(released(), 1);
    });

    it("knows which way the damage level moved", () => {
        const { levels } = gatesWith();

        levels.setLevels(2, 0);
        assert.equal(levels.altDescending, false);

        levels.setLevels(1, 0);
        assert.equal(levels.altDescending, true);
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
