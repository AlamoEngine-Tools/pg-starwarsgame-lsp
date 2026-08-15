// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import { splitGeometryFromBone, stampTreeKeys, treeKeyOf } from './boneNodes';

/** A bone node that also carries geometry, with a child bone hanging off it. */
function nebulonShaped(): { parent: THREE.Object3D; mesh: THREE.Mesh; child: THREE.Object3D } {
    const parent = new THREE.Object3D();

    const mesh = new THREE.Mesh(new THREE.BoxGeometry(), new THREE.MeshBasicMaterial());
    mesh.name = 'Nebulon_parent#5';
    mesh.position.set(3, 4, 5);
    mesh.scale.set(2, 2, 2);

    const child = new THREE.Object3D();
    child.name = 'engines#6';
    child.position.set(1, 0, 0);

    mesh.add(child);
    parent.add(mesh);

    return { parent, mesh, child };
}

describe('splitGeometryFromBone', () => {
    it('leaves a bone node with no geometry of its own', () => {
        const { mesh } = nebulonShaped();

        const bone = splitGeometryFromBone(mesh, 'COLLISION');

        assert.equal(bone instanceof THREE.Mesh, false);
        assert.equal(bone.name, 'Nebulon_parent#5', 'the bone kept its identity');
    });

    it('puts the geometry underneath as a mesh of its own', () => {
        const { mesh } = nebulonShaped();

        const bone = splitGeometryFromBone(mesh, 'COLLISION');

        assert.equal(mesh.parent, bone);
        assert.equal(mesh.name, 'COLLISION', 'the mesh should carry its own name, not the bone`s');
    });

    it('takes the place of the node it replaced', () => {
        const { parent, mesh } = nebulonShaped();

        const bone = splitGeometryFromBone(mesh, 'COLLISION');

        assert.equal(bone.parent, parent);
        assert.equal(parent.children.includes(mesh), false, 'the mesh is still a direct child');
    });

    it('moves the children onto the bone, not the mesh', () => {
        const { mesh, child } = nebulonShaped();

        const bone = splitGeometryFromBone(mesh, 'COLLISION');

        assert.equal(child.parent, bone);
        assert.deepEqual(mesh.children, [], 'the mesh kept children it can now hide by accident');
    });

    /**
     * The point of reusing the mesh object rather than rebuilding it. A SkinnedMesh's bind matrix is
     * computed against its world matrix, so moving it in the graph without preserving that would
     * deform the model - and the deformation would look like a skinning bug, not a graph edit.
     */
    it('leaves every world transform exactly where it was', () => {
        const before = nebulonShaped();
        before.parent.updateMatrixWorld(true);
        const meshBefore = before.mesh.matrixWorld.clone();
        const childBefore = before.child.matrixWorld.clone();

        const after = nebulonShaped();
        const bone = splitGeometryFromBone(after.mesh, 'COLLISION');
        bone.parent!.updateMatrixWorld(true);

        assert.deepEqual(after.mesh.matrixWorld.toArray(), meshBefore.toArray());
        assert.deepEqual(after.child.matrixWorld.toArray(), childBefore.toArray());
    });

    it('carries the node`s visibility over to the bone', () => {
        const { mesh } = nebulonShaped();
        mesh.visible = false;

        const bone = splitGeometryFromBone(mesh, 'COLLISION');

        assert.equal(bone.visible, false, 'a hidden bone node became visible');
        assert.equal(mesh.visible, true, 'the mesh should start drawn; gating decides after');
    });

    /**
     * The extras on a glTF node describe the BONE - `alamoBillboard` is a field of the `0x206` bone
     * chunk. Leaving them on the mesh loses them the moment anything asks a bone about itself: the
     * billboard collector walks bone nodes and found nothing at all on `W_smallbomb`, whose one
     * billboarded bone is exactly this shape.
     */
    it('carries the node`s own data over to the bone', () => {
        const { mesh } = nebulonShaped();
        mesh.userData.alamoBillboard = 'Parallel';

        const bone = splitGeometryFromBone(mesh, 'COLLISION');

        assert.equal(bone.userData.alamoBillboard, 'Parallel');
        assert.deepEqual(mesh.userData, {}, 'the mesh kept data that belongs to its bone');
    });

    it('keeps the original name when there is no mesh name to use', () => {
        const { mesh } = nebulonShaped();

        splitGeometryFromBone(mesh);

        assert.equal(mesh.name, 'Nebulon_parent#5');
    });
});

describe('stampTreeKeys', () => {
    /** Two bones, the second carrying two sub-meshes, which is where a name-based key falls over. */
    function rigged(): { root: THREE.Object3D; bones: Map<number, THREE.Object3D>;
        meshes: THREE.Mesh[]; } {
        const root = new THREE.Object3D();
        const boneA = new THREE.Object3D();
        const boneB = new THREE.Object3D();
        root.add(boneA, boneB);

        const mesh = (name: string): THREE.Mesh =>
            new THREE.Mesh(new THREE.BoxGeometry(), new THREE.MeshBasicMaterial());

        const one = mesh('Crusher');
        const two = mesh('Crusher');
        const three = mesh('hull');
        boneA.add(three);
        boneB.add(one, two);

        return { root, bones: new Map([[3, boneA], [7, boneB]]), meshes: [three, one, two] };
    }

    /**
     * The key has to survive the file being opened again. The three.js uuid it replaces was a
     * different value every load, which is what stopped a hidden mesh being restorable; a NAME
     * cannot do it either, because `Ai_rancor` carries two sub-meshes called `Crusher#0`.
     */
    it('keys a mesh by the bone it hangs off and its place on it', () => {
        const { root, bones, meshes } = rigged();

        stampTreeKeys(root, bones, 'hull');

        assert.equal(treeKeyOf(meshes[0]), 'hull:3:0');
        assert.equal(treeKeyOf(meshes[1]), 'hull:7:0');
        assert.equal(treeKeyOf(meshes[2]), 'hull:7:1');
    });

    it('gives the same key twice for the same graph', () => {
        const first = rigged();
        const second = rigged();

        stampTreeKeys(first.root, first.bones, 'hull');
        stampTreeKeys(second.root, second.bones, 'hull');

        assert.deepEqual(first.meshes.map(treeKeyOf), second.meshes.map(treeKeyOf));
    });

    /** Mounting the same turret eight times must give eight independently switchable meshes. */
    it('keeps two parts apart', () => {
        const a = rigged();
        const b = rigged();

        stampTreeKeys(a.root, a.bones, 'hull');
        stampTreeKeys(b.root, b.bones, 'hp:turret');

        assert.notEqual(treeKeyOf(a.meshes[0]), treeKeyOf(b.meshes[0]));
    });

    /** A mesh under no bone at all still needs a key, or its row cannot be switched. */
    it('falls back to the lowest bone for a mesh that hangs off none', () => {
        const root = new THREE.Object3D();
        const loose = new THREE.Mesh(new THREE.BoxGeometry(), new THREE.MeshBasicMaterial());
        root.add(loose);

        stampTreeKeys(root, new Map([[4, new THREE.Object3D()]]), 'hull');

        assert.equal(treeKeyOf(loose), 'hull:4:0');
    });
});
