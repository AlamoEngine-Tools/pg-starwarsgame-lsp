// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The base game's shader sources the preview asks for by name, whatever the model.
//
// Shared by the webview, which draws every emitter through one of these effects, and the extension
// host, which decides from this list whether a folder holds the base sources (#395). The server's
// `ShaderSourceResolver.BaseShaders` is the same list; `scripts/base-shaders.test.js` holds the two
// together.

/** The header every effect includes, the particle effects as `..\AlamoEngine.fxh`. */
export const SHADER_HEADER = "AlamoEngine.fxh";

/**
 * The emitter's blend mode, as the protocol spells it, to the effect file that draws it.
 *
 * Measured in the game (FoC x64, 2024 release): the particle renderer binds one effect per render
 * mode, by the mode's index, and draws every particle of the emitter through it. The effects live
 * under the shaders' `Engine/` folder and are requested by bare name like any mesh effect.
 */
export const PARTICLE_EFFECT_BY_MODE: ReadonlyMap<string, string> = new Map([
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

/**
 * The base set: the header and every particle effect.
 *
 * What tells the base sources from a mod's shader folder. A mod ships compiled `.fxo` and at most a
 * few sources of its own - measured on five installed `Data\Art\Shaders` folders: none held a
 * particle effect source, and the two holding `.fx` sources each held a copy of the header, so the
 * header alone does not decide it. The mesh effects are left out because which ones a preview needs
 * depends on the model.
 */
export const BASE_SHADERS: readonly string[] = [
    SHADER_HEADER,
    ...new Set(PARTICLE_EFFECT_BY_MODE.values()),
];
