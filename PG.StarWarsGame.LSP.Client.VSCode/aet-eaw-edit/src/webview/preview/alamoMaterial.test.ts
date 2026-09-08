// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { AlamoMaterial } from './alamoMaterial';
import { ALPHA_TEST_OFF, ALPHA_TEST_UNIFORM } from './fx/glslProgram';
import type { TranslatedEffect } from './fx/effect';

/** The smallest thing that is still a translated effect. */
const EFFECT: TranslatedEffect = {
    vertex: '#version 300 es\nvoid main() { gl_Position = vec4(0.0); }',
    fragment: '#version 300 es\nprecision highp float;\nout vec4 c;\nvoid main() { c = vec4(1.0); }',
    semantics: new Map(),
    defaults: new Map(),
    samplers: new Map(),
    types: new Map(),
};

describe('AlamoMaterial alpha test', () => {
    /**
     * A `RawShaderMaterial` gets none of three's shader chunks, so `material.alphaTest` never
     * reaches the GPU. `Tree.fx` asks for `AlphaRef = 0x80`, and without this its palm fronds -
     * a leaf texture on a plain rectangle - draw as solid green cards.
     */
    it('starts with the test off rather than discarding anything', () => {
        const material = new AlamoMaterial(EFFECT, {});

        assert.equal(material.uniforms[ALPHA_TEST_UNIFORM].value, ALPHA_TEST_OFF);
    });

    it('takes the threshold the effect declared', () => {
        const material = new AlamoMaterial(EFFECT, {});

        material.setAlphaTest(128 / 255);

        assert.ok(Math.abs(material.uniforms[ALPHA_TEST_UNIFORM].value - 128 / 255) < 1e-6);
    });

    /**
     * An additive glow is MEANT to draw where its alpha is zero - that family blends `ONE / ONE`
     * and never reads alpha - so an effect that declares no test must not get one by default.
     */
    it('switches the test back off when an effect declares none', () => {
        const material = new AlamoMaterial(EFFECT, {});

        material.setAlphaTest(0.5);
        material.setAlphaTest(null);

        assert.equal(material.uniforms[ALPHA_TEST_UNIFORM].value, ALPHA_TEST_OFF);
    });
});
