// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a style template is allowed to contain, now that there is a token layer to contain it.
//
// Three rules, and the third is the one worth having. A misspelled custom property is not an error
// anywhere in this stack: CSS drops a declaration whose var() does not resolve, so
// `padding: var(--space-5)` is not 5px and not 4px, it is nothing. TypeScript sees a string, esbuild
// sees a string, the browser silently discards it. That is how a portalled tooltip can go to zero
// padding with a green build - which is exactly what would have happened had the layer not been
// mounted at the root as well as on the Shell.
//
// The other two stop the drift coming back. Nine paddings for one compact control, radius split
// 3px against 4px for the same intent, five relative font sizes inside 0.12em of each other: every
// one of those was a value re-decided at a call site because nothing said not to.

/** What went wrong, for a driver that wants to group or filter. */
const KINDS = {
    length: 'length',
    colour: 'colour',
    unknown: 'unknown',
};

// Properties that are ON a scale. Dimensions are deliberately absent: 128px is 128 because 136
// overflowed once the dock grew a scrollbar, and no scale has an opinion about that.
const SCALE_PROPERTY =
    /^(gap|row-gap|column-gap|padding|padding-(top|right|bottom|left)|margin|margin-(top|right|bottom|left)|border-radius|font-size)$/;

/** Replace every comment with spaces, keeping offsets and line breaks intact. */
function withoutComments(body) {
    return body.replace(/\/\*[\s\S]*?\*\//g, m => m.replace(/[^\n]/g, ' '));
}

/** The step a raw length should probably have used, named so the message can suggest it. */
function nearestSteps(px) {
    const steps = [1, 2, 4, 6, 8, 12, 16, 24];
    const best = steps.reduce((a, b) => (Math.abs(b - px) < Math.abs(a - px) ? b : a));
    const tie = steps.find(s => s !== best && Math.abs(s - px) === Math.abs(best - px));

    return tie === undefined ? `--space-${best}` : `--space-${best} or --space-${tie}`;
}

/**
 * Every offence in one style template body.
 *
 * `defined` is the set of custom properties the token layer declares. `allowRawLengths` and
 * `allowRawColours` are for the files that legitimately carry neither - the credits crawl
 * reproduces the game's own titles, and the encyclopedia card draws over game artwork, so a colour
 * there is game truth rather than a design decision.
 */
function tokenOffences(body, {
    defined, allowRawLengths = false, allowRawColours = false, alsoDeclared = new Set(),
} = {}) {
    const text = withoutComments(body);
    const found = [];

    for (const match of text.matchAll(/([a-z-]+)\s*:\s*([^;{}`]*?)\s*(?=[;}])/g)) {
        const [whole, property, value] = match;
        const valueAt = match.index + whole.indexOf(value, property.length);

        // A file declaring a custom property of its own is defining it, not referencing it.
        if (property.startsWith('--')) {
            continue;
        }

        // A length inside var(--host, 13px) is the fallback for a host variable, not a value chosen
        // here - the same reasoning that exempts a hex in that position.
        const chosen = value.replace(/var\(\s*--[a-zA-Z0-9-]+\s*,[^)]*\)/g, m => ' '.repeat(m.length));

        if (!allowRawLengths && SCALE_PROPERTY.test(property)) {
            // Negative lengths are skipped: a negative margin is nearly always cancelling a padding
            // somewhere else, and the pair has to move together or not at all.
            for (const px of chosen.matchAll(/(^|[^-\w.])([0-9.]+)px/g)) {
                found.push({
                    index: valueAt + px.index,
                    kind: KINDS.length,
                    text: `${property}: ${value}`,
                    hint: property === 'border-radius' ? '--radius-3 or --radius-6'
                        : property === 'font-size' ? '--font-size-11, --font-size-12 or an --icon-size-*'
                            : nearestSteps(parseFloat(px[2])),
                });
            }
        }

        if (!allowRawColours) {
            // Same exemption, same reason: a hex after the comma inside var() is what the panel
            // draws when the host defines nothing, not a colour someone picked here.
            for (const hex of chosen.matchAll(/#[0-9a-fA-F]{3,8}\b/g)) {
                found.push({
                    index: valueAt,
                    kind: KINDS.colour,
                    text: `${property}: ${value}`,
                    hint: `a --colour-* role, or add ${hex[0]} to the fitted block if it must not theme`,
                });
            }
        }
    }

    // Every reference, whether or not it sits on a scale property - this is the rule that catches a
    // typo, and a typo is just as silent in a property no scale covers.
    const declaredHere = new Set(
        [...text.matchAll(/(--[a-z0-9-]+)\s*:/g)].map(m => m[1]));

    // Only the form with no fallback. `var(--x, 100%)` resolves to 100% when --x is undefined, so
    // it behaves; it is `var(--x)` alone that takes the whole declaration with it.
    for (const reference of text.matchAll(/var\(\s*(--[a-zA-Z0-9-]+)\s*\)/g)) {
        const name = reference[1];

        if (name.startsWith('--vscode-')
            || defined.has(name) || declaredHere.has(name) || alsoDeclared.has(name)) {
            continue;
        }

        found.push({
            index: reference.index,
            kind: KINDS.unknown,
            text: `var(${name})`,
            hint: 'nothing defines this, so the whole declaration is dropped at runtime',
        });
    }

    return found.sort((a, b) => a.index - b.index);
}

module.exports = { KINDS, SCALE_PROPERTY, tokenOffences };
