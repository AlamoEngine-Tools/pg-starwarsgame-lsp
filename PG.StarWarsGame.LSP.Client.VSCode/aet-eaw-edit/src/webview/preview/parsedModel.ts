// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A model as it was read from its bytes, and the copy of it a part is built from (#442).
//
// The bytes are read ONCE. What is kept is the node tree with its vertex data, its clips and its
// visibility tracks; the bytes are let go. Every part - the hull, each hardpoint's model, a wreck -
// is a copy of the tree that draws from the SAME vertex data, so a unit with twenty turrets of one
// model holds that model once, and a refresh builds its parts again without reading anything.
// Before, each part was read from the bytes on its own and the bytes were kept beside the result:
// two copies of a 149 MB model for one part, and one more for every further part of it.
//
// The model kept is never put in a scene and never changed. A part takes its copy apart - a bone
// loses its geometry to a node of its own, every mesh gets another material - and the next copy
// has to start from the file's own shape.

import * as THREE from "three";
import { GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { clone } from "three/examples/jsm/utils/SkeletonUtils.js";

import { type VisibilityTrack, visibilityTracks } from "./boneVisibility";
import { bufferOf } from "./payloadBytes";

/** A model as it was read from its bytes. Shared by every part built from it: read, never written. */
export interface ParsedModel {
    /** The node tree as the file gives it. */
    readonly scene: THREE.Object3D;
    readonly clips: readonly THREE.AnimationClip[];
    /** Each clip's visibility tracks, by clip name. */
    readonly visibility: ReadonlyMap<string, VisibilityTrack>;
}

/** What the glTF loader hands back, as far as a model is read off it. */
export interface LoadedGltf {
    scene: THREE.Object3D;
    animations: THREE.AnimationClip[];
    parser: { json: unknown };
}

const loader = new GLTFLoader();

/** Reads a model from its GLB bytes. */
export async function readModel(bytes: Uint8Array): Promise<ParsedModel> {
    return parsedModel(await loader.parseAsync(bufferOf(bytes), ""));
}

/**
 * The model in what the loader read. The loader's own result is not kept: its parser holds the
 * whole binary chunk a second time.
 */
export function parsedModel(gltf: LoadedGltf): ParsedModel {
    // A clip finds its nodes by name from the root it plays on, and the loader names a track by
    // the node's uuid where the node has no name. A copy has uuids of its own, so such a node takes
    // its uuid for a name, which the copy keeps.
    const driven = new Set(
        gltf.animations.flatMap((clip) =>
            clip.tracks.map(
                (track) =>
                    THREE.PropertyBinding.parseTrackName(track.name).nodeName,
            ),
        ),
    );
    gltf.scene.traverse((node) => {
        if (node.name === "" && driven.has(node.uuid)) {
            node.name = node.uuid;
        }
    });

    return {
        scene: gltf.scene,
        clips: gltf.animations,
        // Off the raw glTF JSON: three's loader builds AnimationClips and does not carry an
        // animation's extras onto them, so the parser's own document is the only source.
        visibility: visibilityTracks(gltf.parser.json),
    };
}

/**
 * A copy of the model's node tree for one part to own. Nodes, skeletons and user data are the
 * copy's; the vertex data and the glTF materials are the model's, shared with every other copy.
 */
export function partOf(model: ParsedModel): THREE.Object3D {
    return clone(model.scene);
}
