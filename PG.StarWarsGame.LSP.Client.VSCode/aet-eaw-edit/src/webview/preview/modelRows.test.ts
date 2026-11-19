// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { type MaterialExtras } from "./materials";
import { meshDrawn } from "./meshVisibility";
import { ModelRows } from "./modelRows";
import { type PreviewRules, UnitRules } from "./unitRules";
import { type VisibilityTrack } from "./boneVisibility";
import { standInContext } from "./viewportContext.testing";

/** What a test varies about {@link rowsWith}'s model. */
interface RowsOptions {
    /** Whose rules the preview takes. A unit preview's by default. */
    rules?: PreviewRules;
    /** The file's extras for the mesh sharing the turret bone's row. */
    turret?: MaterialExtras;
    /** The file's extras for the second mesh on that bone. */
    barrel?: MaterialExtras;
    /** The clips' visibility tracks, by clip name. */
    visibility?: Map<string, VisibilityTrack>;
    /** The clip playing, by name, at this many seconds; nothing plays when absent. */
    playing?: { clip: string; time: number };
}

/**
 * Rows over a stand-in viewport: a hull with a root bone, a child bone carrying a mesh of its own name
 * and a second mesh, and a bone below that carrying a mesh of its own name; no particle systems,
 * nothing animated unless asked, every level and gate open.
 */
