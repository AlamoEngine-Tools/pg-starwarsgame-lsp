// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { meshDrawn } from "./meshVisibility";
import { type ParsedModel } from "./parsedModel";
import { PartAssembly } from "./partAssembly";
import { standInContext } from "./viewportContext.testing";

/**
 * An assembly over a stand-in viewport whose scene already holds a hull - a root with a turret bone at
 * index 1 - and a wreck that is a subject of its own. Nothing is loaded here; the parts are put in place
 * the way `addPart` leaves them. What the other layers are told is recorded.
 */
function assemblyWith(): {
    assembly: PartAssembly;
    modelRoot: THREE.Group;
    hull: LoadedPart;
    wreck: LoadedPart;
    held: string[];
    dropped: string[];
    billboards: THREE.Object3D[][];
    refreshed: () => number;
} {
    const modelRoot = new THREE.Group();
    const turret = new THREE.Bone();
    turret.name = "Turret#1";
    const hullRoot = new THREE.Group();
    hullRoot.add(turret);
    modelRoot.add(hullRoot);
    const hull: LoadedPart = {
        id: "hull",
        root: hullRoot,
        bones: new Map([["turret", turret]]),
        bonesByIndex: new Map([[1, turret]]),
        subjectId: ACTIVE_SUBJECT,
    };
    const wreckRoot = new THREE.Group();
    modelRoot.add(wreckRoot);
    const wreck: LoadedPart = {
        id: "wreck:1",
        root: wreckRoot,
        bones: new Map(),
        bonesByIndex: new Map(),
        subjectId: "wreck:1",
    };
    const held: string[] = [];
    const dropped: string[] = [];
    const billboards: THREE.Object3D[][] = [];
    let refreshes = 0;
    const context = standInContext({
        levels: { alt: 0, lod: 0 },
        modelRoot,
        scaledRoot: modelRoot,
        parts: new Map([
            ["hull", hull],
            ["wreck:1", wreck],
        ]),
        animation: {
            unregisterPart: (subjectId: string) => dropped.push(subjectId),
            forgetSubject: (subjectId: string) =>
                dropped.push(`forget ${subjectId}`),
        },
        environment: { dropShadowVolumes: () => undefined },
        cameraRig: {
            dropBillboards: (nodes: ReadonlySet<THREE.Object3D>) =>
                billboards.push([...nodes]),
        },
        particles: {
            // Only the wreck carries effects.
            holdEffectsOf: (id: string) => {
                held.push(id);
                return id === "wreck:1";
            },
            particleSystems: new Map(),
            removeParticleSystem: () => undefined,
        },
        rows: {
            refreshRows: () => {
                refreshes++;
            },
        },
    });
    return {
        assembly: new PartAssembly(context),
        modelRoot,
        hull,
        wreck,
        held,
        dropped,
        billboards,
        refreshed: () => refreshes,
    };
}

/** Whether `node` is `ancestor` or hangs under it. */
const under = (node: THREE.Object3D, ancestor: THREE.Object3D): boolean => {
    for (let at: THREE.Object3D | null = node; at !== null; at = at.parent) {
        if (at === ancestor) {
            return true;
        }
    }
    return false;
};

/**
 * An assembly that LOADS parts, over a stand-in viewport whose layers do nothing. `hull` is a model
 * whose root has a `Turret` bone at index 1, `turret` a model of its own.
 */
