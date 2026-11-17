// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { ModelQueries } from "./modelQueries";
import { type ViewportContext } from "./viewportContext";

/**
 * Queries over a stand-in viewport whose hull has a root bone and a turret bone under it, 5 units up,
 * carrying one triangle tagged for ALT 1. Every mesh counts as drawn; the subject has one clip.
 */
function queriesWith(): { queries: ModelQueries; noted: string[] } {
    const modelRoot = new THREE.Group();
    const root = new THREE.Bone();
    root.name = "Root#0";
    const turret = new THREE.Bone();
    turret.name = "Turret#1";
    turret.position.set(0, 5, 0);
    root.add(turret);
    modelRoot.add(root);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute(
        "position",
        new THREE.Float32BufferAttribute([0, 0, 0, 1, 0, 0, 0, 1, 0], 3),
    );
    const barrel = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial());
    barrel.name = "Barrel";
    barrel.userData.alamo = { alamoMesh: "Barrel", alamoAlt: 1 };
    turret.add(barrel);
    modelRoot.updateMatrixWorld(true);
    const hull: LoadedPart = {
        id: "hull",
        root: modelRoot,
        bones: new Map([
            ["root", root],
            ["turret", turret],
        ]),
        bonesByIndex: new Map([
            [0, root],
            [1, turret],
        ]),
        subjectId: ACTIVE_SUBJECT,
    };
    const noted: string[] = [];
    const context = {
        modelRoot,
        parts: new Map([["hull", hull]]),
        particles: { particleSystems: new Map() },
        levels: { alt: 0, drawn: () => true },
        animation: { animationNames: ["Move"] },
        assembly: {
            hull: () => hull,
            activeSubjectRoots: () => [modelRoot],
            noteAttachment: (partId: string, bone?: string) =>
                noted.push(`${partId}/${bone}`),
        },
    } as unknown as ViewportContext;
    return { queries: new ModelQueries(context), noted };
}

describe("ModelQueries", () => {
    it("lists the hull's bones in index order with their parents", () => {
        const { queries } = queriesWith();

        assert.deepEqual(queries.skeleton(), [
            { index: 0, name: "Root", parent: -1, visible: true },
            { index: 1, name: "Turret", parent: 0, visible: true },
        ]);
    });

    it("lists what hangs on each bone", () => {
        const { queries } = queriesWith();

        const byBone = queries.attachmentsByBone();

        assert.deepEqual(
            (byBone.get(1) ?? []).map((attachment) => attachment.kind),
            ["mesh"],
        );
        assert.ok((byBone.get(1) ?? [])[0].label.includes("Barrel"));
    });

    it("finds a bone in the world and remembers that it was asked for", () => {
        const { queries, noted } = queriesWith();

        assert.deepEqual(queries.bonePosition("hull", "Turret"), {
            x: 0,
            y: 5,
            z: 0,
        });
        assert.equal(queries.bonePosition("hull", "Missing"), null);
        assert.equal(queries.bonePosition("missing"), null);
        assert.deepEqual(noted, [
            "hull/Turret",
            "hull/Missing",
            "missing/undefined",
        ]);
    });

    it("counts the drawn meshes, triangles, bones and clips", () => {
        const { queries } = queriesWith();

        assert.deepEqual(queries.stats(), {
            parts: 1,
            meshes: 1,
            triangles: 1,
            bones: 2,
            animations: ["Move"],
        });
    });

    it("reports the damage levels the model defines", () => {
        const { queries } = queriesWith();

        assert.ok(queries.definedLevels().alt.includes(1));
    });
});
