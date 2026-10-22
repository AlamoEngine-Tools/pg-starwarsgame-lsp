// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import type { ModelDetail } from "../../protocol/modelPreview";
import { buildTree } from "./previewTree";
import { treeItemsFromDetail } from "./modelTreeItems";

const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

function bone(index: number, name: string, parentIndex: number) {
    return {
        index,
        name,
        parentIndex,
        visible: true,
        billboard: "Disable",
        relativeTransform: IDENTITY,
        absoluteTransform: IDENTITY,
    };
}

function mesh(index: number, name: string, boneIndex: number) {
    return {
        index,
        name,
        boneIndex,
        visible: true,
        collidable: false,
        alt: null,
        lod: null,
        boundsMin: [0, 0, 0],
        boundsMax: [1, 1, 1],
        subMeshCount: 1,
        vertexCount: 3,
        triangleCount: 1,
    };
}

function proxy(name: string, boneIndex: number) {
    return {
        name,
        boneIndex,
        visible: true,
        altDecreaseStayHidden: false,
        alt: null,
        lod: null,
    };
}

// A hull on the root, a turret bone carrying a mesh of its own name, a second mesh on the turret,
// and an engine proxy bone named for the effect it carries, beside one on a bone of another name.
const DETAIL: ModelDetail = {
    model: "ev_test.alo",
    bones: [
        bone(0, "Root", -1),
        bone(1, "Turret", 0),
        bone(2, "P_Engine", 0),
        bone(3, "Engine_Block", 0),
    ],
    meshes: [
        mesh(0, "Hull", 0),
        mesh(1, "Turret", 1),
        mesh(2, "Turret_Barrel", 1),
    ],
    proxies: [proxy("P_Engine", 2), proxy("P_Smoke", 3)],
    lightCount: 0,
};

const shape = (nodes: ReturnType<typeof buildTree>): string[] =>
    nodes.flatMap((n) => [
        `${"  ".repeat(n.depth)}${n.kind}:${n.name}`,
        ...shape(n.children),
    ]);

describe("treeItemsFromDetail", () => {
    it("nests a model's bones, meshes and effects as the model preview's tree does", () => {
        // A bone whose mesh or effect shares its name is one row of that kind; the root never
        // merges, so the hull hangs off it. Each level lists bones, then meshes, then effects.
        assert.deepEqual(shape(buildTree(treeItemsFromDetail(DETAIL))), [
            "bone:Root",
            "  bone:Engine_Block",
            "    particle:P_Smoke",
            "  mesh:Turret",
            "    mesh:Turret_Barrel",
            "  mesh:Hull",
            "  particle:P_Engine",
        ]);
    });

    it("keys a bone's row by its index, as the preview's rows are", () => {
        const ids = treeItemsFromDetail(DETAIL).map((i) => i.id);
        assert.ok(ids.includes("bone:1"));
        assert.equal(new Set(ids).size, ids.length);
    });
});
