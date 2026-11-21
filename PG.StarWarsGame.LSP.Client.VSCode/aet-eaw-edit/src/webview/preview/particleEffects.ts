// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which of the engine's own effects draws an emitter.
//
// The mode-to-effect map lives in `shared/baseShaders.ts`, because the same effects are what tells
// a folder of the base shader sources from a mod's (#395).

import { PARTICLE_EFFECT_BY_MODE } from "../../shared/baseShaders";

/** The effect file an emitter's blend mode is drawn with, or null for a mode this build does not know. */
export function particleEffectName(blendMode: string): string | null {
    return PARTICLE_EFFECT_BY_MODE.get(blendMode) ?? null;
}