function loaderWith(): {
    assembly: PartAssembly;
    modelRoot: THREE.Group;
    parts: Map<string, LoadedPart>;
    scene: (name: "hull" | "turret") => ParsedModel;
} {
    const modelRoot = new THREE.Group();
    const scaledRoot = new THREE.Group();
    modelRoot.add(scaledRoot);
    const parts = new Map<string, LoadedPart>();
    const make = (name: "hull" | "turret"): THREE.Group => {
        const root = new THREE.Group();
        if (name === "hull") {
            const bone = new THREE.Bone();
            bone.name = "Turret#1";
            bone.position.set(0, 0, 5);
            root.add(bone);
        } else {
            const bone = new THREE.Bone();
            bone.name = "Barrel#1";
            root.add(bone);
        }
        return root;
    };
    const idle = () => undefined;
    const assembly = new PartAssembly(
        standInContext({
            modelRoot,
            scaledRoot,
            parts,
            levels: { alt: 0, lod: 0, applyLevels: idle },
            materials: {
                applyMaterial: idle,
                applyWireframe: idle,
                applyStoredState: idle,
                bindLoadedTextures: idle,
            },
            environment: {
                collectShadowVolumes: idle,
                dropShadowVolumes: idle,
            },
            cameraRig: { collectBillboards: idle, dropBillboards: idle },
            animation: {
                registerPart: idle,
                unregisterPart: idle,
                forgetSubject: idle,
            },
            particles: {
                particleSystems: new Map(),
                removeParticleSystem: idle,
                holdEffectsOf: idle,
            },
        }),
    );
    return {
        assembly,
        modelRoot,
        parts,
        scene: (name) => ({
            scene: make(name),
            clips: [],
            visibility: new Map(),
        }),
    };
}

