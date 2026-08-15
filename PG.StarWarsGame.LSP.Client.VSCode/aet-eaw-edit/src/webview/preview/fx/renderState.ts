// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Turning a pass's declared render state into the material state a renderer can set.
//
// This is the part of shader fidelity that needs no shader translation at all: the effects spell
// their blend, depth and cull state out declaratively, so reading it gets most of the visible
// difference for none of the HLSL risk.

import type { FxPass } from './fxParser';

/** How a pass composites. */
/**
 * The blend a pass declares.
 *
 * `additive` and `additiveAlpha` are kept apart on purpose. Both add to what is already there, and
 * they differ only in whether the source is scaled by its own alpha first - which is invisible until
 * a texture has no alpha channel worth the name, and then it is the difference between a mesh being
 * drawn and being multiplied away. See `blending.ts`.
 */
export type FxBlend =
    'opaque' | 'alpha' | 'additive' | 'additiveAlpha' | 'multiply' | 'custom';

/** Which faces survive. */
export type FxCull = 'back' | 'front' | 'none';

/** Depth comparison, named the way a renderer thinks of it. */
export type FxDepthFunc = 'less' | 'lessEqual' | 'greater' | 'greaterEqual' | 'equal' | 'always';

export interface FxMaterialState {
    depthTest: boolean;
    depthWrite: boolean;
    depthFunc: FxDepthFunc;
    blend: FxBlend;
    cull: FxCull;
    /** Alpha below which fragments are discarded, or null when alpha testing is off. */
    alphaTest: number | null;
}

/**
 * Reads a boolean render state.
 *
 * Case-insensitive because the shipped effects are genuinely inconsistent: `ZWriteEnable` appears as
 * `TRUE`, `true` and `False` across the set, and a case-sensitive read would silently take the
 * default for most of them.
 */
function readBool(states: Record<string, string>, key: string): boolean | undefined {
    const raw = states[key]?.toLowerCase();

    if (raw === 'true' || raw === '1') {
        return true;
    }
    if (raw === 'false' || raw === '0') {
        return false;
    }

    return undefined;
}

function readEnum(states: Record<string, string>, key: string): string | undefined {
    return states[key]?.toUpperCase();
}

const DEPTH_FUNCS: Record<string, FxDepthFunc> = {
    LESS: 'less',
    LESSEQUAL: 'lessEqual',
    GREATER: 'greater',
    GREATEREQUAL: 'greaterEqual',
    EQUAL: 'equal',
    ALWAYS: 'always',
};

const CULL_MODES: Record<string, FxCull> = {
    NONE: 'none',
    // D3D winds the other way round from most renderers: CW culls clockwise faces, leaving the
    // counter-clockwise ones - which is what "back-face culling" means here.
    CW: 'back',
    CCW: 'front',
};

/**
 * Which named blend the source and destination factors add up to.
 *
 * The four combinations below are the ones the shipped effects actually use. Anything else is
 * reported as `custom` rather than guessed at - a wrong blend is far more visible than a plain one,
 * and the caller can fall back to its archetype.
 */
function readBlend(states: Record<string, string>): FxBlend {
    if (readBool(states, 'alphablendenable') !== true) {
        return 'opaque';
    }

    const src = readEnum(states, 'srcblend') ?? 'ONE';
    const dst = readEnum(states, 'destblend') ?? 'ZERO';

    if (src === 'SRCALPHA' && dst === 'INVSRCALPHA') {
        return 'alpha';
    }
    if (src === 'ONE' && dst === 'ONE') {
        return 'additive';
    }
    if (src === 'SRCALPHA' && dst === 'ONE') {
        return 'additiveAlpha';
    }
    if (src === 'ONE' && dst === 'ZERO') {
        return 'opaque';
    }
    if ((src === 'ZERO' && dst === 'SRCCOLOR') || (src === 'DESTCOLOR' && dst === 'ZERO')) {
        return 'multiply';
    }

    return 'custom';
}

/**
 * The material state one pass asks for.
 *
 * Defaults follow Direct3D's own: depth testing and writing on, depth compare LESSEQUAL, back faces
 * culled, no blending. A pass that says nothing therefore draws as ordinary opaque geometry.
 */
export function materialStateFrom(pass: FxPass): FxMaterialState {
    const states = pass.states;

    const alphaTestOn = readBool(states, 'alphatestenable') === true;

    // `Number` rather than `parseFloat`, because every shipped effect writes this in hex -
    // `AlphaRef = 0x00000080` in `Tree.fx`. `parseFloat` stops at the `x` and returns 0, a cutoff
    // that discards nothing, which drew the game's foliage as solid rectangles.
    // Guarded against the empty string, which `Number` reads as a perfectly good zero.
    const written = (states.alpharef ?? '').trim();
    const alphaRef = written === '' ? Number.NaN : Number(written);

    return {
        depthTest: readBool(states, 'zenable') ?? true,
        depthWrite: readBool(states, 'zwriteenable') ?? true,
        depthFunc: DEPTH_FUNCS[readEnum(states, 'zfunc') ?? ''] ?? 'lessEqual',
        blend: readBlend(states),
        cull: CULL_MODES[readEnum(states, 'cullmode') ?? ''] ?? 'back',
        // AlphaRef is 0-255 in D3D; renderers want 0-1.
        alphaTest: alphaTestOn ? (Number.isFinite(alphaRef) ? alphaRef / 255 : 0.5) : null,
    };
}
