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
import { type CaptureRig } from "./captureRig";
import { type DestructionRig } from "./destructionRig";
import { type EnvironmentRig } from "./environmentRig";
import { type LevelGates } from "./levelGates";
import { type MaterialBinder } from "./materialBinder";
import { type ModelQueries } from "./modelQueries";
import { type ModelRows } from "./modelRows";
import { type OverlayLayer } from "./overlayLayer";
import { type PartAssembly } from "./partAssembly";
import { type ParticleHost } from "./particleHost";
import { type TurretRig } from "./turretRig";

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
    /** Absolutely-positioned container the bone labels are written into. */
    readonly labelLayer: HTMLElement;
    readonly levels: LevelGates;
    readonly queries: ModelQueries;
    readonly assembly: PartAssembly;
    readonly destruction: DestructionRig;
    readonly animation: AnimationRig;
    readonly capturer: CaptureRig;
    readonly cameraRig: CameraRig;
    readonly environment: EnvironmentRig;
    readonly rows: ModelRows;
    readonly materials: MaterialBinder;
    readonly particles: ParticleHost;
    readonly overlay: OverlayLayer;
    readonly turret: TurretRig;
}
