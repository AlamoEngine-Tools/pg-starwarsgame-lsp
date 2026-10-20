// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { particleEffectName } from "./particleEffects";

describe("particleEffectName", () => {
    /**
     * Measured in the game (FoC x64, 2024 release): the emitter's render mode picks the effect by
     * index - 0 PrimOpaque, 1 PrimAdditive, 2 PrimAlpha, 3 PrimModulate, 4 to 6 the depth sprites,
     * 7 PrimDiffuseAlpha, 8 and 9 the stencil darkeners, 10 PrimHeat, 11 PrimParticleBumpAlpha,
     * 12 PrimDecalBumpAlpha, 13 PrimAlphaScanlines.
     */
    const expected: [string, string][] = [
        ["None", "PrimOpaque.fx"],
        ["Additive", "PrimAdditive.fx"],
        ["Transparent", "PrimAlpha.fx"],
        ["Inverse", "PrimModulate.fx"],
        ["DepthAdditive", "PrimDepthSpriteAdditive.fx"],
        ["DepthTransparent", "PrimDepthSpriteAlpha.fx"],
        ["DepthInverse", "PrimDepthSpriteModulate.fx"],
        ["DiffuseTransparent", "PrimDiffuseAlpha.fx"],
        ["StencilDarken", "StencilDarkenToAlpha.fx"],
        ["StencilDarkenBlur", "StencilDarkenFinalBlur.fx"],
        ["Heat", "PrimHeat.fx"],
        ["Bump", "PrimParticleBumpAlpha.fx"],
        ["DecalBump", "PrimDecalBumpAlpha.fx"],
        ["Scanlines", "PrimAlphaScanlines.fx"],
    ];

    for (const [mode, file] of expected) {
        it(`names ${file} for the ${mode} mode`, () => {
            assert.equal(particleEffectName(mode), file);
        });
    }

    it("names nothing for a mode this build does not know", () => {
        assert.equal(particleEffectName("Plasma"), null);
    });
});
