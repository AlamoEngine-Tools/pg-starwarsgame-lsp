// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * A CSS font stack for a face the GAME DATA names: ask for it, then degrade.
 *
 * The rule, which is general and not specific to any one preview:
 *
 *   1. Name what the data asked for. Always, including faces the preview could never ship.
 *   2. For a hyphenated name, name the base family too - an installed copy may report either
 *      `EmpireAtWar-Bold` as its family or `EmpireAtWar` with `Bold` as a subfamily.
 *   3. Then the caller's fallbacks, in order, then a generic.
 *
 * Step 1 is the point. Some of the faces the game names ship embedded in its executable under a
 * commercial licence, so they are not ours to extract or redistribute - but NAMING one ships
 * nothing and licenses nothing, and a reader who installed it on their own machine then gets the
 * real thing rather than our approximation. Everyone else gets the fallback, which is what these
 * previews used to show unconditionally.
 *
 * Font names come from a mod's XML, so they are untrusted: quotes are stripped rather than emitted,
 * which stops a name from closing the CSS string and appending a declaration of its own.
 */
export function gameFontStack(gameFontName: string, fallbacks: readonly string[]): string {
    const asked: string[] = [];
    const family = clean(gameFontName);

    if (family.length > 0) {
        asked.push(family);

        // "EmpireAtWar-Bold" -> also try "EmpireAtWar". Only when it differs, so a name with no
        // suffix is not listed twice.
        const base = family.split('-')[0].trim();
        if (base.length > 0 && base !== family) {
            asked.push(base);
        }
    }

    const families = dedupe([
        ...asked,
        ...asked.flatMap(metricTwinsFor),
        ...fallbacks.map(clean).filter(f => f.length > 0),
        ...fallbacks.flatMap(f => metricTwinsFor(clean(f))),
    ]);
    return [...families.map(f => `'${f}'`), 'sans-serif'].join(', ');
}

/**
 * Metric-compatible stand-ins for the faces this game names, keyed lower-case.
 *
 * The game was authored on Windows and names Windows faces. The editor is not: on Linux neither
 * Arial nor Tahoma exists, and without these the stack falls through to the generic - typically
 * DejaVu Sans, which is appreciably wider than Arial and pushes text past the edge of a card whose
 * proportions were calibrated against the real thing.
 *
 * These are METRIC clones, chosen because they have the same advance widths rather than because
 * they look similar. Liberation and the Chrome OS faces (Arimo, Tinos, Cousine) are the two usual
 * families and are present on most distributions or installable everywhere.
 *
 * Note this no longer affects where LINES BREAK - that is a character count, not a measurement -
 * only whether the drawn text fits the space the card gives it.
 */
const METRIC_TWINS: Readonly<Record<string, readonly string[]>> = {
    'arial': ['Liberation Sans', 'Arimo', 'Helvetica'],
    'helvetica': ['Liberation Sans', 'Arimo'],
    'tahoma': ['DejaVu Sans', 'Verdana'],
    'verdana': ['DejaVu Sans'],
    'times new roman': ['Liberation Serif', 'Tinos'],
    'courier new': ['Liberation Mono', 'Cousine'],
    'trebuchet ms': ['Fira Sans', 'DejaVu Sans'],
    'segoe ui': ['Selawik', 'DejaVu Sans'],
};

function metricTwinsFor(family: string): readonly string[] {
    return METRIC_TWINS[family.toLowerCase()] ?? [];
}

function dedupe(families: readonly string[]): string[] {
    const seen = new Set<string>();
    return families.filter(f => {
        const key = f.toLowerCase();
        if (seen.has(key)) {
            return false;
        }
        seen.add(key);
        return true;
    });
}

/** A family name safe to sit inside a single-quoted CSS string. */
function clean(name: string): string {
    return name.replace(/['"\\]/g, '').trim();
}
