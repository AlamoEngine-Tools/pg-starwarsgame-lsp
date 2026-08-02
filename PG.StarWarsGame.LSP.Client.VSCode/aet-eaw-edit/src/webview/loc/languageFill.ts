// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Filling a newly added language from the game's own translations.
//
// Adding a language to a mod's text file otherwise gives you an empty column and a long afternoon:
// most of the keys in it are the game's, and the game already ships them in that language. The
// values come from the baseline the editor has already fetched for the Inherited toggle.

import { LocRow } from './locRow';
import { BaselineRow } from '../translationInherited';

export interface LanguageFillValue { key: string; value: string; }

/**
 * What the game already says, in `language`, for the keys this file holds.
 *
 * Scoped to the file's own keys deliberately. The baseline is the entire game - tens of thousands of
 * entries - and filling from it wholesale would turn "add a language" into "import all of Empire at
 * War into my mod". Keys the game does not define are left empty, which is the honest result: there
 * is nothing to copy, and inventing something would be worse than a visible gap.
 */
export function languageFillValues(
    language: string, rows: LocRow[], baseline: BaselineRow[]
): LanguageFillValue[] {
    const wanted = language.toUpperCase();

    const fromBaseline = new Map<string, string>();
    for (const entry of baseline) {
        const value = entry.values.find(v => v.language.toUpperCase() === wanted)?.value;
        if (value) { fromBaseline.set(entry.key, value); }
    }

    const filled: LanguageFillValue[] = [];
    const seen = new Set<string>();
    for (const row of rows) {
        if (seen.has(row.key)) { continue; }
        seen.add(row.key);

        const value = fromBaseline.get(row.key);
        if (value !== undefined) { filled.push({ key: row.key, value }); }
    }

    return filled;
}
