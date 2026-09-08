// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How big the model on screen actually is.
//
// `Box3.setFromObject` is the obvious answer and the wrong one: it measures EVERYTHING under the
// node, drawn or not, mesh or not. Three separate things live under the model root that are not the
// model -
//
//   - firing-arc cones, sized by a weapon's range rather than by the hull. The Star Destroyer's span
//     2000 x 6928 x 2801 against a hull of a few hundred units, and they are built whether or not
//     they are shown. Pressing a camera preset framed the weapon range and put the ship 27 times
//     further away than the view it replaced, which is the defect this file exists for.
//   - the detail and damage levels that are not selected, which are in the graph and hidden.
//   - collision hulls and shadow volumes, gated off by their shader archetype.
//
// So: only meshes, and only the ones the caller says are on screen.

import * as THREE from 'three';

/**
 * A box around the drawn meshes under a root, in world space.
 *
 * Empty when nothing qualifies - the caller decides what to do with that, because "no geometry yet"
 * and "everything is hidden" want different answers and neither is this function's business.
 */
export function drawnBounds(
    root: THREE.Object3D, isDrawn: (mesh: THREE.Mesh) => boolean,
): THREE.Box3 {
    const box = new THREE.Box3();
    const corner = new THREE.Vector3();

    root.updateWorldMatrix(false, true);

    root.traverse(node => {
        // `instanceof Mesh` is doing real work here: LineSegments and Points are Object3Ds with
        // geometry, so anything that merely checks for a `geometry` property lets the overlays in.
        if (!(node instanceof THREE.Mesh) || !isDrawn(node)) {
            return;
        }

        const geometry = node.geometry;
        if (geometry.boundingBox === null) {
            geometry.computeBoundingBox();
        }

        const local = geometry.boundingBox;
        // A NaN vertex would spread through the box and out into the sphere, where the caller reads
        // the NaN radius as no size at all and frames a one-unit ball around the origin.
        if (local === null || !isFinite(local.min.x + local.min.y + local.min.z
            + local.max.x + local.max.y + local.max.z)) {
            return;
        }

        for (let i = 0; i < 8; i++) {
            corner.set(
                (i & 1) === 0 ? local.min.x : local.max.x,
                (i & 2) === 0 ? local.min.y : local.max.y,
                (i & 4) === 0 ? local.min.z : local.max.z,
            ).applyMatrix4(node.matrixWorld);

            box.expandByPoint(corner);
        }
    });

    return box;
}
