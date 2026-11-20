// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One subject's animation state as the viewport holds it. Its own module because the viewport and the
// row layer both read it.

import type * as THREE from "three";

import { type VisibilityTrack } from "./boneVisibility";
import { type DeathState } from "./deathEnding";

/**
 * One subject's animation, kept entirely apart from every other subject's.
 *
 * Nothing here is scene-wide, and that is the point: a name is unique WITHIN a model - the exporter
 * suffixes every bone with its index - and unique nowhere else. A death clone is the same model as
 * the ship it replaces, so a scene-wide map of any of these is a map in which the hull holds every
 * key and the clone holds none.
 */
export interface SubjectAnimation {
    /**
     * Rooted at the first of this subject's parts to bring clips.
     *
     * A clip binds to nodes BY NAME from its mixer's root, so a mixer rooted at the hull will drive
     * the hull whatever clip is fed to it.
     */
    mixer: THREE.AnimationMixer | null;
    clips: THREE.AnimationClip[];
    /**
     * Which bones each of this subject's clips hides, and on which frames.
     *
     * Filled from the glTF extras as parts arrive. Alamo animations switch bones off per frame and
     * half the shipped corpus does it - mostly to time a particle effect to the motion - so without
     * this every effect on a model fires from frame zero of every clip.
     */
    visibility: Map<string, VisibilityTrack>;
    /**
     * This subject's nodes by their glTF name, which is how a visibility track addresses a bone.
     *
     * First wins, which is safe now that it is scoped: a bone name is unique inside one model
     * because the exporter suffixes it with the bone index. Scene-wide it was not, and the hull
     * won every key.
     */
    nodeByName: Map<string, THREE.Object3D>;
    /**
     * Where every node sat when its part loaded - the pose the model file describes.
     *
     * Captured per part as it arrives, so a hardpoint that loads late is recorded too, and never
     * overwritten: the first sighting of a node is the only one taken while an animation may have
     * moved it since.
     */
    restPose: Map<
        THREE.Object3D,
        {
            position: THREE.Vector3;
            quaternion: THREE.Quaternion;
            scale: THREE.Vector3;
        }
    >;
    /** The clip running on this subject right now. */
    action: THREE.AnimationAction | null;
    /** How far this subject has got towards its end, once it has started dying; null until then. */
    death: DeathState | null;
}
