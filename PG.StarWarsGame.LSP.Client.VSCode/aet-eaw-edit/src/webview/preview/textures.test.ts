// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { addressingFor } from './textures';

describe('addressingFor', () => {
    it('repeats a power-of-two texture, which is what the samplers ask for', () => {
        // Every sampler_state in the shipped shaders declares AddressU/V = WRAP.
        assert.equal(addressingFor(512, 512), 'repeat');
        assert.equal(addressingFor(1024, 256), 'repeat');
        assert.equal(addressingFor(1, 1), 'repeat');
    });

    it('clamps a non-power-of-two one, because WebGL1 draws it black otherwise', () => {
        // A black hull is a worse failure than a smeared one.
        assert.equal(addressingFor(1024, 768), 'clamp');
        assert.equal(addressingFor(118, 118), 'clamp');
    });

    it('clamps when a dimension is unknown', () => {
        // A compressed texture's `image` may carry no width at all.
        assert.equal(addressingFor(undefined, 512), 'clamp');
        assert.equal(addressingFor(512, undefined), 'clamp');
    });

    it('clamps nonsense rather than treating it as valid', () => {
        assert.equal(addressingFor(0, 0), 'clamp');
        assert.equal(addressingFor(-256, 256), 'clamp');
        assert.equal(addressingFor(256.5, 256), 'clamp');
    });
});
