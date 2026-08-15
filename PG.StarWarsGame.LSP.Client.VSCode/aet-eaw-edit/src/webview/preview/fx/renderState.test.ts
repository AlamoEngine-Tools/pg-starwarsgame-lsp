// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { FxPass } from './fxParser';
import { materialStateFrom } from './renderState';

const pass = (states: Record<string, string>): FxPass => ({ name: 'p0', states });

describe('materialStateFrom', () => {
    it('takes D3D defaults for a pass that declares nothing', () => {
        const state = materialStateFrom(pass({}));

        assert.equal(state.depthTest, true);
        assert.equal(state.depthWrite, true);
        assert.equal(state.depthFunc, 'lessEqual');
        assert.equal(state.blend, 'opaque');
        assert.equal(state.cull, 'back');
        assert.equal(state.alphaTest, null);
    });

    it('reads booleans however they are cased', () => {
        // The shipped effects genuinely mix these: ZWriteEnable appears as TRUE, true and False
        // across the set, so a case-sensitive read would silently default most of them.
        for (const written of ['TRUE', 'true', 'True']) {
            assert.equal(materialStateFrom(pass({ zwriteenable: written })).depthWrite, true,
                written);
        }

        for (const written of ['FALSE', 'false', 'False']) {
            assert.equal(materialStateFrom(pass({ zwriteenable: written })).depthWrite, false,
                written);
        }
    });

    it('reads enums however they are cased', () => {
        assert.equal(materialStateFrom(pass({ zfunc: 'Less' })).depthFunc, 'less');
        assert.equal(materialStateFrom(pass({ zfunc: 'LESSEQUAL' })).depthFunc, 'lessEqual');
        assert.equal(materialStateFrom(pass({ cullmode: 'none' })).cull, 'none');
        assert.equal(materialStateFrom(pass({ cullmode: 'NONE' })).cull, 'none');
    });

    it('recognises ordinary alpha blending', () => {
        const state = materialStateFrom(pass({
            alphablendenable: 'TRUE', srcblend: 'SRCALPHA', destblend: 'INVSRCALPHA',
        }));

        assert.equal(state.blend, 'alpha');
    });

    it('keeps the two additive blends apart, because they differ over alpha', () => {
        // ONE,ONE adds the source as it is. SRCALPHA,ONE scales it by the source's alpha first.
        // Collapsing both into one blend hid the Geonosian's wings completely: they draw with
        // MeshAdditiveVColor over Ni_geonosian.dds, whose alpha channel is empty - measured, 4092
        // of its 4096 DXT5 blocks carry alpha endpoints (0, 1) - so scaling by alpha multiplied
        // them to nothing while the engine added them at full strength.
        assert.equal(materialStateFrom(pass({
            alphablendenable: 'TRUE', srcblend: 'ONE', destblend: 'ONE',
        })).blend, 'additive');

        assert.equal(materialStateFrom(pass({
            alphablendenable: 'TRUE', srcblend: 'SRCALPHA', destblend: 'ONE',
        })).blend, 'additiveAlpha');
    });

    it('ignores the blend factors when blending is switched off', () => {
        // A pass often leaves stale factors set with AlphaBlendEnable FALSE; honouring them would
        // make opaque geometry translucent.
        const state = materialStateFrom(pass({
            alphablendenable: 'FALSE', srcblend: 'SRCALPHA', destblend: 'INVSRCALPHA',
        }));

        assert.equal(state.blend, 'opaque');
    });

    it('reports an unrecognised combination rather than guessing', () => {
        // A wrong blend is far more visible than a plain one, so the caller gets to fall back.
        const state = materialStateFrom(pass({
            alphablendenable: 'TRUE', srcblend: 'BOTHINVSRCALPHA', destblend: 'SRCCOLOR',
        }));

        assert.equal(state.blend, 'custom');
    });

    it('turns D3D winding into back-face culling', () => {
        // CW culls clockwise faces, which is what a renderer calls culling the back.
        assert.equal(materialStateFrom(pass({ cullmode: 'CW' })).cull, 'back');
        assert.equal(materialStateFrom(pass({ cullmode: 'CCW' })).cull, 'front');
    });

    it('scales the alpha reference out of 255', () => {
        const state = materialStateFrom(pass({ alphatestenable: 'TRUE', alpharef: '128' }));

        assert.ok(state.alphaTest !== null && Math.abs(state.alphaTest - 128 / 255) < 1e-6);
    });

    /**
     * Every shipped effect writes this one in HEX - `AlphaRef = 0x00000080` in `Tree.fx`,
     * `0x00000008` in `Grass.fx`. `parseFloat` stops at the `x` and hands back 0, which is a cutoff
     * that discards nothing: the foliage cards came out as solid rectangles with the leaves
     * painted on. The decimal form in the old fixture is a shape no shipped shader uses.
     */
    it('reads a hex alpha reference, which is the only form the shipped effects use', () => {
        const state = materialStateFrom(
            pass({ alphatestenable: 'TRUE', alpharef: '0x00000080' }));

        assert.ok(state.alphaTest !== null && Math.abs(state.alphaTest - 128 / 255) < 1e-6,
            `${state.alphaTest}`);
    });

    it('falls back to a sane cutoff when the reference is unreadable', () => {
        assert.equal(
            materialStateFrom(pass({ alphatestenable: 'TRUE', alpharef: 'nonsense' })).alphaTest,
            0.5);
    });

    it('picks a sane cutoff when alpha testing is on but unspecified', () => {
        assert.equal(materialStateFrom(pass({ alphatestenable: 'TRUE' })).alphaTest, 0.5);
    });

    it('leaves alpha testing off unless it is switched on', () => {
        assert.equal(materialStateFrom(pass({ alpharef: '128' })).alphaTest, null);
    });
});
