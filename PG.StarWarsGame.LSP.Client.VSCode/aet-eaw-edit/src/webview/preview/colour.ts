// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Faction colours, between the three forms they take: the game's 0-255 channels, the picker's hex,
// and the renderer's 0-1.

import type { PreviewFaction, PreviewRgba } from '../../protocol/modelPreview';

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
/**
 * The colour to feed the shader's `Colorization`, which is the ONE colourisation parameter the
 * effects declare.
 *
 * Two values reach it, never two mechanisms. A faction's `Color` is the SKIRMISH tint. When no
 * faction colour applies - which is most of the time - the subject wears its own
 * `No_Colorization_Color` instead: a TIE Fighter is `75,75,75` whoever owns it, an indigenous Bantha
 * is `128,101,79`, and ten of the 25 shipped objects that declare one write pure white, which is the
 * identity for the multiply and means "leave my texture alone".
 *
 * Null when the subject declares nothing, so the viewport keeps its own identity rather than being
 * handed a white this cannot tell apart from a declared one.
 */
export function colorizationFor(
    chosen: PreviewRgba | null,
    noColorization: PreviewRgba | null | undefined,
    affiliation: PreviewRgba | null | undefined = null,
): NormalisedColour | null {
    if (chosen !== null) {
        return teamColour(chosen);
    }

    // The object's own word outranks its faction's default - a TIE is 75,75,75 whoever owns it.
    const own = noColorization ?? affiliation;

    return own === null || own === undefined ? null : teamColour(own);
}

/**
 * The uncoloured colour of the faction a subject belongs to, or null when there is none to have.
 *
 * The fallback that reaches most units: 772 shipped objects declare an `Affiliation` and only 24 of
 * them also declare a colour of their own. Rebel is the case that shows - its `199,105,59` is a real
 * orange-brown, where Empire's and Neutral's are pure white and change nothing.
 *
 * Null for a faction that declares none, and null for an affiliation no faction matches: a mod can
 * name a faction that was never defined, and that is an authoring mistake to surface elsewhere
 * rather than a reason to invent a colour here.
 */
export function affiliationColour(
    factions: readonly PreviewFaction[],
    affiliation: string | null | undefined,
): PreviewRgba | null {
    if (affiliation === null || affiliation === undefined || affiliation.length === 0) {
        return null;
    }

    return factions.find(faction => faction.name === affiliation)?.noColorizationColor ?? null;
}

export function teamColour(colour: PreviewRgba): NormalisedColour {
    return {
        r: clampChannel(colour.r) / 255,
        g: clampChannel(colour.g) / 255,
        b: clampChannel(colour.b) / 255,
        a: 1,
    };
}
