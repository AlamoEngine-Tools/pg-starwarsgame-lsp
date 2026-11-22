// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which of the engine's own effects draws an emitter.
//
// The mode-to-effect map lives in `shared/baseShaders.ts`, because the same effects are what tells
// a folder of the base shader sources from a mod's (#395).

import type { AlamoEmitter } from "../../protocol/modelPreview";
import { PARTICLE_EFFECT_BY_MODE } from "../../shared/baseShaders";

/** The effect file an emitter's blend mode is drawn with, or null for a mode this build does not know. */
export function particleEffectName(blendMode: string): string | null {
    return PARTICLE_EFFECT_BY_MODE.get(blendMode) ?? null;
}

/**
 * The engine effect an emitter is drawn with, to be fetched like any mesh effect - or null for a
 * heat sprite, which the distortion pass draws with the preview's own program.
 *
 * Read off the emitter's data, so it is the same answer before an emitter is built as after.
 */
export function emitterEffectName(emitter: AlamoEmitter): string | null {
    return emitter.properties.isHeatParticle
        ? null
        : particleEffectName(emitter.properties.blendMode);
}

/**
 * The textures an emitter samples, as the file spells them: its sprite, then the normal map a bump
 * emitter names. One it does not name is an empty string.
 */
export function emitterTextureNames(emitter: AlamoEmitter): string[] {
    return [emitter.colorTexture, emitter.normalTexture ?? ""];
}
