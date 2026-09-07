// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Turning a colour token into a concrete colour, for the one consumer that cannot use var().
//
// A 2D canvas context takes a string and paints it; it has no cascade and no idea what a custom
// property is. So anything drawn on a canvas has, up to now, been written as a hex literal beside
// the CSS that named a variable for the same colour - which is how the story graph ended up with
// four copies of one palette and canvas colours that did not follow the user's theme.
//
// The resolution order is deliberate, and the middle step is the interesting one:
//
//   1. the token itself, --colour-data-blue
//   2. the host variable behind it, --vscode-charts-blue
//   3. the token's literal fallback
//
// Step 1 alone should be enough: a custom property's computed value has its var() substituted, so
// reading --colour-data-blue ought to return the theme's blue. I have not been able to prove that
// in this repo - there is no browser harness here to run it in - so step 2 reads the variable the
// token defers to, and the chain only reaches step 3 if the host defines neither. The failure mode
// if step 1 does turn out to be a no-op is therefore the same colour by a longer route, never a
// missing one.

import { colourTokens } from './tokens';

const BY_NAME = new Map(colourTokens.map(token => [token.name, token]));

/** Reads one custom property off a computed style. Injected so the chain itself is testable. */
export type PropertyReader = (name: string) => string;

/**
 * The resolution chain, given a way to read properties.
 *
 * An unknown token resolves to the empty string rather than to a guess: a caller painting with it
 * would draw nothing, which is a visible bug, where a plausible grey would be an invisible one.
 */
export function resolveColourWith(read: PropertyReader, token: string): string {
    const known = BY_NAME.get(token);

    if (!known) {
        return '';
    }

    return read(token).trim() || read(known.from).trim() || known.fallback;
}

/**
 * A resolver bound to one element, for a draw pass.
 *
 * Call this ONCE per frame and index the result, never once per node: it reads the computed style,
 * which is not free, and the LOD overview exists specifically to keep a large campaign cheap to
 * draw. Resolving per frame rather than caching across frames is what lets a theme switch take
 * effect without anything having to listen for it.
 */
export function colourResolver(element: Element): (token: string) => string {
    const style = getComputedStyle(element);
    const resolved = new Map<string, string>();

    return token => {
        let value = resolved.get(token);

        if (value === undefined) {
            value = resolveColourWith(name => style.getPropertyValue(name), token);
            resolved.set(token, value);
        }

        return value;
    };
}
