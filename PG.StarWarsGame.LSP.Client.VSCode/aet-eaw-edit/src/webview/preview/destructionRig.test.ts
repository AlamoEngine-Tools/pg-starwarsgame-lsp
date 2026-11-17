// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type PreviewBreakoffProp } from "../../protocol/modelPreview";
import { type DestructionViewport, DestructionRig } from "./destructionRig";
import {
    ACTIVE_SUBJECT,
    BREAKOFF_ATTACHMENT,
    type LoadedPart,
} from "./loadedPart";
import { type ViewportContext } from "./viewportContext";

const PROP: PreviewBreakoffProp = {
    id: "Hardpoint_Breakoff_Test",
    modelRef: "Test_Wreck.alo",
    resolved: true,
    movementVector: { x: 0, y: -20, z: 0 },
    facingRotateVector: { x: 0, y: 0, z: 90 },
    minLifetimeSeconds: 2,
    maxLifetimeSeconds: 4,
    attachedParticle: null,
    deathExplosions: null,
    removeUponDeath: true,
    animations: [],
    particles: [],
};

/**
 * A rig over a stand-in viewport: a hull at the origin with a bone 10 units up, and an `addPart` that
 * hangs a fresh group under the model and records how it was asked for. What is removed and what expires
 * is recorded too.
 */
function rigWith(): {
    destruction: DestructionRig;
    hull: LoadedPart;
    added: { id: string; subjectId?: string; at?: THREE.Vector3 }[];
    removed: string[];
    expired: string[];
    wreck: () => THREE.Group;
} {
    const modelRoot = new THREE.Group();
    const bone = new THREE.Bone();
    bone.position.set(0, 10, 0);
    const hullRoot = new THREE.Group();
    hullRoot.add(bone);
    modelRoot.add(hullRoot);
    modelRoot.updateMatrixWorld(true);
    const hull: LoadedPart = {
        id: "hull",
        root: hullRoot,
        bones: new Map([["hp_bone", bone]]),
        bonesByIndex: new Map([[3, bone]]),
        subjectId: ACTIVE_SUBJECT,
    };
    const parts = new Map<string, LoadedPart>([["hull", hull]]);
    const added: { id: string; subjectId?: string; at?: THREE.Vector3 }[] = [];
    const removed: string[] = [];
    const expired: string[] = [];
    let last = new THREE.Group();
    const context = {
        assembly: {
            attachmentFor: () => bone,
            addPart: async (
                id: string,
                _glb: string,
                _to?: string,
                _bone?: string,
                options?: {
                    subjectId?: string;
                    pose?: { position: THREE.Vector3 };
                },
            ) => {
                added.push({
                    id,
                    subjectId: options?.subjectId,
                    at: options?.pose?.position,
                });
                last = new THREE.Group();
                last.add(
                    new THREE.Mesh(
                        new THREE.BufferGeometry(),
                        new THREE.MeshBasicMaterial(),
                    ),
                );
                modelRoot.add(last);
                parts.set(id, {
                    id,
                    root: last,
                    bones: new Map(),
                    bonesByIndex: new Map(),
                    subjectId: id,
                });
                return last;
            },
            removePart: (partId: string) => removed.push(partId),
        },
        parts,
    } as unknown as ViewportContext;
    const host = {
        modelScale: 1,
        onBreakoffExpired: (key: string) => expired.push(key),
        dropRegistrations: () => undefined,
    } as unknown as DestructionViewport;
    return {
        destruction: new DestructionRig(context, host),
        hull,
        added,
        removed,
        expired,
        wreck: () => last,
    };
}

describe("DestructionRig", () => {
    it("loads a wreck as a subject of its own, where its bone is", async () => {
        const { destruction, added } = rigWith();

        await destruction.addBreakoff(
            "HP_1",
            "glb",
            { partId: "hull", bone: "HP_Bone", boneIndex: 3 },
            PROP,
        );

        assert.equal(added.length, 1);
        assert.equal(added[0].id, `${BREAKOFF_ATTACHMENT}HP_1`);
        assert.equal(added[0].subjectId, `${BREAKOFF_ATTACHMENT}HP_1`);
        assert.deepEqual(added[0].at?.toArray(), [0, 10, 0]);
        assert.equal(destruction.hasBreakoff("HP_1"), true);
    });

    it("flies a wreck off its bone and lets it expire at the end of its life", async () => {
        const { destruction, expired, wreck } = rigWith();
        await destruction.addBreakoff(
            "HP_1",
            "glb",
            { partId: "hull", bone: "HP_Bone", boneIndex: 3 },
            PROP,
        );

        destruction.advanceBreakoffs(1);
        assert.notDeepEqual(wreck().position.toArray(), [0, 10, 0]);
        assert.deepEqual(expired, []);

        destruction.advanceBreakoffs(2.5);
        assert.deepEqual(expired, ["HP_1"]);
        assert.equal(wreck().children.length, 0);
    });

    it("takes a wreck out through the assembly when it is cleared", async () => {
        const { destruction, removed } = rigWith();
        await destruction.addBreakoff("HP_1", "glb", { partId: "hull" }, PROP);
        await destruction.addBreakoff("HP_2", "glb", { partId: "hull" }, PROP);

        destruction.clearBreakoff("HP_1");
        assert.deepEqual(removed, [`${BREAKOFF_ATTACHMENT}HP_1`]);
        assert.equal(destruction.hasBreakoff("HP_1"), false);

        destruction.clearBreakoffs();
        assert.deepEqual(removed, [
            `${BREAKOFF_ATTACHMENT}HP_1`,
            `${BREAKOFF_ATTACHMENT}HP_2`,
        ]);
    });

    it("spins a dying ship away and puts it back where it was on repair", () => {
        const { destruction, hull } = rigWith();
        hull.root.position.set(1, 2, 3);

        destruction.startSpinAway(
            "hull",
            { timeSeconds: 2, chance: 1, explosion: null, maxSpeed: 4.5 },
            () => undefined,
        );
        destruction.advanceSpinAway(1);
        assert.notDeepEqual(hull.root.position.toArray(), [1, 2, 3]);

        destruction.clearSpinAway();
        assert.deepEqual(hull.root.position.toArray(), [1, 2, 3]);
    });

    it("finishes at once for a part that is not there", () => {
        const { destruction } = rigWith();
        let done = false;

        destruction.startSpinAway(
            "missing",
            { timeSeconds: 2, chance: 1, explosion: null, maxSpeed: 4.5 },
            () => {
                done = true;
            },
        );

        assert.equal(done, true);
    });
});
