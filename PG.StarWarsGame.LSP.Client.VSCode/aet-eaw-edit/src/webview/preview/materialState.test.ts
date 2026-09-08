// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import {
    applyBaseMapColourSpace, applyBlend, applyColourScale, applyDepth, shadowCatcherOpacity,
} from './materialState';
import { type FxDepthFunc } from './fx/renderState';

const material = (): THREE.MeshBasicMaterial => new THREE.MeshBasicMaterial();

const scaleUniform = (m: THREE.Material): { value: number } | undefined =>
    (m.userData as { colourScale?: { value: number } }).colourScale;

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

// The fixed-function `MODULATE2X` the four shader-less effects declare. Driven by a UNIFORM rather
// than by `material.color`, because two other places set that colour to white outright - the
// colorization tint and the moment a base texture binds - and a multiplier living there would be
// quietly reset by whichever ran last.
describe('applyColourScale', () => {
    it('installs the scale so a doubled pass is actually doubled', () => {
        const m = material();
        applyColourScale(m, 2);

        assert.equal(scaleUniform(m)?.value, 2);
    });

    it('is idempotent - re-applying does not stack another injection', () => {
        const m = material();
        applyColourScale(m, 2);
        const first = scaleUniform(m);
        applyColourScale(m, 2);

        assert.equal(scaleUniform(m), first);
        assert.equal(first?.value, 2);
    });

    it('goes back to unscaled when the effect asks for no doubling', () => {
        const m = material();
        applyColourScale(m, 2);
        applyColourScale(m, 1);

        assert.equal(scaleUniform(m)?.value, 1);
    });

    // A material that never needs scaling must not be given an injection at all: it would compile a
    // second program for an operation that multiplies by one.
    it('installs nothing for a plain unscaled material', () => {
        const m = material();
        applyColourScale(m, 1);

        assert.equal(scaleUniform(m), undefined);
        assert.equal(m.onBeforeCompile, THREE.Material.prototype.onBeforeCompile);
    });
});

// The engine does NO colour management: `Engine/Global.fx` sets `SRGBTexture = false` in its global
// sampler block, AloViewer sets no sRGB state anywhere and every surface is plain A8R8G8B8. A
// translated effect runs that same HLSL, so it has to sample the stored bytes, not a decoded copy.
//
// Three's own materials are a different case and are deliberately left as they were: decode, light,
// encode is self-consistent, and it agrees with the engine wherever the operation is just "show this
// texture". Only multiplies pull them apart, and that path is an approximation of fixed-function
// shading anyway.
describe('applyBaseMapColourSpace', () => {
    const fresh = (): THREE.Texture => new THREE.Texture();

    it('leaves the raw bytes when only translated effects want the texture', () => {
        const t = fresh();
        applyBaseMapColourSpace(t, false);

        assert.equal(t.colorSpace, THREE.NoColorSpace);
    });

    it('decodes when one of three own materials wants it', () => {
        const t = fresh();
        applyBaseMapColourSpace(t, true);

        assert.equal(t.colorSpace, THREE.SRGBColorSpace);
    });

    // A pure function of the answer, deliberately, and this is the test that would have caught the
    // first attempt. That one refused to take a decode BACK, which looked like a safe tie-break and
    // silently defeated the whole fix: textures arrive and bind while the materials are still
    // archetypes, and only later do the shaders turn up and swap in the translated ones. Every
    // texture had therefore been marked sRGB before any translated material ever saw it.
    // The colour space picks the GPU internal format, so a texture already uploaded has to be sent
    // again or the change is inert. This is what made the first live run read "(none)" on the
    // texture and still render the old 22.1 - the mark had moved and the upload had not.
    // `needsUpdate` is write-only in three - it bumps `version` and has no getter - so the version
    // is what can actually be asserted on.
    it('re-uploads when the answer changes', () => {
        const t = fresh();
        applyBaseMapColourSpace(t, true);
        const before = t.version;

        applyBaseMapColourSpace(t, false);

        assert.ok(t.version > before, `${before} -> ${t.version}`);
    });

    it('does not re-upload when the answer is the same', () => {
        const t = fresh();
        applyBaseMapColourSpace(t, true);
        const before = t.version;

        applyBaseMapColourSpace(t, true);

        assert.equal(t.version, before);
    });

    it('answers only to what it is told, in both directions', () => {
        const t = fresh();

        applyBaseMapColourSpace(t, true);
        applyBaseMapColourSpace(t, false);
        assert.equal(t.colorSpace, THREE.NoColorSpace);

        applyBaseMapColourSpace(t, true);
        assert.equal(t.colorSpace, THREE.SRGBColorSpace);
    });
});

// The shadow value the reader sets is a MULTIPLIER - `DEFAULT_LIGHTS.shadow` is [0.5,0.5,0.5],
// meaning "half as bright" - and the stencil darken uses it that way. The shadow-map CATCHER is a
// `THREE.ShadowMaterial`, which PAINTS its colour instead, so handing it the same value painted mid
// grey onto a dark floor: a shadow BRIGHTER than the ground it fell on. That is what the trees show.
//
// A paint can express a neutral multiply exactly - black at alpha `1 - m` gives `dst * m` - so the
// catcher takes black and this decides the alpha.
describe('shadowCatcherOpacity', () => {
    it('turns "half as bright" into a half-strength black paint', () => {
        assert.ok(Math.abs(shadowCatcherOpacity('#808080') - 0.498) < 0.01,
            `${shadowCatcherOpacity('#808080')}`);
    });

    // The trap this file has already been caught by once: `THREE.Color` decodes an sRGB hex to
    // LINEAR, so reading `.r` off #808080 gives 0.216 and an opacity of 0.784 - darkening to a fifth
    // where the reader asked for a half. The multiplier is the value as WRITTEN.
    it('reads the multiplier as written, not as three decodes it', () => {
        assert.ok(shadowCatcherOpacity('#808080') < 0.6,
            `0.784 would mean the linear value was used: ${shadowCatcherOpacity('#808080')}`);
    });

    it('paints solid for a shadow that admits no light', () => {
        assert.equal(shadowCatcherOpacity('#000000'), 1);
    });

    // Within a rounding error of nothing, not exactly nothing: the luminance weights do not sum to
    // exactly one in binary, so white lands 1e-16 off. That is the test's problem, not the code's.
    it('paints nothing for a shadow that darkens nothing', () => {
        assert.ok(shadowCatcherOpacity('#ffffff') < 1e-6,
            `${shadowCatcherOpacity('#ffffff')}`);
    });
});
