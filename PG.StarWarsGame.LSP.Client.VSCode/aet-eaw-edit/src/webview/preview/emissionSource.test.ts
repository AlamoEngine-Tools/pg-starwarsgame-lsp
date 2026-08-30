// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import { emissionMeshFor } from './emissionSource';

/** A triangle, so there is a face to sample as well as vertices to walk. */
function triangle(name: string, shader = 'alDefault.fx'): THREE.Mesh {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(
        [0, 0, 0, 1, 0, 0, 0, 1, 0], 3));
    geometry.setAttribute('normal', new THREE.Float32BufferAttribute(
        [0, 0, 1, 0, 0, 1, 0, 0, 1], 3));
    geometry.setIndex([0, 1, 2]);

    const mesh = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial());
    mesh.name = name;
    mesh.userData.alamo = { alamoShader: shader, alamoMesh: name };

    return mesh;
}

/**
 * The Nebulon-B's shape: the emitter is attached to a bone of its own, and the geometry it is meant to
 * come off lives one bone further up.
 */
function nebulonShaped(): { engines: THREE.Object3D; proxy: THREE.Object3D; mesh: THREE.Mesh } {
    const root = new THREE.Object3D();

    const engines = new THREE.Object3D();
    engines.name = 'engines';
    engines.position.set(0, 0, 100);

    const mesh = triangle('engines');

    const proxy = new THREE.Object3D();
    proxy.name = 'pe_nebulonengines';
    proxy.position.set(0, 0, 10);

    engines.add(mesh);
    engines.add(proxy);
    root.add(engines);
    root.updateMatrixWorld(true);

    return { engines, proxy, mesh };
}

describe('emissionMeshFor', () => {
    it('takes the geometry attached to the bone itself', () => {
        const { engines } = nebulonShaped();

        const source = emissionMeshFor(engines);

        assert.equal(source?.subMeshes.length, 1);
        assert.deepEqual([...source!.subMeshes[0].positions], [0, 0, 0, 1, 0, 0, 0, 1, 0]);
    });

    /**
     * `pe_nebulonengines` is an empty proxy bone. Stopping there would emit a single puff at a
     * point, which is exactly the bug: the wash belongs to the engine block one bone up.
     */
    it('walks up to the nearest bone that actually carries geometry', () => {
        const { proxy } = nebulonShaped();

        const source = emissionMeshFor(proxy);

        assert.equal(source?.subMeshes.length, 1, 'the empty proxy bone ended the search');
    });

    /**
     * Particles are simulated in the space of the node they are attached to, so a source read from a bone
     * further up has to be brought down into it or the whole cloud is born displaced.
     */
    it('expresses the geometry in the attachment`s own space', () => {
        const { proxy } = nebulonShaped();

        const source = emissionMeshFor(proxy);

        // The proxy sits 10 ahead of the engine bone the geometry belongs to.
        assert.deepEqual([...source!.subMeshes[0].positions].slice(0, 3), [0, 0, -10]);
    });

    it('leaves normals as directions, unmoved by the translation', () => {
        const { proxy } = nebulonShaped();

        const source = emissionMeshFor(proxy);

        assert.deepEqual([...source!.subMeshes[0].normals].slice(0, 3), [0, 0, 1]);
    });

    /**
     * A collision hull is not a surface anything is emitted from, and on a warship it is the crude
     * box that encloses the whole thing - emitting off it would scatter the engine wash over the
     * hull.
     */
    it('ignores collision and shadow hulls', () => {
        const bone = new THREE.Object3D();
        bone.add(triangle('COLLISION', 'MeshCollision.fx'));
        bone.add(triangle('shadow', 'RSkinShadowVolume.fx'));
        bone.updateMatrixWorld(true);

        assert.equal(emissionMeshFor(bone), null);
    });

    it('gathers every sub-mesh on the bone, keeping them apart', () => {
        const bone = new THREE.Object3D();
        bone.add(triangle('engine_left'));
        bone.add(triangle('engine_right'));
        bone.updateMatrixWorld(true);

        assert.equal(emissionMeshFor(bone)?.subMeshes.length, 2);
    });

    it('gives nothing back when no bone in the chain carries geometry', () => {
        const bone = new THREE.Object3D();
        const child = new THREE.Object3D();
        bone.add(child);
        bone.updateMatrixWorld(true);

        assert.equal(emissionMeshFor(child), null);
    });

    /**
     * A non-indexed mesh still has faces - every three vertices in order - and surface emission
     * needs them, so they are filled in rather than left for `RandomMesh` to fall back off.
     */
    it('fills in the implied faces of a non-indexed mesh', () => {
        const mesh = triangle('engines');
        mesh.geometry.setIndex(null);

        const bone = new THREE.Object3D();
        bone.add(mesh);
        bone.updateMatrixWorld(true);

        assert.deepEqual([...emissionMeshFor(bone)!.subMeshes[0].indices], [0, 1, 2]);
    });

    /** Search has to end somewhere, or a proxy on an empty bone reads the whole model's geometry. */
    it('stops at the highest node it is given', () => {
        const { proxy } = nebulonShaped();

        assert.equal(emissionMeshFor(proxy, proxy), null, 'the walk climbed past its boundary');
    });
});
