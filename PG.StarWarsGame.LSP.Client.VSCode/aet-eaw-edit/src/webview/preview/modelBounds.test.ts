// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import { drawnBounds } from './modelBounds';

/** A one-unit cube at the origin, scaled to the span asked for. */
function cube(span: number): THREE.Mesh {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(span, span, span));
    mesh.updateMatrixWorld(true);

    return mesh;
}

describe('drawnBounds', () => {
    it('measures the meshes that are drawn', () => {
        const root = new THREE.Object3D();
        root.add(cube(10));

        const box = drawnBounds(root, () => true);

        assert.equal(box.isEmpty(), false);
        assert.equal(box.max.x - box.min.x, 10);
    });

    /**
     * The defect this exists for.
     *
     * A weapon's firing arc is a `LineSegments` cone parented under the model, and it is sized by a
     * gameplay stat rather than by the hull - on the Star Destroyer it spans 2000 x 6928 x 2801
     * against a hull of a few hundred units. `Box3.setFromObject` swallowed it whole, so pressing a
     * camera preset framed the weapon range and put the ship 27 times further away than the view it
     * replaced.
     */
    it('ignores an overlay that is not a mesh', () => {
        const root = new THREE.Object3D();
        root.add(cube(10));

        const arc = new THREE.LineSegments(new THREE.BoxGeometry(7000, 7000, 7000));
        arc.updateMatrixWorld(true);
        root.add(arc);

        assert.equal(drawnBounds(root, () => true).max.x - drawnBounds(root, () => true).min.x, 10);
    });

    /**
     * Framing fits what you can SEE. Hidden detail levels, the damage variants that are not
     * selected, collision hulls and shadow volumes are all in the graph and all larger than nothing.
     */
    it('ignores a mesh the caller says is not drawn', () => {
        const root = new THREE.Object3D();
        const small = cube(10);
        const huge = cube(900);
        root.add(small, huge);

        const box = drawnBounds(root, mesh => mesh === small);

        assert.equal(box.max.x - box.min.x, 10);
    });

    it('is empty when nothing is drawn', () => {
        const root = new THREE.Object3D();
        root.add(cube(10));

        assert.equal(drawnBounds(root, () => false).isEmpty(), true);
    });

    it('takes world transforms into account', () => {
        const root = new THREE.Object3D();
        const moved = cube(2);
        moved.position.set(100, 0, 0);
        root.add(moved);
        root.updateMatrixWorld(true);

        const box = drawnBounds(root, () => true);

        assert.equal(box.min.x, 99);
        assert.equal(box.max.x, 101);
    });

    /**
     * A geometry with a NaN vertex would otherwise poison the whole box, and the sphere taken from
     * it comes out NaN - which the caller reads as "no size at all" and frames a one-unit ball.
     */
    it('skips a geometry whose bounds are not finite', () => {
        const root = new THREE.Object3D();
        root.add(cube(10));

        const broken = new THREE.Mesh(new THREE.BufferGeometry());
        broken.geometry.setAttribute('position',
            new THREE.BufferAttribute(new Float32Array([NaN, NaN, NaN]), 3));
        broken.updateMatrixWorld(true);
        root.add(broken);

        const box = drawnBounds(root, () => true);

        assert.equal(box.max.x - box.min.x, 10);
    });
});
