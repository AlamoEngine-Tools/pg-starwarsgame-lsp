// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { skinVisibilityBone } from "./skinBone";

/** A bone node as the exporter names it: `<name>#<model bone index>`. */
function bone(name: string, index: number): THREE.Bone {
    const made = new THREE.Bone();
    made.name = `${name}#${index}`;
    return made;
}

/** One sub-mesh of mesh `mesh`, skinned to `bones` in that order, hung at `root`. */
function subMesh(
    root: THREE.Object3D,
    mesh: number,
    sub: number,
    bones: THREE.Bone[],
): THREE.SkinnedMesh {
    const made = new THREE.SkinnedMesh(
        new THREE.BufferGeometry(),
        new THREE.MeshBasicMaterial(),
    );
    made.skeleton = new THREE.Skeleton(bones);
    made.userData.alamo = {
        alamoMesh: `Mesh${mesh}`,
        alamoMeshIndex: mesh,
        alamoSubMeshIndex: sub,
    };
    root.add(made);
    return made;
}

describe("skinVisibilityBone", () => {
    const root = bone("Root", 0);
    const saber = bone("saber", 1);
    const blade = bone("B_saber", 2);
    const hand = bone("Hand", 3);

    it("is the first non-root bone of the mesh's first sub-mesh's skin list", () => {
        const part = new THREE.Group();
        const first = subMesh(part, 4, 0, [root, saber, blade]);

        assert.equal(skinVisibilityBone(first, part, root), "saber#1");
    });

    it("is the same for every sub-mesh of the mesh, whatever its own list says", () => {
        const part = new THREE.Group();
        subMesh(part, 4, 0, [root, blade, saber]);
        const second = subMesh(part, 4, 1, [hand, saber]);

        assert.equal(skinVisibilityBone(second, part, root), "B_saber#2");
    });

    it("keeps to the mesh's own sub-meshes, not another mesh's", () => {
        const part = new THREE.Group();
        subMesh(part, 2, 0, [root, hand]);
        const other = subMesh(part, 4, 0, [root, saber]);

        assert.equal(skinVisibilityBone(other, part, root), "saber#1");
    });

    it("falls back to the root when the first sub-mesh lists no other bone", () => {
        const part = new THREE.Group();
        const only = subMesh(part, 4, 0, [root]);

        assert.equal(skinVisibilityBone(only, part, root), "Root#0");
    });
});
