// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The bone a skinned mesh's visibility follows.
//
// Measured in the game: a skinned mesh is shown or hidden by ONE bone's track - the first bone of its
// FIRST sub-mesh's skin list that is not bone 0, the root. Every sub-mesh of that mesh follows the same
// bone, whatever its own list says, and no other bone of the skin counts. Yoda's `sabre_light` is
// skinned to the root first, so it follows the next bone of that list, which is the one his clips key.
//
// The order is the file's: the exporter writes each sub-mesh's joints in its ALO skin list order, the
// root where the list has it.

import * as THREE from "three";

import { type MaterialExtras } from "./materials";
import { alamoBoneName, alamoNodeName } from "./skeleton";

/** What a skinned mesh follows, worked out once: its skeleton is fixed when the model loads. */
const answered = new WeakMap<THREE.Object3D, THREE.Object3D | undefined>();

/**
 * The bone a skinned mesh's visibility follows, or undefined for a rigid mesh.
 *
 * @param partRoot The root of the part the mesh belongs to, where its sibling sub-meshes are.
 * @param rootBone The part's bone 0, for the fallback.
 */
export function skinVisibilityBone(
    mesh: THREE.Mesh,
    partRoot: THREE.Object3D,
    rootBone: THREE.Object3D | undefined,
): THREE.Object3D | undefined {
    if (!(mesh as THREE.SkinnedMesh).isSkinnedMesh) {
        return undefined;
    }

    if (answered.has(mesh)) {
        return answered.get(mesh);
    }

    const bones = firstSubMeshOf(mesh as THREE.SkinnedMesh, partRoot).skeleton
        ?.bones;

    const named = (bones ?? []).find(
        (bone) => alamoBoneName(alamoNodeName(bone))?.index !== 0,
    );

    // A list with no bone but the root is a case the engine reading does not record: unmeasured, so
    // it falls back to the root itself.
    const bone = named ?? rootBone ?? bones?.[0];

    answered.set(mesh, bone);

    return bone;
}

/**
 * The first sub-mesh of the mesh `mesh` is a sub-mesh of: the skinned mesh under `partRoot` with the
 * same mesh index and the lowest sub-mesh index. The mesh itself when it carries no indices.
 */
function firstSubMeshOf(
    mesh: THREE.SkinnedMesh,
    partRoot: THREE.Object3D,
): THREE.SkinnedMesh {
    const own = (mesh.userData.alamo ?? {}) as MaterialExtras;

    if (typeof own.alamoMeshIndex !== "number") {
        return mesh;
    }

    let first = mesh;
    let lowest =
        typeof own.alamoSubMeshIndex === "number"
            ? own.alamoSubMeshIndex
            : Number.POSITIVE_INFINITY;

    partRoot.traverse((node) => {
        if (!(node as THREE.SkinnedMesh).isSkinnedMesh) {
            return;
        }

        const extras = (node.userData.alamo ?? {}) as MaterialExtras;

        if (
            extras.alamoMeshIndex === own.alamoMeshIndex &&
            typeof extras.alamoSubMeshIndex === "number" &&
            extras.alamoSubMeshIndex < lowest
        ) {
            first = node as THREE.SkinnedMesh;
            lowest = extras.alamoSubMeshIndex;
        }
    });

    return first;
}
