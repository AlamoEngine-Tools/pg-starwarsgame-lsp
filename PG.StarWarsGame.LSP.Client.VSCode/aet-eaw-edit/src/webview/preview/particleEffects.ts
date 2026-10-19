// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which of the engine's own effects draws an emitter.
//
// Measured in the game (FoC x64, 2024 release): the particle renderer binds one effect per render
// mode, by the mode's index, and draws every particle of the emitter through it. The effects live
// under the shaders' `Engine/` folder and are requested by bare name like any mesh effect.

/** The emitter's blend mode, as the protocol spells it, to the effect file that draws it. */
const EFFECT_BY_MODE: ReadonlyMap<string, string> = new Map([
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
]);

/** The effect file an emitter's blend mode is drawn with, or null for a mode this build does not know. */
export function particleEffectName(blendMode: string): string | null {
    return EFFECT_BY_MODE.get(blendMode) ?? null;
}
