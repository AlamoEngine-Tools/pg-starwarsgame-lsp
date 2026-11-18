// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { PartAssembly } from "./partAssembly";
import { type ViewportContext } from "./viewportContext";

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
    billboards: THREE.Object3D[];
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
    const billboards: THREE.Object3D[] = [];
    const context = {
        levels: { alt: 0, lod: 0 },
        modelRoot,
        scaledRoot: modelRoot,
        parts: new Map([
            ["hull", hull],
            ["wreck:1", wreck],
        ]),
        animation: {
            subjects: new Map(),
            subjectAnimation: (subjectId: string) => {
                dropped.push(subjectId);
                return {
                    restPose: new Map(),
                    fileHidden: new Set(),
                    nodeByName: new Map(),
                };
            },
        },
        environment: { shadowVolumes: { remove: () => undefined } },
        cameraRig: {
            billboards,
            dropBillboards: (nodes: ReadonlySet<THREE.Object3D>) => {
                const kept = billboards.filter((node) => !nodes.has(node));
                billboards.length = 0;
                billboards.push(...kept);
            },
        },
        particles: {
            holdEffectsOf: (id: string) => held.push(id),
            particleSystems: new Map(),
            removeParticleSystem: () => undefined,
        },
    } as unknown as ViewportContext;
    return {
        assembly: new PartAssembly(context),
        modelRoot,
        hull,
        wreck,
        held,
        dropped,
        billboards,
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

describe("PartAssembly", () => {
    it("knows the hull and each subject's parts", () => {
        const { assembly, hull, wreck } = assemblyWith();

        assert.equal(assembly.hull(), hull);
        assert.deepEqual(assembly.partsOf("wreck:1"), [wreck]);
        assert.equal(assembly.hasPart("wreck:1"), true);
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

    it("takes a removed part out of the scene and its registrations with it", () => {
        const { assembly, modelRoot, wreck, dropped } = assemblyWith();

        assembly.removePart("wreck:1");

        assert.equal(assembly.hasPart("wreck:1"), false);
        assert.equal(modelRoot.children.includes(wreck.root), false);
        assert.ok(dropped.includes("wreck:1"));
    });

    it("stops turning a removed part's billboards", () => {
        const { assembly, wreck, billboards } = assemblyWith();
        const card = new THREE.Object3D();
        wreck.root.add(card);
        billboards.push(card);

        assembly.removePart("wreck:1");

        assert.deepEqual(billboards, []);
    });

    it("finds a fire bone on the hull before the attached model, and on the model when the hull has none", () => {
        const { assembly, hull, wreck } = assemblyWith();
        const turret = hull.bones.get("turret")!;

        assert.equal(assembly.fireBoneAttachment("wreck:1", "Turret"), turret);
        assert.ok(
            under(assembly.fireBoneAttachment("wreck:1", "Muzzle"), wreck.root),
        );
    });
});
