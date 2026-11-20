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
    /**
     * The bones the second mesh is skinned to, by index, in the order of its skin; it is rigid on
     * the turret bone when absent.
     */
    skinnedTo?: number[];
    /** The bones, by index, the model file marks invisible. */
    boneHidden?: number[];
    /** A model effect anchored on this bone, by index, under a name of its own. */
    effectOn?: number;
    /** The part that effect belongs to; the hull when absent. */
    effectPart?: string;
    /** The parts that are loaded and held off screen. */
    hiddenParts?: string[];
    /** Whether the hull is a model a hardpoint attaches, rather than the subject's own root. */
    attached?: boolean;
    /** The clips the model ships, by name, whether or not they carry a visibility track. */
    clips?: string[];
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
    counting: Map<THREE.Mesh, boolean>;
    effectDrawn: () => boolean | undefined;
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
    const byIndex = [rootBone, turretBone, gunBone];
    for (const index of options.boneHidden ?? []) {
        byIndex[index].userData.alamoBoneHidden = true;
    }
    const extra =
        options.skinnedTo === undefined
            ? new THREE.Mesh(
                  new THREE.BufferGeometry(),
                  new THREE.MeshStandardMaterial(),
              )
            : new THREE.SkinnedMesh(
                  new THREE.BufferGeometry(),
                  new THREE.MeshStandardMaterial(),
              );
    extra.name = "Barrel";
    extra.userData.aetTreeKey = "barrel";
    extra.userData.alamo = options.barrel ?? {
        alamoMesh: "Barrel",
        alamoShader: "MeshGloss.fx",
    };
    if (extra instanceof THREE.SkinnedMesh) {
        // A skinned mesh hangs at the part root, not under a bone.
        extra.skeleton = new THREE.Skeleton(
            (options.skinnedTo ?? []).map((index) => byIndex[index]),
        );
        root.add(extra);
    } else {
        turretBone.add(extra);
    }
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
        attached: options.attached,
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
    const counting = new Map<THREE.Mesh, boolean>();
    let effectDrawn: boolean | undefined;
    const particleSystems = new Map<string, unknown>();
    if (options.effectOn !== undefined) {
        particleSystems.set("hull#0", {
            id: "hull#0",
            system: { name: "pe_glow", emitters: [] },
            origin: "model",
            state: { shown: true, writer: "file" },
            altVisible: true,
            lodVisible: true,
            attachBoneIndex: options.effectOn,
            attachToPartId: options.effectPart ?? "hull",
            instance: {
                anchorNode: byIndex[options.effectOn],
                root: new THREE.Group(),
                emitterIsHeat: () => [],
            },
        });
    }
    const context = standInContext({
        animation: {
            active: {
                mixer: null,
                clips: (options.clips ?? []).map((name) => ({ name })),
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
            },
        },
        levels: {
            alt: 0,
            lod: 0,
        },
        rules: unit,
        parts: new Map([["hull", hull]]),
        environment: {
            shadowVolumes: {
                setCounting: (source: THREE.Mesh, on: boolean) =>
                    counting.set(source, on),
            },
        },
        particles: {
            particleSystems,
            particlesVisible: true,
            setParticleSystemDrawn: (_id: string, drawn: boolean) => {
                effectDrawn = drawn;
            },
        },
        assembly: {
            forEachPartMesh: (visit: (mesh: THREE.Mesh) => void) => {
                visit(mesh);
                visit(extra);
                visit(gun);
            },
            hull: () => hull,
            hiddenParts: new Set(options.hiddenParts ?? []),
        },
        queries: {
            skeleton: () => [
                { index: 0, name: "Root", parent: -1, visible: true },
                { index: 1, name: "Turret", parent: 0, visible: true },
                { index: 2, name: "Gun", parent: 1, visible: true },
            ],
        },
    });
    return {
        rows: new ModelRows(context),
        mesh,
        extra,
        gun,
        turretBone,
        unit,
        counting,
        effectDrawn: () => effectDrawn,
    };
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

        unit.setActiveAbilities(new Set(["STEALTH"]));
        unit.setShieldRevealed(true);
        rows.resolveRows();

        assert.equal(meshDrawn(mesh), true, "the cloak did not show the shell");
        // A cloak writes BASEMESH and STEALTH parts and the attached models; the shield keeps the
        // state its own ability wrote.
        assert.equal(meshDrawn(extra), true, "the cloak hid the shield");
    });

    it("hides the meshes and effects of a model a hardpoint attaches while the unit is stealthed", () => {
        const { rows, gun, unit, effectDrawn } = rowsWith({
            attached: true,
            effectOn: 2,
        });
        rows.treeItems();
        assert.equal(meshDrawn(gun), true);

        unit.setActiveAbilities(new Set(["STEALTH"]));
        rows.resolveRows();

        assert.equal(meshDrawn(gun), false, "the attached mesh is drawn");
        assert.equal(effectDrawn(), false, "the attached effect is drawn");
    });

    /**
     * Switching a cloak takes back the reader's word on every row the cloak writes - its BASEMESH and
     * STEALTH parts and the attached models - and on nothing else.
     */
    it("gives the rows a cloak writes back to the model when the cloak is switched", () => {
        const { rows } = rowsWith({
            turret: { alamoMesh: "Ship_BaseMesh" },
        });
        const items = rows.treeItems();
        const barrel = items.find((item) => item.name === "Barrel")!;
        const base = items.find((item) => item.name === "Ship_BaseMesh")!;
        rows.setItemsVisible([base.id, barrel.id], false);

        rows.releaseAbilityRows({
            systemIds: [],
            shieldMesh: false,
            cloak: "STEALTH",
        });

        assert.deepEqual(rows.hiddenItemIds(), [barrel.id]);
    });

    it("gives an attached model's rows back to the model when stealth is switched", () => {
        const { rows } = rowsWith({ attached: true });
        const items = rows.treeItems();
        const barrel = items.find((item) => item.name === "Barrel")!;
        rows.setItemsVisible([barrel.id], false);

        rows.releaseAbilityRows({
            systemIds: [],
            shieldMesh: false,
            cloak: "STEALTH",
        });

        assert.deepEqual(rows.hiddenItemIds(), []);
    });

    it("stands a unit in its idle clip, and a model file in its own flags", () => {
        const visibility = new Map<string, VisibilityTrack>([
            [
                "Model_IDLE_00",
                { fps: 15, bones: new Map([["Turret#1", "00"]]) },
            ],
        ]);
        const clips = ["Model_IDLE_00"];
        const model = rowsWith({ rules: "file", visibility, clips });
        const unit = rowsWith({ rules: "game", visibility, clips });

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
        unit.setHardpoints({
            decals: new Set(),
            damageBones: new Set(["turret"]),
            deaths: [],
        });

        rows.treeItems();

        assert.equal(meshDrawn(mesh), false, "the bone's own mesh is drawn");
        assert.equal(meshDrawn(extra), false, "its second mesh is drawn");
        assert.equal(meshDrawn(gun), false, "the mesh below it is drawn");

        unit.setHardpoints({
            decals: new Set(),
            damageBones: new Set(["turret"]),
            deaths: [
                {
                    hardpoint: "HP_Turret",
                    decal: null,
                    damageBone: "turret",
                    engineBone: null,
                },
            ],
        });
        rows.resolveRows();

        assert.equal(meshDrawn(gun), true, "destroying it did not show them");
    });

    it("hides every mesh on a dead engine hardpoint's Engine_Particles bone and below it", () => {
        const { rows, mesh, extra, gun, unit } = rowsWith();
        rows.treeItems();
        assert.equal(meshDrawn(gun), true);

        unit.setHardpoints({
            decals: new Set(),
            damageBones: new Set(),
            deaths: [
                {
                    hardpoint: "HP_Engine",
                    decal: null,
                    damageBone: null,
                    engineBone: "turret",
                },
            ],
        });
        rows.resolveRows();

        assert.equal(meshDrawn(mesh), false, "the bone's own mesh is drawn");
        assert.equal(meshDrawn(extra), false, "its second mesh is drawn");
        assert.equal(meshDrawn(gun), false, "the mesh below it is drawn");
    });

    it("lets a skinned mesh follow the first bone it is skinned to, and only that one", () => {
        const track = (bits: Record<string, string>) =>
            new Map<string, VisibilityTrack>([
                [
                    "Model_DIE_00",
                    { fps: 15, bones: new Map(Object.entries(bits)) },
                ],
            ]);
        const playing = { clip: "Model_DIE_00", time: 0 };

        const first = rowsWith({
            skinnedTo: [1, 2],
            visibility: track({ "Turret#1": "0" }),
            playing,
        });
        first.rows.treeItems();
        assert.equal(
            meshDrawn(first.extra),
            false,
            "its first bone hid nothing",
        );

        const second = rowsWith({
            skinnedTo: [2, 1],
            visibility: track({ "Turret#1": "0" }),
            playing,
        });
        second.rows.treeItems();
        assert.equal(meshDrawn(second.extra), true, "a later bone hid it");
    });

    it("skips the root at the head of a skin list, so a skinned mesh follows the next bone", () => {
        const track = (bits: Record<string, string>) =>
            new Map<string, VisibilityTrack>([
                [
                    "Model_DIE_00",
                    { fps: 15, bones: new Map(Object.entries(bits)) },
                ],
            ]);
        const playing = { clip: "Model_DIE_00", time: 0 };

        const next = rowsWith({
            skinnedTo: [0, 1],
            visibility: track({ "Turret#1": "0" }),
            playing,
        });
        next.rows.treeItems();
        assert.equal(
            meshDrawn(next.extra),
            false,
            "hiding the next bone hid nothing",
        );

        const root = rowsWith({
            skinnedTo: [0, 1],
            visibility: track({ "Root#0": "0" }),
            playing,
        });
        root.rows.treeItems();
        assert.equal(meshDrawn(root.extra), true, "hiding the root hid it");
    });

    it("counts a shadow volume only while its own switches show it", () => {
        const visibility = new Map<string, VisibilityTrack>([
            ["Model_DIE_00", { fps: 15, bones: new Map([["Turret#1", "01"]]) }],
        ]);
        const { rows, extra, counting } = rowsWith({
            barrel: {
                alamoMesh: "Barrel_Shadow",
                alamoShader: "MeshShadowVolume.fx",
            },
            visibility,
            playing: { clip: "Model_DIE_00", time: 0 },
        });

        rows.treeItems();

        assert.equal(
            meshDrawn(extra),
            false,
            "a shadow volume is drawn as a mesh",
        );
        assert.equal(
            counting.get(extra),
            false,
            "a hidden bone's volume casts",
        );
    });

    it("stops a shadow volume counting while the unit is cloaked, and counts it again after", () => {
        const { rows, extra, counting, unit } = rowsWith({
            barrel: {
                alamoMesh: "Barrel_Shadow",
                alamoShader: "MeshShadowVolume.fx",
            },
        });

        rows.treeItems();
        assert.equal(counting.get(extra), true);

        unit.setCloakShadowsOff(true);
        rows.resolveRows();
        assert.equal(counting.get(extra), false);

        unit.setCloakShadowsOff(false);
        rows.resolveRows();
        assert.equal(counting.get(extra), true);
    });

    it("hides the parts on a bone the file hides while no clip runs, and nothing below it", () => {
        const { rows, mesh, extra, gun, turretBone } = rowsWith({
            rules: "file",
            boneHidden: [1],
        });

        const items = rows.treeItems();

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
            "the hidden bone prunes its subtree",
        );
        assert.equal(
            items.find((item) => item.name === "Barrel")?.because,
            "pose",
        );
    });

    it("lets a running clip decide instead, a bone it has no track for showing its parts", () => {
        const visibility = new Map<string, VisibilityTrack>([
            ["Model_ATTACK_00", { fps: 15, bones: new Map() }],
        ]);
        const { rows, mesh } = rowsWith({
            rules: "file",
            boneHidden: [1],
            visibility,
            playing: { clip: "Model_ATTACK_00", time: 0 },
        });

        rows.treeItems();

        assert.equal(meshDrawn(mesh), true);
    });

    it("applies the bone flag to a unit with no idle clip, and not to one standing in its idle", () => {
        const without = rowsWith({ rules: "game", boneHidden: [1] });
        without.rows.treeItems();
        assert.equal(
            meshDrawn(without.mesh),
            false,
            "a unit with no idle ignored the file",
        );

        const standing = rowsWith({
            rules: "game",
            boneHidden: [1],
            visibility: new Map<string, VisibilityTrack>([
                ["Model_IDLE_00", { fps: 15, bones: new Map() }],
            ]),
            clips: ["Model_IDLE_00"],
        });
        standing.rows.treeItems();
        assert.equal(
            meshDrawn(standing.mesh),
            true,
            "the idle a unit stands in did not decide",
        );
    });

    it("holds back an effect on a bone the file hides while no clip runs", () => {
        const hidden = rowsWith({
            rules: "file",
            boneHidden: [2],
            effectOn: 2,
        });
        hidden.rows.treeItems();
        assert.equal(hidden.effectDrawn(), false, "the effect runs");

        const shown = rowsWith({ rules: "file", effectOn: 2 });
        shown.rows.treeItems();
        assert.equal(shown.effectDrawn(), true);
    });

    /**
     * A death clone is fetched ahead of the death and held off screen, and its effects attach to it
     * there. Effects are anchored to the model root rather than parented, so hiding the clone's part
     * does not hide them: until the clone is spawned they draw nothing because of this rule.
     */
    it("draws no effect of a part that is held off screen, and draws it once the part is shown", () => {
        const held = rowsWith({
            effectOn: 2,
            effectPart: "deathclone:Wreck",
            hiddenParts: ["deathclone:Wreck"],
        });
        held.rows.treeItems();
        assert.equal(
            held.effectDrawn(),
            false,
            "the held clone's effect is drawn",
        );

        const spawned = rowsWith({
            effectOn: 2,
            effectPart: "deathclone:Wreck",
        });
        spawned.rows.treeItems();
        assert.equal(spawned.effectDrawn(), true);
    });

    /** The subject's own part hidden - the hull, once the unit has died - is a rule of its own. */
    it("leaves the subject's own effects to their rules when the subject's part is hidden", () => {
        const { rows, effectDrawn } = rowsWith({
            effectOn: 2,
            hiddenParts: ["hull"],
        });
        rows.treeItems();

        assert.equal(effectDrawn(), true);
    });

    it("stands a unit in an idle clip that hides nothing, rather than in its base pose", () => {
        // A clip that hides no bone carries no visibility track at all, so the model's clips are
        // what say whether it has an idle - not its tracks.
        const { rows, mesh } = rowsWith({
            rules: "game",
            boneHidden: [1],
            clips: ["Model_IDLE_00"],
        });

        rows.treeItems();

        assert.equal(meshDrawn(mesh), true, "the base pose decided");
    });
});
