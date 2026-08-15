// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import { applyBlend, applyDepth } from './materialState';
import { type FxDepthFunc } from './fx/renderState';

const material = (): THREE.MeshBasicMaterial => new THREE.MeshBasicMaterial();

describe('applyBlend', () => {
    it('adds an additive pass at full strength, whatever its alpha says', () => {
        // ONE, ONE. three's AdditiveBlending is SRC_ALPHA, ONE, which is a different blend - and
        // over a texture with an empty alpha channel it is the difference between the Geonosian's
        // wings being there and not being there at all.
        const m = material();
        applyBlend(m, 'additive');

        assert.equal(m.blending, THREE.CustomBlending);
        assert.equal(m.blendSrc, THREE.OneFactor);
        assert.equal(m.blendDst, THREE.OneFactor);
        assert.equal(m.transparent, true);
    });

    it('scales an alpha-weighted additive pass by its alpha', () => {
        const m = material();
        applyBlend(m, 'additiveAlpha');

        assert.equal(m.blending, THREE.AdditiveBlending);
        assert.equal(m.transparent, true);
    });

    it('blends an alpha pass normally', () => {
        const m = material();
        applyBlend(m, 'alpha');

        assert.equal(m.blending, THREE.NormalBlending);
        assert.equal(m.transparent, true);
    });

    it('multiplies a multiply pass', () => {
        const m = material();
        applyBlend(m, 'multiply');

        assert.equal(m.blending, THREE.MultiplyBlending);
        assert.equal(m.transparent, true);
    });

    it('leaves an opaque pass opaque', () => {
        const m = material();
        m.transparent = true;
        applyBlend(m, 'opaque');

        assert.equal(m.blending, THREE.NormalBlending);
        assert.equal(m.transparent, false);
    });

    it('does not touch a material whose blend it cannot name', () => {
        // `custom` means the effect declared a combination we do not model. The archetype's guess
        // is better than a wrong translation of it.
        const m = material();
        applyBlend(m, 'additive');
        applyBlend(m, 'custom');

        assert.equal(m.blending, THREE.CustomBlending);
        assert.equal(m.blendSrc, THREE.OneFactor);
    });
});

describe('applyDepth', () => {
    const state = (over: Partial<{
        depthTest: boolean; depthWrite: boolean; depthFunc: FxDepthFunc;
    }> = {}) => ({ depthTest: true, depthWrite: true, depthFunc: 'lessEqual' as FxDepthFunc, ...over });

    it('honours the comparison the effect declared', () => {
        // Parsed into FxMaterialState since the beginning and then dropped on the floor: nothing
        // ever put it on a material. Only MeshOccludedUnit and its RSkin twin declare anything but
        // LESSEQUAL, and both are gated off as non-visual, so it has been inert rather than wrong -
        // but a mod shader declaring GREATER would have been silently ignored.
        const m = material();
        applyDepth(m, state({ depthFunc: 'greater' }));

        assert.equal(m.depthFunc, THREE.GreaterDepth);
    });

    it('carries depth test and write across', () => {
        const m = material();
        applyDepth(m, state({ depthTest: false, depthWrite: false }));

        assert.equal(m.depthTest, false);
        assert.equal(m.depthWrite, false);
    });

});
