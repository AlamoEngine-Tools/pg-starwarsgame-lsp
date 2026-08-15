// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a pass's declared render state - its blend and its depth handling - is expressed in three.
//
// One place, because there are two routes to a material - the archetype guessed from a shader's
// NAME, and the render state read out of the effect itself - and they have to land on the same
// blend. They did not: both said "additive" and both used three's AdditiveBlending, which is
// SRC_ALPHA, ONE.
//
// That matters because Alamo's additive effects are ONE, ONE. The two are identical on any texture
// with a sensible alpha channel and completely different on one without: `Ni_geonosian.dds` has an
// EMPTY alpha channel - measured, 4092 of its 4096 DXT5 blocks carry alpha endpoints (0, 1) - so
// scaling by alpha multiplied its wings away entirely. The engine adds them at full strength, which
// is why the model has wings in game and had none here.

import * as THREE from 'three';

import { type FxBlend, type FxDepthFunc, type FxMaterialState } from './fx/renderState';

/**
 * Puts an Alamo blend onto a three material.
 *
 * `custom` is deliberately a no-op: the effect declared a combination this does not model, and
 * whatever the archetype already chose is a better answer than a wrong translation of it.
 */
export function applyBlend(material: THREE.Material, blend: FxBlend): void {
    switch (blend) {
        case 'opaque':
            material.transparent = false;
            material.blending = THREE.NormalBlending;
            break;

        case 'alpha':
            material.transparent = true;
            material.blending = THREE.NormalBlending;
            break;

        case 'additive':
            // ONE, ONE - the source added as it is. NOT three's AdditiveBlending.
            material.transparent = true;
            material.blending = THREE.CustomBlending;
            material.blendEquation = THREE.AddEquation;
            material.blendSrc = THREE.OneFactor;
            material.blendDst = THREE.OneFactor;
            break;

        case 'additiveAlpha':
            // SRCALPHA, ONE - which is exactly what three calls AdditiveBlending.
            material.transparent = true;
            material.blending = THREE.AdditiveBlending;
            break;

        case 'multiply':
            material.transparent = true;
            material.blending = THREE.MultiplyBlending;
            break;

        default:
            break;
    }
}

/** Alamo's depth comparisons, in three's terms. */
const DEPTH_FUNCS: Record<FxDepthFunc, THREE.DepthModes> = {
    less: THREE.LessDepth,
    lessEqual: THREE.LessEqualDepth,
    greater: THREE.GreaterDepth,
    greaterEqual: THREE.GreaterEqualDepth,
    equal: THREE.EqualDepth,
    always: THREE.AlwaysDepth,
};

/**
 * Puts a pass's depth handling onto a three material.
 *
 * Includes the comparison the effect declared, which was read into `FxMaterialState` from the start
 * and then never applied to anything. Only `MeshOccludedUnit` and its RSkin twin declare anything
 * but LESSEQUAL - both gated off as non-visual - so it has been inert rather than wrong, but a mod
 * shader asking for GREATER was being silently ignored.
 *
 * NO depth bias is applied here, and that is deliberate rather than an omission. Not one shipped
 * Alamo effect declares one: the light meshes that sit directly on a hull are `ZWriteEnable = FALSE`
 * with `ZFunc = LESSEQUAL`, and the engine relies on plain depth precision to hold them apart. A
 * bias was tried and measured against the Victory Star Destroyer's `Lighting` mesh, which is the
 * worst case to hand: it changed nothing at all, from two units up to two hundred and fifty six.
 * Whatever residual instability that mesh shows is therefore NOT a depth tie - an overlay winning
 * every comparison outright still flickered by the same amount - so a bias would have been a change
 * with a reason attached to it that was not true.
 */
export function applyDepth(material: THREE.Material, state: Pick<FxMaterialState,
    'depthTest' | 'depthWrite' | 'depthFunc'>): void {
    material.depthTest = state.depthTest;
    material.depthWrite = state.depthWrite;
    material.depthFunc = DEPTH_FUNCS[state.depthFunc];
}
