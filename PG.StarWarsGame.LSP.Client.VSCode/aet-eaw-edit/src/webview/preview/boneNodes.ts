// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Making "is this a bone or a mesh?" a question with one answer.
//
// The exporter writes one glTF node per bone and hangs the FIRST mesh attached to a bone on the
// node itself, so a single object routinely means two things at once. Three separate defects came
// out of that in as many days:
//
//   - `W_fuel_cell_1.alo` blanked entirely. Its three meshes all ride bone 0, so the first of them
//     - the shadow volume, gated off - became Root, and switching it off took the whole model.
//   - `Rv_nebulonb.alo` reported its engines and every hardpoint as children of `COLLISION`,
//     because that hull happened to be the first mesh on `Nebulon_parent`.
//   - `Alttest.alo` drew fourteen rows for seven meshes, half of them inert.
//
// Each was patched where it surfaced. This removes the shape instead: after `splitGeometryFromBone`
// a bone node NEVER carries geometry, so a mesh has no children to lose and no identity to borrow,
// and every rule downstream gets to be about one thing.

import * as THREE from 'three';

/**
 * Splits a node that is both a bone and a mesh into a bone node with a mesh child.
 *
 * The mesh object itself is reused rather than rebuilt: it keeps its geometry, material, uuid and -
 * for a skinned mesh - its skeleton binding, which is computed against a world matrix this must not
 * disturb. The new bone node takes the original's transform and the mesh sits at identity beneath
 * it, so every world transform in the subtree comes out exactly where it was.
 *
 * Returns the bone node that now stands in for the original.
 */
export function splitGeometryFromBone(mesh: THREE.Mesh, meshName?: string): THREE.Object3D {
    const parent = mesh.parent;

    const bone = new THREE.Object3D();
    bone.name = mesh.name;
    bone.position.copy(mesh.position);
    bone.quaternion.copy(mesh.quaternion);
    bone.scale.copy(mesh.scale);
    bone.visible = mesh.visible;

    // The glTF node's extras describe the BONE, not its geometry - `alamoBillboard` is a field of
    // the `0x206` bone chunk. They go with the identity they belong to; the mesh starts clean and
    // takes its own material extras later.
    bone.userData = mesh.userData;
    mesh.userData = {};

    // Children first, while the mesh still holds them and before its transform is reset. They keep
    // their own local transforms, and the bone node carries the transform they were relative to.
    for (const child of [...mesh.children]) {
        bone.add(child);
    }

    // Into the tree where the mesh was, then take the mesh down under it. `add` detaches from the
    // previous parent, so the mesh is never in two places.
    parent?.add(bone);

    mesh.position.set(0, 0, 0);
    mesh.quaternion.identity();
    mesh.scale.set(1, 1, 1);
    mesh.visible = true;

    // The mesh's OWN name, not the bone's. Leaving it as `<bone>#<index>` would leave a second node
    // answering to a bone name, which is the confusion being removed.
    if (meshName !== undefined && meshName !== '') {
        mesh.name = meshName;
    }

    bone.add(mesh);

    return bone;
}

/**
 * A key that names a mesh the same way every time the file is opened.
 *
 * The three.js uuid this replaces was a different value on every load, which is why a hidden mesh
 * could not be restored when a model was reopened and why a tree id told nobody anything. A NAME
 * cannot do the job either: `Ai_rancor` carries two sub-meshes called `Crusher#0`, and one row
 * would drive both.
 *
 * So: the part, the bone the mesh hangs off, and its position among that bone's meshes. Traversal
 * order is fixed for a given file, which is what makes the last part stable rather than arbitrary.
 */
