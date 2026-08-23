// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import {
    cancelRootCorrection, outermostRoots, splitGeometryFromBone, stampTreeKeys, treeKeyOf,
} from './boneNodes';

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

describe('outermostRoots', () => {
    it('leaves a flat set of parts alone', () => {
        const hull = new THREE.Object3D();
        const loose = new THREE.Object3D();

        assert.deepEqual(outermostRoots([hull, loose]), [hull, loose]);
    });

    it('drops a part that is already inside another one', () => {
        // MEASURED on the real Star Destroyer: every hardpoint part is attached to a BONE of the
        // hull, so it lives inside the hull's own root. Walking each part root in turn therefore
        // visited every mounted mesh twice - once through the hull and once on its own - which gave
        // the model tree two rows under one React key and made the parts list say everything twice.
        const hull = new THREE.Object3D();
        const bone = new THREE.Object3D();
        const turret = new THREE.Object3D();

        hull.add(bone);
        bone.add(turret);

        assert.deepEqual(outermostRoots([hull, turret]), [hull]);
    });

    it('does not care what order the parts arrive in', () => {
        const hull = new THREE.Object3D();
        const turret = new THREE.Object3D();
        hull.add(turret);

        assert.deepEqual(outermostRoots([turret, hull]), [hull]);
    });

    it('keeps a chain of mounts down to one walk', () => {
        // A hardpoint may mount on another hardpoint's model. The outermost root reaches them all.
        const hull = new THREE.Object3D();
        const turret = new THREE.Object3D();
        const barrel = new THREE.Object3D();

        hull.add(turret);
        turret.add(barrel);

        assert.deepEqual(outermostRoots([hull, turret, barrel]), [hull]);
    });

    it('keeps a part whose ancestor is not itself a part', () => {
        // The scene's own model root is not a part, so a part parented to it is still outermost.
        const modelRoot = new THREE.Object3D();
        const hull = new THREE.Object3D();
        modelRoot.add(hull);

        assert.deepEqual(outermostRoots([hull]), [hull]);
    });
});

describe('cancelRootCorrection', () => {
    /** The Z-up-to-Y-up matrix the exporter puts on every model's `AlamoRoot`. */
    const correction = () => {
        const node = new THREE.Object3D();
        node.name = 'AlamoRoot';
        node.matrixAutoUpdate = false;
        node.matrix.set(
            1, 0, 0, 0,
            0, -4.371139e-8, 1, 0,
            0, -1, -4.371139e-8, 0,
            0, 0, 0, 1);
        return node;
    };

    it('neutralises the correction on the node that carries it', () => {
        // MEASURED: every GLB the exporter writes - hull AND hardpoint alike - carries the same
        // -90-degree-about-X AlamoRoot. A mount parented to a hull BONE is already inside the
        // hull's corrected space, so its own copy applies the rotation a SECOND time. That is the
        // 90-degree roll in the green-blue plane, and mirrored hulls make it read anticlockwise to
        // port and clockwise to starboard from one single bug.
        const scene = new THREE.Object3D();
        const alamo = correction();
        scene.add(alamo);

        cancelRootCorrection(scene);

        assert.ok(alamo.matrix.equals(new THREE.Matrix4()));
    });

    it('finds the correction when it IS the root handed in', () => {
        const alamo = correction();

        cancelRootCorrection(alamo);

        assert.ok(alamo.matrix.equals(new THREE.Matrix4()));
    });

    it('leaves a root that carries no correction alone', () => {
        // Never guess at geometry: a model whose root is already identity must come through
        // untouched rather than being rotated the other way to compensate.
        const scene = new THREE.Object3D();
        const plain = new THREE.Object3D();
        plain.name = 'Root#0';
        plain.position.set(1, 2, 3);
        scene.add(plain);

        cancelRootCorrection(scene);

        assert.deepEqual(
            [plain.position.x, plain.position.y, plain.position.z], [1, 2, 3]);
    });

    it('leaves everything below the correction where the file put it', () => {
        // Only the correction is cancelled. A bone's own placement is the model's own word.
        const scene = new THREE.Object3D();
        const alamo = correction();
        const bone = new THREE.Object3D();
        bone.position.set(4, 5, 6);
        alamo.add(bone);
        scene.add(alamo);

        cancelRootCorrection(scene);

        assert.deepEqual([bone.position.x, bone.position.y, bone.position.z], [4, 5, 6]);
    });
});
