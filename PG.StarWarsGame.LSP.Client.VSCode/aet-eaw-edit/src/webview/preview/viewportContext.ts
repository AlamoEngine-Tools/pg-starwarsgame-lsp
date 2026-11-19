// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What every layer of the viewport shares: the scene objects, made once by the viewport and never
// replaced, and the layers themselves, so that one layer asks another directly instead of through the
// viewport. A layer's own host interface is then only what the viewport alone has.

import type * as THREE from "three";
import { type OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { type GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";

import { type LoadedPart } from "./loadedPart";
import { type AnimationRig } from "./animationRig";
import { type CameraRig } from "./cameraRig";
import { type EnvironmentRig } from "./environmentRig";
import { type LevelGates } from "./levelGates";
import { type MaterialBinder } from "./materialBinder";
import { type ModelQueries } from "./modelQueries";
import { type ModelRows } from "./modelRows";
import { type OverlayLayer } from "./overlayLayer";
import { type PartAssembly } from "./partAssembly";
import { type ParticleRig } from "./particleRig";
import { type TurretRig } from "./turretRig";
import { type TextureStore } from "./textureStore";

/**
 * A layer as the other layers see it: every field read-only, and the collections they read too.
 *
 * Only the layer itself writes its state; everyone else asks it to. Typing the context this way makes
 * a write from another layer a compile error rather than a convention. Methods are unaffected.
 */
export type LayerView<T, Collections extends keyof T = never> = Readonly<
    Omit<T, Collections>
> & {
    readonly [K in Collections]: T[K] extends Map<infer A, infer B>
        ? ReadonlyMap<A, B>
        : T[K] extends Set<infer A>
          ? ReadonlySet<A>
          : T[K] extends (infer A)[]
            ? readonly A[]
            : Readonly<T[K]>;
};

/** The scene objects and the layers, as the viewport hands them to every layer it builds. */
export interface ViewportContext {
    readonly scene: THREE.Scene;
    readonly camera: THREE.PerspectiveCamera;
    readonly renderer: THREE.WebGLRenderer;
    readonly canvas: HTMLCanvasElement;
    readonly controls: OrbitControls;
    readonly loader: GLTFLoader;
    /** Everything the model is drawn under: parts, effects, overlays. */
    readonly modelRoot: THREE.Group;
    /** The subject's own geometry, under its `Scale_Factor`. */
    readonly scaledRoot: THREE.Group;
    /** Every loaded part by id. The assembly is the only writer. */
    readonly parts: Map<string, LoadedPart>;
    /** Every decoded texture by name, for the materials and the particle systems alike. */
    readonly textures: TextureStore;
    /** Absolutely-positioned container the bone labels are written into. */
    readonly labelLayer: HTMLElement;
    /** The subject's `Scale_Factor`, or 1: what everything parented under `scaledRoot` that must stay a world length divides by. */
    readonly modelScale: number;
    readonly levels: LayerView<LevelGates>;
    readonly queries: LayerView<ModelQueries>;
    readonly assembly: LayerView<PartAssembly, "hiddenParts">;
    readonly animation: LayerView<AnimationRig, "subjects">;
    readonly cameraRig: LayerView<CameraRig, "billboards">;
    readonly environment: LayerView<EnvironmentRig>;
    readonly rows: LayerView<ModelRows, "rowParents" | "rowTargets">;
    readonly materials: LayerView<
        MaterialBinder,
        "shaderStates" | "translated"
    >;
    readonly particles: LayerView<ParticleRig, "particleSystems">;
    readonly overlay: LayerView<OverlayLayer>;
    readonly turret: LayerView<TurretRig>;
}
