// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's own types: where a particle system goes, what a model part is loaded with, the stats
// readout, the named camera angles and what a still capture shows. Their own module because the viewport and its layers both read
// them; `viewport.ts` re-exports them.

import type * as THREE from "three";

import { type LevelTagged } from "./levels";
import { type ParticleOrigin } from "./particleScene";

/**
 * Where a system being loaded goes, and what it is.
 *
 * One object rather than six trailing positional parameters. Two of them were already being passed
 * as `undefined, undefined` at half the call sites to reach the one after, which is the point at
 * which a signature stops saying anything - and `origin`, the one field a caller must not get
 * wrong, would have been the eighth.
 */
export interface ParticlePlacement {
    /** Whether the MODEL declares this system, or the Gameplay lens asked for it by name. */
    origin: ParticleOrigin;
    attachToPartId?: string;
    attachBone?: string;
    /** The bone's index, which is what tells two same-named bones apart. */
    attachBoneIndex?: number;
    /**
     * The name of the model's proxy this system is, for the rules that read it - the game's effect
     * name groups. None for an effect the model does not carry.
     */
    proxyName?: string;
    /** The ALT/LOD tags on the proxy. Untagged - every level - when omitted. */
    levels?: LevelTagged;
    /**
     * The owning object's `Scale_Factor`, a uniform render scale on the whole system.
     *
     * Only a `<Particle>` GAME OBJECT can carry one; a model's proxy names the asset directly and
     * gets 1. Six shipped objects declare one - 20.0 on the four hero powerup effects, 2.0 on the
     * two bombing-run explosions - and nothing read it, so all six drew far too small.
     */
    scaleFactor?: number;

    /**
     * Where to put it, in model space, for a system that names no part to hang on.
     *
     * A part-less system otherwise sits at the MODEL ROOT, which is right for a ship's own death
     * blast - it goes off where the ship was - and wrong for a wreck that flew 270 units away first.
     * Attaching it to the wreck instead is not the answer either: the wreck is hidden in the same
     * breath and three prunes a hidden subtree, effects included.
     */
    at?: { x: number; y: number; z: number };
}

/** What the stats readout shows. */
export interface ViewportStats {
    parts: number;
    meshes: number;
    triangles: number;
    bones: number;
    animations: string[];
}

/**
 * A still capture: its size in pixels, the backdrop behind it - null for a transparent one - and what of
 * the room and the reading aids it shows. Each layer stages its own part of it; see `CaptureRig.capture`.
 */
export interface CaptureShot {
    width: number;
    height: number;
    background: string | null;
    grid: boolean;
    floor: boolean;
    particles: boolean;
    /** The axes, the skeleton, the selection and the firing arcs: diagnostics, not part of the subject. */
    annotations: boolean;
}

/** A named camera angle the toolbar can jump to. */
export type PresetView = "front" | "side" | "top" | "threeQuarter";

/** Anything about a loaded model beyond the bone it is attached to. */
export interface AddPartOptions {
    /** Which subject the geometry belongs to. The active one when not given. */
    subjectId?: string;
    /**
     * A world pose to drop the model at, instead of parenting it to a bone.
     *
     * Wreckage stops being attached to the ship the moment it breaks off, which is the whole point
     * of it - so it captures where its hardpoint WAS and is placed there under the model root.
     */
    pose?: { position: THREE.Vector3; quaternion: THREE.Quaternion };
    /**
     * The model's OWN render scale, for a part that is its own object - a death clone.
     *
     * Such a part is placed beside the subject's scaled geometry, not inside it, and carries this
     * instead: it is spawned as a separate object, so the engine draws it at its own
     * `Scale_Factor` and nothing relates that to the ship's.
     */
    scale?: number;
}
