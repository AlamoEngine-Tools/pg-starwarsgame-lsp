// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { meshDrawn } from "./meshVisibility";
import { type RowsViewport, ModelRows } from "./modelRows";

/**
 * Rows over a stand-in viewport: a hull with a root bone and one child bone carrying a mesh of its own
 * name, no particle systems, nothing animated, every level and gate open.
 */
function rowsWith(): { rows: ModelRows; mesh: THREE.Mesh; extra: THREE.Mesh } {
    const root = new THREE.Group();
    const rootBone = new THREE.Bone();
    rootBone.name = "Root#0";
    const turretBone = new THREE.Bone();
    turretBone.name = "Turret#1";
    rootBone.add(turretBone);
    root.add(rootBone);

    // A mesh of the bone's own name shares its row; a second one on the same bone is a child row.
    const mesh = new THREE.Mesh(
        new THREE.BufferGeometry(),
        new THREE.MeshStandardMaterial(),
    );
    mesh.name = "Turret";
    mesh.userData.alamo = { alamoMesh: "Turret", alamoShader: "MeshGloss.fx" };
    turretBone.add(mesh);
    const extra = new THREE.Mesh(
        new THREE.BufferGeometry(),
        new THREE.MeshStandardMaterial(),
    );
    extra.name = "Barrel";
    extra.userData.alamo = { alamoMesh: "Barrel", alamoShader: "MeshGloss.fx" };
    turretBone.add(extra);

    const hull: LoadedPart = {
        id: "hull",
        root,
        bones: new Map([
            ["root", rootBone],
            ["turret", turretBone],
        ]),
        bonesByIndex: new Map([
            [0, rootBone],
            [1, turretBone],
        ]),
        subjectId: ACTIVE_SUBJECT,
    };
    const host = {
        active: {
            mixer: null,
            clips: [],
            visibility: new Map(),
            nodeByName: new Map(),
            restPose: new Map(),
            action: null,
            fileHidden: new Set(),
        },
        alt: 0,
        lod: 0,
        collisionMeshes: new Set(),
        knownDecals: new Set(),
        shownDecals: new Set(),
        shieldRevealed: false,
        stealthed: false,
        particles: {
            particleSystems: new Map(),
            particlesVisible: true,
            setParticleSystemDrawn: () => undefined,
        },
        forEachPartMesh: (visit: (mesh: THREE.Mesh) => void) => {
            visit(mesh);
            visit(extra);
        },
        hull: () => hull,
        skeleton: () => [
            { index: 0, name: "Root", parent: -1, visible: true },
            { index: 1, name: "Turret", parent: 0, visible: true },
        ],
    } as unknown as RowsViewport;
    return { rows: new ModelRows(host), mesh, extra };
}

describe("ModelRows", () => {
    it("lists a bone and the mesh of its own name as one row, and a second mesh under it", () => {
        const { rows } = rowsWith();

        const items = rows.treeItems();
        const turret = items.find((item) => item.name === "Turret");
        const barrel = items.find((item) => item.name === "Barrel");

        assert.ok(turret, "no Turret row");
        assert.ok(barrel, "no Barrel row");
        assert.equal(turret.id, "bone:1");
        assert.equal(barrel.parentId, turret.id);
    });

    it("hides a row's mesh and gives it back to the model", () => {
        const { rows, extra } = rowsWith();
        const barrel = rows.treeItems().find((item) => item.name === "Barrel")!;

        rows.setItemsVisible([barrel.id], false);
        assert.equal(meshDrawn(extra), false);
        assert.deepEqual(rows.hiddenItemIds(), [barrel.id]);

        rows.applyRowOverrides([{ row: barrel.id, override: null }]);
        assert.equal(meshDrawn(extra), true);
        assert.deepEqual(rows.rowOverrideEntries(), []);
    });

    it("takes a bone's meshes with it when the bone is switched off", () => {
        const { rows, mesh, extra } = rowsWith();
        rows.treeItems();

        rows.setItemsVisible(["bone:1"], false);

        assert.equal(meshDrawn(mesh), false);
        assert.equal(meshDrawn(extra), false);
    });

    it("hands every row back to the model when a clip starts", () => {
        const { rows, extra } = rowsWith();
        rows.treeItems();
        rows.setItemsVisible(["bone:1"], false);
        rows.setItemsVisible(["bone:0"], true);

        rows.clearRowOverrides();

        assert.deepEqual(rows.hiddenItemIds(), []);
        assert.deepEqual(rows.shownItemIds(), []);
        assert.equal(meshDrawn(extra), true);
    });
});