function rowsWith(options: RowsOptions = {}): {
    rows: ModelRows;
    mesh: THREE.Mesh;
    extra: THREE.Mesh;
    gun: THREE.Mesh;
    turretBone: THREE.Bone;
    unit: UnitRules;
} {
    const root = new THREE.Group();
    const rootBone = new THREE.Bone();
    rootBone.name = "Root#0";
    const turretBone = new THREE.Bone();
    turretBone.name = "Turret#1";
    rootBone.add(turretBone);
    root.add(rootBone);
    const gunBone = new THREE.Bone();
    gunBone.name = "Gun#2";
    turretBone.add(gunBone);

    // A mesh of the bone's own name shares its row; a second one on the same bone is a child row.
    const mesh = new THREE.Mesh(
        new THREE.BufferGeometry(),
        new THREE.MeshStandardMaterial(),
    );
    mesh.name = "Turret";
    mesh.userData.aetTreeKey = "turret";
    mesh.userData.alamo = options.turret ?? {
        alamoMesh: "Turret",
        alamoShader: "MeshGloss.fx",
    };
    turretBone.add(mesh);
    const extra = new THREE.Mesh(
        new THREE.BufferGeometry(),
        new THREE.MeshStandardMaterial(),
    );
    extra.name = "Barrel";
    extra.userData.aetTreeKey = "barrel";
    extra.userData.alamo = options.barrel ?? {
        alamoMesh: "Barrel",
        alamoShader: "MeshGloss.fx",
    };
    turretBone.add(extra);
    const gun = new THREE.Mesh(
        new THREE.BufferGeometry(),
        new THREE.MeshStandardMaterial(),
    );
    gun.name = "Gun";
    gun.userData.aetTreeKey = "gun";
    gun.userData.alamo = { alamoMesh: "Gun", alamoShader: "MeshGloss.fx" };
    gunBone.add(gun);

    const hull: LoadedPart = {
        id: "hull",
        root,
        bones: new Map([
            ["root", rootBone],
            ["turret", turretBone],
            ["gun", gunBone],
        ]),
        bonesByIndex: new Map([
            [0, rootBone],
            [1, turretBone],
            [2, gunBone],
        ]),
        subjectId: ACTIVE_SUBJECT,
    };
    const skeleton = [
        { index: 0, name: "Root", parent: -1, visible: true },
        { index: 1, name: "Turret", parent: 0, visible: true },
        { index: 2, name: "Gun", parent: 1, visible: true },
    ];
    const unit = new UnitRules(
        standInContext({
            levels: { applyLevels: () => undefined },
            queries: { skeleton: () => skeleton },
            assembly: { hull: () => hull },
        }),
    );
    unit.setRules(options.rules ?? "game");
    const context = standInContext({
        animation: {
            active: {
                mixer: null,
                clips: [],
                visibility: options.visibility ?? new Map(),
                nodeByName: new Map(),
                restPose: new Map(),
                action:
                    options.playing === undefined
                        ? null
                        : {
                              getClip: () => ({ name: options.playing!.clip }),
                              time: options.playing.time,
                          },
                fileHidden: new Set(),
            },
        },
        levels: {
            alt: 0,
            lod: 0,
        },
        rules: unit,
        particles: {
            particleSystems: new Map(),
            particlesVisible: true,
            setParticleSystemDrawn: () => undefined,
        },
        assembly: {
            forEachPartMesh: (visit: (mesh: THREE.Mesh) => void) => {
                visit(mesh);
                visit(extra);
                visit(gun);
            },
            hull: () => hull,
        },
        queries: {
            skeleton: () => [
                { index: 0, name: "Root", parent: -1, visible: true },
                { index: 1, name: "Turret", parent: 0, visible: true },
                { index: 2, name: "Gun", parent: 1, visible: true },
            ],
        },
    });
    return { rows: new ModelRows(context), mesh, extra, gun, turretBone, unit };
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

    it("draws the stealth shell and the shield mesh a model file marks visible in a model preview", () => {
        const { rows, mesh, extra } = rowsWith({
            rules: "file",
            turret: { alamoMesh: "stealth", alamoShader: "MeshShield.fx" },
            barrel: { alamoMesh: "shield", alamoShader: "MeshShield.fx" },
        });

        rows.treeItems();

        assert.equal(meshDrawn(mesh), true, "the stealth shell is hidden");
        assert.equal(meshDrawn(extra), true, "the shield mesh is hidden");
    });

    it("hides the same shell and shield in a unit preview until an ability asks for them", () => {
        const { rows, mesh, extra, unit } = rowsWith({
            rules: "game",
            turret: { alamoMesh: "stealth", alamoShader: "MeshShield.fx" },
            barrel: { alamoMesh: "shield", alamoShader: "MeshShield.fx" },
        });

        rows.treeItems();
        assert.equal(meshDrawn(mesh), false);
        assert.equal(meshDrawn(extra), false);

        unit.setStealthed(true);
        unit.setShieldRevealed(true);
        rows.resolveRows();

        assert.equal(meshDrawn(mesh), true, "the cloak did not show the shell");
        assert.equal(
            meshDrawn(extra),
            false,
            "a cloaked unit draws its shield",
        );
    });

    it("stands a unit in its idle clip, and a model file in its own flags", () => {
        const visibility = new Map<string, VisibilityTrack>([
            [
                "Model_IDLE_00",
                { fps: 15, bones: new Map([["Turret#1", "00"]]) },
            ],
        ]);
        const model = rowsWith({ rules: "file", visibility });
        const unit = rowsWith({ rules: "game", visibility });

        model.rows.treeItems();
        unit.rows.treeItems();

        assert.equal(
            meshDrawn(model.mesh),
            true,
            "the model stood in its idle",
        );
        assert.equal(meshDrawn(unit.mesh), false, "the unit ignored its idle");
    });

    it("hides only the parts on a bone a clip hides, never the parts below it", () => {
        const visibility = new Map<string, VisibilityTrack>([
            ["Model_DIE_00", { fps: 15, bones: new Map([["Turret#1", "0"]]) }],
        ]);
        const { rows, mesh, extra, gun, turretBone } = rowsWith({
            visibility,
            playing: { clip: "Model_DIE_00", time: 0 },
        });

        rows.treeItems();

        assert.equal(meshDrawn(mesh), false, "the bone's own mesh is drawn");
        assert.equal(meshDrawn(extra), false, "its second mesh is drawn");
        assert.equal(
            meshDrawn(gun),
            true,
            "the mesh on the bone below is hidden",
        );
        assert.equal(
            turretBone.visible,
            true,
            "the bone prunes what is below it",
        );
    });

    it("never lets a playing clip show a mesh the file hides", () => {
        const visibility = new Map<string, VisibilityTrack>([
            ["Model_IDLE_00", { fps: 15, bones: new Map([["Turret#1", "1"]]) }],
        ]);
        const { rows, mesh } = rowsWith({
            visibility,
            playing: { clip: "Model_IDLE_00", time: 0 },
            turret: {
                alamoMesh: "Turret",
                alamoShader: "MeshGloss.fx",
                alamoHidden: true,
            },
        });

        rows.treeItems();

        assert.equal(meshDrawn(mesh), false);
    });

    it("says the file hides a mesh the file hides, not the level", () => {
        const { rows } = rowsWith({
            barrel: {
                alamoMesh: "Barrel",
                alamoShader: "MeshGloss.fx",
                alamoHidden: true,
            },
        });

        const barrel = rows.treeItems().find((item) => item.name === "Barrel");

        assert.equal(barrel?.visible, false);
        assert.equal(barrel?.because, "file");
    });

    it("still takes the rows below a bone the reader switches off", () => {
        const { rows, turretBone } = rowsWith();
        rows.treeItems();

        rows.setItemsVisible(["bone:1"], false);

        assert.equal(turretBone.visible, false);
        assert.equal(
            rows.treeItems().find((item) => item.name === "Gun")?.because,
            "ancestor:Turret",
        );
    });

    it("hides every mesh on a living hardpoint's Damage_Particles bone and below it, and nothing else", () => {
        const { rows, mesh, extra, gun, unit } = rowsWith();
        unit.setDamageParticles(new Map([["turret", false]]));

        rows.treeItems();

        assert.equal(meshDrawn(mesh), false, "the bone's own mesh is drawn");
        assert.equal(meshDrawn(extra), false, "its second mesh is drawn");
        assert.equal(meshDrawn(gun), false, "the mesh below it is drawn");

        unit.setDamageParticles(new Map([["turret", true]]));
        rows.resolveRows();

        assert.equal(meshDrawn(gun), true, "destroying it did not show them");
    });

    it("hides every mesh on a dead engine hardpoint's Engine_Particles bone and below it", () => {
        const { rows, mesh, extra, gun, unit } = rowsWith();
        rows.treeItems();
        assert.equal(meshDrawn(gun), true);

        unit.setEngineParticles(new Set(["turret"]));
        rows.resolveRows();

        assert.equal(meshDrawn(mesh), false, "the bone's own mesh is drawn");
        assert.equal(meshDrawn(extra), false, "its second mesh is drawn");
        assert.equal(meshDrawn(gun), false, "the mesh below it is drawn");
    });
});
