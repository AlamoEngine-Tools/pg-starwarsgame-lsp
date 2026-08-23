// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { BLOOM_SIZE, blurDelta, brightPass } from './bloomPass';

describe('brightPass', () => {
    // `Engine/SceneBloom.fx`, ps_bright_filter: a pixel below the cutoff is raised to the FIFTH
    // power rather than discarded. There is no threshold in practice - `BloomCutoff` ships at 1.0,
    // which luminance never reaches - so this branch is the whole filter.
    it('crushes a mid pixel almost to nothing', () => {
        assert.ok(Math.abs(brightPass(0.5) - 0.03125) < 1e-9);
    });

    it('leaves white alone', () => {
        assert.equal(brightPass(1), 1);
    });

    it('keeps most of a near-white pixel, which is what actually blooms', () => {
        assert.ok(brightPass(0.9) > 0.59 && brightPass(0.9) < 0.6);
    });

    it('is far gentler than a hard threshold at the bottom', () => {
        // A cutoff throws a 0.4 pixel away entirely; this keeps a hundredth of it, so a large dim
        // area still contributes a little and the effect does not switch on at an edge.
        assert.ok(brightPass(0.4) > 0);
        assert.ok(brightPass(0.4) < 0.011);
    });
});

describe('blurDelta', () => {
    // `delta = BloomSize * (half_pixel + 2.0f * BloomIteration * half_pixel)`, and `half_pixel` is
    // `0.5 / width` - AloViewer's `m_resolutionConstants.zw`.
    it('is a quarter of a half-texel on the first iteration', () => {
        assert.ok(Math.abs(blurDelta(0, 800) - BLOOM_SIZE * 0.5 / 800) < 1e-12);
    });

    it('widens by two half-texels each iteration', () => {
        const half = 0.5 / 800;

        assert.ok(Math.abs(blurDelta(1, 800) - BLOOM_SIZE * 3 * half) < 1e-12);
        assert.ok(Math.abs(blurDelta(2, 800) - BLOOM_SIZE * 5 * half) < 1e-12);
    });

    it('is measured in texture coordinates, so it halves when the buffer doubles', () => {
        assert.ok(Math.abs(blurDelta(3, 1600) - blurDelta(3, 800) / 2) < 1e-12);
    });
});
