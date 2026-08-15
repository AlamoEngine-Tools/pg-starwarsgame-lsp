// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which CSS face the card draws a game font name with. Split out of encyclopediaCard.tsx so it can
// be unit-tested: the Arial remap is load-bearing calibration, not cosmetics - it is what makes the
// body wrap where the game wraps.

/**
 * The face the popup is measured in.
 *
 * `encyclopedia_text` says `Arial`, and Arial is provably not what the engine wraps with: the game
 * keeps "Skywalker trained under Jedi Master Yoda" on one line but breaks before "to become the
 * first of a new generation of", and in Arial the string it KEEPS is 1.6% *wider* than the one it
 * BREAKS - so no width can satisfy both. That holds for fractional/kerned measurement and for
 * GDI-style integer per-glyph advances at every size from 8px to 22px. Sweeping the common Windows
 * faces, Tahoma satisfies every break with a 1.5% margin, and it was the standard Windows UI font
 * of this game's era. Only Arial and the EmpireAtWar family are remapped; a mod naming any other
 * font gets what it asked for.
 */
export function cssFontStack(gameFontName: string): string {
    const family = gameFontName.trim();

    if (/^arial$/i.test(family)) { return "'Tahoma', 'Verdana', sans-serif"; }

    // One stack for the whole EmpireAtWar family: the -Bold/-Medium/-Light suffix is a WEIGHT, and
    // fontOf already strips it and applies weight separately.
    if (isSubstitutedFont(family)) { return "'Trebuchet MS', 'Segoe UI', 'Tahoma', sans-serif"; }

    return `'${family}', 'Tahoma', sans-serif`;
}

/**
 * Whether this game font is one the preview cannot draw with and has substituted.
 *
 * The EmpireAtWar-* faces are REAL TrueType fonts, but they ship embedded inside `StarWarsG.exe`
 * and their name tables credit Village Type & Design LLC - a commercial foundry. They are not
 * Petroglyph's to sublicense and not ours to redistribute, so the preview substitutes rather than
 * extracting them.
 *
 * Two rows are affected: the population number (`EmpireAtWar-Medium`) and the cost row
 * (`EmpireAtWar-Bold`). The body and header ask for Arial and are unaffected.
 *
 * The panel surfaces this rather than letting those rows quietly render in something else - the
 * numerals are the part a modder is most likely to be measuring against a screenshot.
 */
export function isSubstitutedFont(gameFontName: string): boolean {
    return /^empireatwar\b/i.test(gameFontName.trim());
}
