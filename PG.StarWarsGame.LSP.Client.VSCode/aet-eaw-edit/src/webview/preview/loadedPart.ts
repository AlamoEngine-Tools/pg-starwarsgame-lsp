// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One part of the scene as the viewport holds it: its root, its bones by name and by index, and the
// subject it belongs to. Its own module because the viewport and every layer cut out of it read it.

import type * as THREE from "three";

/** One loaded part, kept so it can be hidden, re-tinted or removed without a reload. */
export interface LoadedPart {
    id: string;
    root: THREE.Object3D;
    /** Bones by their Alamo name, lowercased - node names carry a `#index` suffix to stay unique. */
    bones: Map<string, THREE.Object3D>;
    /** Bones by their model bone index, in index order, for the skeleton view. */
    bonesByIndex: Map<number, THREE.Object3D>;
    /**
     * The subject this part belongs to - {@link ACTIVE_SUBJECT} for the one that opened the scene.
     *
     * A death clone and a piece of wreckage are their own subjects, not further pieces of the one
     * being previewed. Run through the ACTIVE subject's row chain they inherit its answers by
     * NAME - the Calamari Cruiser's wreck carries meshes called `HP_F-L_Blast` and so does its
     * hull, so every blast decal on the wreck lit because the hull's hardpoints were all destroyed.
     * Worse, nothing wrote the meshes the chain had no row for, so the file's raw visibility
     * stood: its `RSkinShadowVolume` hull and its two `alamoHidden` engine plates were drawn as
     * solid geometry.
     */
    subjectId: string;
}

/**
 * The subject that opened the scene: the one the tree, the stats and the row chain describe.
 *
 * Exactly one subject is ACTIVE at a time and it is always this one. Everything else in the scene -
 * a death clone, a piece of wreckage - is a PASSIVE subject: it is drawn by its own file's rules,
 * runs its own clips, and never answers a question about the model being previewed.
 */
export const ACTIVE_SUBJECT = "subject";