export function stampTreeKeys(
    root: THREE.Object3D, bonesByIndex: ReadonlyMap<number, THREE.Object3D>, partId: string,
): void {
    const boneOf = new Map<THREE.Object3D, number>();
    for (const [index, node] of bonesByIndex) {
        boneOf.set(node, index);
    }

    // Where a mesh under no bone at all lands. Every mesh needs a key or its row cannot be
    // switched, and the lowest bone index is the model's own root.
    const fallback = [...bonesByIndex.keys()].sort((a, b) => a - b)[0] ?? 0;

    const ownerOf = (mesh: THREE.Object3D): number => {
        for (let at = mesh.parent; at !== null; at = at.parent) {
            const index = boneOf.get(at);
            if (index !== undefined) {
                return index;
            }
        }

        if (mesh instanceof THREE.SkinnedMesh) {
            const first = mesh.skeleton.bones[0];
            const index = first === undefined ? undefined : boneOf.get(first);

            if (index !== undefined) {
                return index;
            }
        }

        return fallback;
    };

    const seen = new Map<number, number>();

    root.traverse(node => {
        if (!(node instanceof THREE.Mesh)) {
            return;
        }

        const owner = ownerOf(node);
        const ordinal = seen.get(owner) ?? 0;
        seen.set(owner, ordinal + 1);
        node.userData.aetTreeKey = `${partId}:${owner}:${ordinal}`;
    });
}

/** The key `stampTreeKeys` left, or the empty string for a mesh that never got one. */
export function treeKeyOf(mesh: THREE.Object3D): string {
    const key = mesh.userData.aetTreeKey;

    return typeof key === 'string' ? key : '';
}

/**
 * The part roots that are not already inside another part root.
 *
 * A hardpoint's model is attached to a BONE of the hull, which puts its whole subtree inside the
 * hull's own root. So walking every part root in turn reaches each mounted mesh twice - once on the
 * way down through the hull and once again from the part itself. On the Star Destroyer that gave the
 * model tree two rows sharing one id for every mesh of every mount, and did the material and
 * wireframe work on that geometry twice over.
 *
 * Starting only from the outermost roots visits everything exactly once, and keeps working when a
 * mount is itself mounted on another mount.
 */
export function outermostRoots(
    roots: readonly THREE.Object3D[],
): THREE.Object3D[] {
    const all = new Set(roots);

    return roots.filter(root => {
        for (let at = root.parent; at !== null; at = at.parent) {
            if (all.has(at)) {
                return false;
            }
        }

        return true;
    });
}

/**
 * The exporter's Z-up-to-Y-up correction, to within float noise.
 *
 * Alamo is Z-up and glTF is Y-up, so `ModelGlbExporter` puts a -90-degree rotation about X on every
 * model's `AlamoRoot`. Recognised by its SHAPE rather than by the node's name: a name test would
 * quietly stop working the day the exporter renames it, and the whole point is to leave a root that
 * does not carry the correction completely alone.
 */
function isRootCorrection(node: THREE.Object3D): boolean {
    const m = node.matrix.elements;
    const near = (a: number, b: number) => Math.abs(a - b) < 1e-5;

    return near(m[0], 1) && near(m[1], 0) && near(m[2], 0)
        && near(m[4], 0) && near(m[5], 0) && near(Math.abs(m[6]), 1)
        && near(m[8], 0) && near(Math.abs(m[9]), 1) && near(m[10], 0)
        && near(m[12], 0) && near(m[13], 0) && near(m[14], 0);
}

/**
 * Cancels a part's own Z-up-to-Y-up correction, for a part being attached INSIDE another one.
 *
 * MEASURED on the shipped Star Destroyer: the hull and every hardpoint model carry the same
 * `AlamoRoot` correction. A mount is parented to a BONE of the hull, which already sits below the
 * hull's correction - so the mount's own copy applies the rotation a second time, and the model
 * lands rolled 90 degrees in the green-blue plane. Mirrored hulls then make one bug read
 * anticlockwise to port and clockwise to starboard, which is exactly how it was reported.
 *
 * Only the correction node is touched. Everything below it keeps the placement the file gave it -
 * that is the model author's own word and none of our business.
 */
export function cancelRootCorrection(root: THREE.Object3D): void {
    const carrier = isRootCorrection(root)
        ? root
        : root.children.find(isRootCorrection);

    if (carrier === undefined) {
        return;
    }

    carrier.matrix.identity();
    carrier.matrix.decompose(carrier.position, carrier.quaternion, carrier.scale);
    carrier.updateMatrixWorld(true);
}
