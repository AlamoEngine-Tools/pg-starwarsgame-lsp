// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Faction colours, between the three forms they take: the game's 0-255 channels, the picker's hex,
// and the renderer's 0-1.

import type { PreviewRgba } from '../../protocol/modelPreview';

/** Normalised channels, as the renderer wants them. */
export interface NormalisedColour {
    r: number;
    g: number;
    b: number;
    a: number;
}

function clampChannel(value: number): number {
    return Math.min(255, Math.max(0, Math.round(value)));
}

/** A colour as `#rrggbb`, which is the only form `<input type="color">` accepts. */
export function toHex(colour: PreviewRgba): string {
    const channel = (value: number): string =>
        clampChannel(value).toString(16).padStart(2, '0');

    return `#${channel(colour.r)}${channel(colour.g)}${channel(colour.b)}`;
}

/** Reads `#rrggbb`, or null when the text is not one. Alpha is opaque, as the engine forces it. */
export function parseHex(text: string): PreviewRgba | null {
    const match = /^#?([0-9a-f]{6})$/i.exec(text.trim());
    if (match === null) {
        return null;
    }

    const value = Number.parseInt(match[1], 16);

    return {
        r: (value >> 16) & 0xff,
        g: (value >> 8) & 0xff,
        b: value & 0xff,
        a: 255,
    };
}

/**
 * A faction colour as the renderer takes it.
 *
 * Alpha is forced opaque, matching `RenderObject::SetColorization`, which comments the same rule:
 * "always force alpha channel to 100%". Vanilla factions do ship colours with other alphas, and
 * honouring them would make those units translucent in a way the game never shows.
 */
export function teamColour(colour: PreviewRgba): NormalisedColour {
    return {
        r: clampChannel(colour.r) / 255,
        g: clampChannel(colour.g) / 255,
        b: clampChannel(colour.b) / 255,
        a: 1,
    };
}
