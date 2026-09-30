// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which CSS face the card draws a game font name with. Split out of encyclopediaCard.tsx so it can
// be unit-tested. The ask-then-degrade rule itself is shared - see shared/gameFontStack.

import {gameFontStack} from './shared/gameFontStack';

/**
 * The face the popup is drawn in: the one the component names, unless the preview cannot have it.
 *
 * Arial used to be remapped to Tahoma here, and that remap was load-bearing calibration - Tahoma
 * was the face that reproduced the shipped line breaks of Luke Skywalker's biography, which Arial
 * could not. The reasoning was sound and the conclusion was still wrong: the engine wraps this text
 * on a CHARACTER COUNT and measures no glyphs at all, so NO face could have reproduced those breaks
 * by width. The sweep was answering a question the engine never asks, and the cost was every row
 * rendering in a font the data did not name. See `encyclopediaWrap.ts` for the rule that replaced
 * it.
 *
 * Only the EmpireAtWar family is substituted now, because those faces genuinely are not ours to
 * ship. Everything else is drawn as asked.
 */
export function cssFontStack(gameFontName: string): string {
    const family = gameFontName.trim();

    // The EmpireAtWar faces are now ASKED FOR and then fallen back from, rather than replaced
    // outright - see gameFontStack for why naming them is free. A reader who has installed them
    // gets the real thing, including the dollar glyph that is the credits coin; everyone else gets
    // the fallback chain, which is what this returned unconditionally before.
    if (isSubstitutedFont(family)) {
        return gameFontStack(family, ['Trebuchet MS', 'Segoe UI', 'Tahoma']);
    }

    return gameFontStack(family, ['Tahoma']);
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
