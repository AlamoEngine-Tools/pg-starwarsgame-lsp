// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Finding the geometry a particle system is meant to be born from.
//
// `meshEmission.ts` knows how to pick a point on a mesh; this knows WHICH mesh. The two are apart
// because the answer is a question about the scene graph - a particle proxy is an empty bone, and
// the geometry it belongs to is the bone above it - while the sampling is arithmetic that has to be
// testable without one.
//
// The Nebulon-B is the case that drove this: `pe_nebulonengines` is `EveryVertex` at 2 a second,
// hung off an empty proxy bone whose parent carries the engine block. Read literally it is two
// puffs a second at a point; read as the engine means it, it is the whole engine wash.

import * as THREE from 'three';

import { resolveMaterial, type MaterialExtras } from './materials';
import type { EmissionMesh } from './meshEmission';

/** How far up the graph a proxy bone may look for the geometry it belongs to. */
const MAX_CLIMB = 8;

/**
 * The geometry a system attached to `attachment` should emit from, in that node's own space.
 *
 * Climbs to the nearest ancestor carrying geometry, because a particle proxy is usually an empty
 * bone: the model author hangs the system where the effect should sit and leaves it parented to the
 * thing it comes off. The climb is bounded so a proxy on a genuinely empty limb ends up with
 * nothing rather than reaching all the way to the hull and scattering over the whole ship.
 *
 * Positions and normals are brought into `attachment`'s space, since that is the space the
 * simulation runs in - a source read one bone up would otherwise be born wherever that bone sits.
 *
 * A skinned mesh is read in its BIND pose: its vertices are posed in the shader, and there is no
 * posed copy on the CPU to read. Alamo skins infantry and little else, and a birth point on the
 * bind pose of a running trooper is off by centimetres.
 */
export function emissionMeshFor(
    attachment: THREE.Object3D, stopAt: THREE.Object3D | null = null,
): EmissionMesh | null {
    const toLocal = new THREE.Matrix4();
    attachment.updateWorldMatrix(true, false);
    toLocal.copy(attachment.matrixWorld).invert();

    let node: THREE.Object3D | null = attachment;

    for (let climbed = 0; node !== null && climbed <= MAX_CLIMB; climbed++) {
        const subMeshes = meshesOn(node).map(mesh => subMeshOf(mesh, toLocal));

        if (subMeshes.length > 0) {
            return { subMeshes };
        }

        if (node === stopAt) {
            return null;
        }

        node = node.parent;
    }

    return null;
}

/**
 * The drawable geometry directly on a node.
 *
 * Direct children only - a bone's own geometry, never a child bone's - because after
 * `splitGeometryFromBone` that is exactly where a bone's meshes sit, and descending would pull in
 * the whole limb below.
 *
 * Collision hulls, shadow volumes and the other non-visual archetypes are skipped: they are not
 * surfaces anything is emitted from, and on a warship the collision hull is the crude box around
 * the entire model, so emitting off it would spread an engine wash over the whole ship.
 */
function meshesOn(node: THREE.Object3D): THREE.Mesh[] {
    return node.children.filter((child): child is THREE.Mesh =>
        child instanceof THREE.Mesh
        && !resolveMaterial((child.userData.alamo ?? {}) as MaterialExtras).hidden);
}

function subMeshOf(
    mesh: THREE.Mesh, toLocal: THREE.Matrix4,
): EmissionMesh['subMeshes'][number] {
    mesh.updateWorldMatrix(true, false);

    const transform = new THREE.Matrix4().multiplyMatrices(toLocal, mesh.matrixWorld);
    const rotation = new THREE.Matrix3().getNormalMatrix(transform);

    const position = mesh.geometry.getAttribute('position');
    const normal = mesh.geometry.getAttribute('normal');

    const positions = new Float32Array(position === undefined ? 0 : position.count * 3);
    const normals = new Float32Array(positions.length);
    const scratch = new THREE.Vector3();

    for (let i = 0; i < positions.length / 3; i++) {
        scratch.fromBufferAttribute(position, i).applyMatrix4(transform).toArray(positions, i * 3);

        if (normal !== undefined) {
            scratch.fromBufferAttribute(normal, i)
                .applyMatrix3(rotation).normalize().toArray(normals, i * 3);
        }
    }

    return { positions, normals, indices: indicesOf(mesh.geometry, positions.length / 3) };
}

/**
 * The mesh's faces, filled in when it has none of its own.
 *
 * A non-indexed geometry still has triangles - every three vertices in order - and surface
 * emission needs them. Leaving the list empty would silently demote `RandomMesh` to sampling bare
 * vertices, which clumps a smooth spread onto the corners.
 */
function indicesOf(geometry: THREE.BufferGeometry, vertices: number): Uint32Array {
    const index = geometry.getIndex();

    if (index !== null) {
        return Uint32Array.from(index.array);
    }

    const implied = new Uint32Array(vertices - (vertices % 3));
    for (let i = 0; i < implied.length; i++) {
        implied[i] = i;
    }

    return implied;
}