describe("PartAssembly", () => {
    // One model, read once, is the hull of nothing and the turret of everything that mounts it.
    it("builds every part of one model from a copy, and leaves the model as it was read", async () => {
        const { assembly, parts, scene } = loaderWith();
        const turret = scene("turret");
        await assembly.addPart("hull", scene("hull"));

        await assembly.addPart("hp:Left", turret, "hull", "Turret");
        await assembly.addPart("hp:Right", turret, "hull", "Turret");

        const left = parts.get("hp:Left")!;
        const right = parts.get("hp:Right")!;
        assert.notEqual(left.root, right.root);
        assert.notEqual(left.root, turret.scene);
        assert.notEqual(left.bones.get("barrel"), right.bones.get("barrel"));
        assert.equal(turret.scene.parent, null, "the model kept is in a scene");
        assert.equal(turret.scene.children.length, 1);
    });

    it("hangs an attached model on its bone when the model it hangs on arrives after it", async () => {
        const { assembly, modelRoot, parts, scene } = loaderWith();

        await assembly.addPart("hp:Gun", scene("turret"), "hull", "Turret");
        const turret = parts.get("hp:Gun")!;
        assert.equal(
            under(turret.root, modelRoot),
            false,
            "a turret whose hull has not arrived is in the scene",
        );

        await assembly.addPart("hull", scene("hull"));
        const bone = parts.get("hull")!.bones.get("turret")!;

        assert.equal(turret.root.parent, bone, "the turret is not on its bone");
        assert.ok(
            turret.root
                .getWorldPosition(new THREE.Vector3())
                .distanceTo(bone.getWorldPosition(new THREE.Vector3())) < 1e-6,
        );
    });

    it("drops a model still waiting for its hull with the scene, or when it is removed", async () => {
        const { assembly, parts, scene } = loaderWith();

        await assembly.addPart("hp:Gun", scene("turret"), "hull", "Turret");
        await assembly.addPart("hp:Other", scene("turret"), "hull", "Turret");
        const other = parts.get("hp:Other")!;
        assembly.removePart("hp:Other");

        assembly.forgetParts();
        const gun = parts.get("hp:Gun");
        assert.equal(gun, undefined);

        await assembly.addPart("hull", scene("hull"));
        const bone = parts.get("hull")!.bones.get("turret")!;
        assert.equal(
            bone.children.length,
            0,
            "a forgotten or removed turret was hung on the new hull",
        );
        assert.equal(other.root.parent, null);
    });

    it("knows the hull and each subject's parts", () => {
        const { assembly, hull, wreck } = assemblyWith();

        assert.equal(assembly.hull(), hull);
        assert.deepEqual(assembly.partsOf("wreck:1"), [wreck]);
        assert.equal(assembly.hasPart("wreck:1"), true);
    });

    it("knows the hull when an attached hardpoint model landed before it", () => {
        const hullRoot = new THREE.Group();
        const turretRoot = new THREE.Group();
        const turret: LoadedPart = {
            id: "hp:HP_Gun",
            root: turretRoot,
            bones: new Map(),
            bonesByIndex: new Map(),
            subjectId: ACTIVE_SUBJECT,
            attached: true,
        };
        const hull: LoadedPart = {
            id: "hull",
            root: hullRoot,
            bones: new Map(),
            bonesByIndex: new Map(),
            subjectId: ACTIVE_SUBJECT,
        };
        const assembly = new PartAssembly(
            standInContext({
                parts: new Map([
                    ["hp:HP_Gun", turret],
                    ["hull", hull],
                ]),
            }),
        );

        assert.equal(assembly.hull(), hull);
    });

    it("attaches to a bone by index, to the part root without a bone, and to the model without a part", () => {
        const { assembly, modelRoot, hull } = assemblyWith();
        const turret = hull.bonesByIndex.get(1)!;

        assert.ok(under(assembly.attachmentFor("hull", "Turret", 1), turret));
        assert.equal(assembly.attachmentFor("hull"), hull.root);
        assert.equal(assembly.attachmentFor(), modelRoot);
    });

    it("reports an attachment whose bone never arrived", () => {
        const { assembly } = assemblyWith();

        assembly.attachmentFor("hull", "Missing_Bone");

        assert.equal(assembly.unresolvedAttachments().length, 1);
    });

    it("hides a part and holds its effects, and shows it again", () => {
        const { assembly, wreck, held } = assemblyWith();

        assembly.setPartHidden("wreck:1", true);
        assert.equal(wreck.root.visible, false);
        assert.ok(assembly.hiddenParts.has("wreck:1"));
        assert.deepEqual(held, ["wreck:1"]);

        assembly.setPartHidden("wreck:1", false);
        assert.equal(wreck.root.visible, true);
        assert.equal(assembly.hiddenParts.has("wreck:1"), false);
    });

    /**
     * A part's effects are anchored to the model root, so the rows decide whether they draw; showing a
     * death clone that was held off screen has to have them say so again.
     */
    it("asks the rows again when it hides or shows a part that carries effects, and only then", () => {
        const { assembly, refreshed } = assemblyWith();

        assembly.setPartHidden("hull", true);
        assert.equal(refreshed(), 0);

        assembly.setPartHidden("wreck:1", true);
        assembly.setPartHidden("wreck:1", false);
        assert.equal(refreshed(), 2);
    });

    it("takes a removed part out of the scene and its registrations with it", () => {
        const { assembly, modelRoot, wreck, dropped } = assemblyWith();

        assembly.removePart("wreck:1");

        assert.equal(assembly.hasPart("wreck:1"), false);
        assert.equal(modelRoot.children.includes(wreck.root), false);
        assert.deepEqual(dropped, ["wreck:1", "forget wreck:1"]);
    });

    it("stops turning a removed part's billboards", () => {
        const { assembly, wreck, billboards } = assemblyWith();
        const card = new THREE.Object3D();
        wreck.root.add(card);

        assembly.removePart("wreck:1");

        assert.equal(billboards.length, 1);
        assert.ok(billboards[0].includes(card));
    });

    it("finds a fire bone on the hull before the attached model, and on the model when the hull has none", () => {
        const { assembly, hull, wreck } = assemblyWith();
        const turret = hull.bones.get("turret")!;

        assert.equal(assembly.fireBoneAttachment("wreck:1", "Turret"), turret);
        assert.ok(
            under(assembly.fireBoneAttachment("wreck:1", "Muzzle"), wreck.root),
        );
    });

    it("takes every part out of the scene, then forgets them, the hidden ones and the attachments asked for", () => {
        const { assembly, modelRoot, wreck } = assemblyWith();
        assembly.setPartHidden("wreck:1", true);
        assembly.attachmentFor("hull", "Missing_Bone");

        assembly.disposeParts();
        assert.equal(modelRoot.children.includes(wreck.root), false);
        assert.equal(assembly.hasPart("wreck:1"), true);

        assembly.forgetParts();
        assert.equal(assembly.hasPart("wreck:1"), false);
        assert.equal(assembly.hiddenParts.size, 0);
        assert.deepEqual(assembly.unresolvedAttachments(), []);
    });

    it("draws a death clone's parts by their own switches, never hiding what is below a hidden bone", () => {
        // A clone of two bones, Hatch and Inner below it: Hatch carries a mesh, Inner carries a mesh
        // and a plate the file hides. The clip hides Hatch and shows Inner.
        const root = new THREE.Group();
        const hatchBone = new THREE.Bone();
        hatchBone.name = "Hatch#1";
        const innerBone = new THREE.Bone();
        innerBone.name = "Inner#2";
        hatchBone.add(innerBone);
        root.add(hatchBone);
        const mesh = (name: string, extras: Record<string, unknown>) => {
            const made = new THREE.Mesh(
                new THREE.BufferGeometry(),
                new THREE.MeshBasicMaterial(),
            );
            made.name = name;
            made.userData.alamo = { alamoMesh: name, ...extras };
            return made;
        };
        const hatch = mesh("Hatch", {});
        hatchBone.add(hatch);
        const inner = mesh("Inner", {});
        innerBone.add(inner);
        const plate = mesh("Plate", { alamoHidden: true });
        innerBone.add(plate);
        const clone: LoadedPart = {
            id: "deathclone:X",
            root,
            bones: new Map([
                ["hatch", hatchBone],
                ["inner", innerBone],
            ]),
            bonesByIndex: new Map([
                [1, hatchBone],
                [2, innerBone],
            ]),
            subjectId: "deathclone:X",
        };
        const assembly = new PartAssembly(
            standInContext({
                levels: { alt: 0, lod: 0 },
                parts: new Map([["deathclone:X", clone]]),
                animation: {
                    subjectAnimation: () => ({
                        action: {
                            getClip: () => ({ name: "x_die_00" }),
                            time: 0,
                        },
                        visibility: new Map([
                            [
                                "x_die_00",
                                {
                                    fps: 30,
                                    bones: new Map([
                                        ["Hatch#1", "0"],
                                        ["Inner#2", "1"],
                                    ]),
                                },
                            ],
                        ]),
                    }),
                },
                environment: {
                    shadowVolumes: { setCounting: () => undefined },
                },
            }),
        );

        assembly.applyOwnFileVisibility(clone);

        assert.equal(
            meshDrawn(hatch),
            false,
            "the hidden bone's mesh is drawn",
        );
        assert.equal(meshDrawn(inner), true, "the mesh below it is hidden");
        assert.equal(
            meshDrawn(plate),
            false,
            "the clip showed a file-hidden plate",
        );
        assert.equal(
            hatchBone.visible,
            true,
            "the hidden bone prunes its subtree",
        );
    });

    it("hides a wreck's parts on a bone its file hides until its clip runs", () => {
        const root = new THREE.Group();
        const hatchBone = new THREE.Bone();
        hatchBone.name = "Hatch#1";
        hatchBone.userData.alamoBoneHidden = true;
        const innerBone = new THREE.Bone();
        innerBone.name = "Inner#2";
        hatchBone.add(innerBone);
        root.add(hatchBone);
        const mesh = (name: string) => {
            const made = new THREE.Mesh(
                new THREE.BufferGeometry(),
                new THREE.MeshBasicMaterial(),
            );
            made.userData.alamo = { alamoMesh: name };
            return made;
        };
        const hatch = mesh("Hatch");
        hatchBone.add(hatch);
        const inner = mesh("Inner");
        innerBone.add(inner);
        const wreck: LoadedPart = {
            id: "wreck:2",
            root,
            bones: new Map(),
            bonesByIndex: new Map([
                [1, hatchBone],
                [2, innerBone],
            ]),
            subjectId: "wreck:2",
        };
        let action: unknown = null;
        const assembly = new PartAssembly(
            standInContext({
                levels: { alt: 0, lod: 0 },
                parts: new Map([["wreck:2", wreck]]),
                animation: {
                    subjectAnimation: () => ({
                        action,
                        visibility: new Map([
                            ["x_die_00", { fps: 30, bones: new Map() }],
                        ]),
                    }),
                },
                environment: {
                    shadowVolumes: { setCounting: () => undefined },
                },
            }),
        );

        assembly.applyOwnFileVisibility(wreck);
        assert.equal(
            meshDrawn(hatch),
            false,
            "the file-hidden bone's mesh is drawn",
        );
        assert.equal(meshDrawn(inner), true, "the mesh below it is hidden");

        action = { getClip: () => ({ name: "x_die_00" }), time: 0 };
        assembly.applyOwnFileVisibility(wreck);
        assert.equal(
            meshDrawn(hatch),
            true,
            "a clip with no track for it hid it",
        );
    });
});
