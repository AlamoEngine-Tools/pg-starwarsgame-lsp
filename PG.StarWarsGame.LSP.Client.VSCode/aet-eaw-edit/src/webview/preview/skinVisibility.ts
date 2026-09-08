// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a clip's visibility tracks say about a SKINNED mesh.
//
// The rest of the chain hides BONES, and hiding a bone prunes its subtree - which is the whole
// mechanism for a mesh that rides one. A skinned mesh rides no bone in that sense: it hangs off the
// root and takes its shape from a weighted set, so every bone track in the file can fire without
// touching it.
//
// Yoda is the case that exposed it. His blade is `sabre_light`, skinned to `{Root, saber, B_saber}`
// and attached to bone 0, and his clips are explicit - `idle_00` and `walk_00` hide both of the
// non-root bones, `attack_00` shows them. None of that reached the mesh, so the blade drew on every
// frame of every clip while the hilt, which does ride a bone, obeyed the tracks and vanished.
//
// Measured over both shipped trees before it was written. 661 skinned meshes, 370 with a non-root
// skin bone; the rule fires on 54 (mesh, clip) pairs, and they are Yoda's blade, Silri's whip in
// twelve of her thirteen clips, the last few frames of six capital-ship deaths, and Jabba during
// his deploy. Two details in it are load-bearing rather than tidy, and each has a test:
//
//   EVERY, not any. 88 further cases have part of a set hidden, and they include the dark trooper's
//   body at all three LODs and its shadow - 11 to 18 bones - on every frame of two clips. "Any"
//   would blank the trooper.
//
//   Root excluded. `sabre_light`'s own set contains bone 0, which nothing ever hides, so counting
//   it would stop the rule ever firing on anything at all.

import { boneIndexOf, type BoneId } from './boneIds';

/**
 * Whether a clip hides a skinned mesh on this frame - or undefined, meaning it has said nothing.
 *
 * Only ever a hide. A clip that shows the bones a mesh is weighted to is not thereby overriding
 * what the FILE says about the mesh: plenty of geometry is marked hidden on purpose, and a clip
 * mentioning a bone the mesh happens to use is not permission to draw it.
 *
 * @param skinBones every bone the mesh is weighted to, as canonical ids.
 * @param hiddenBy what the clip says about one bone: false for hidden, true for shown, undefined
 *     when the clip does not mention it. The same shape the rest of the chain speaks.
 */
export function skinHiddenByClip(
    skinBones: readonly BoneId[],
    hiddenBy: (id: BoneId) => boolean | undefined,
): boolean | undefined {
    const driven = skinBones.filter(id => boneIndexOf(id) !== 0);

    if (driven.length === 0) {
        return undefined;
    }

    return driven.every(id => hiddenBy(id) === false) ? true : undefined;
}
