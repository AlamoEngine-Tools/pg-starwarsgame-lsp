// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Starting an empty credits file, from the game's credits or from a sibling language file.
//
// Distinct from creditsBaselineFill, which fills gaps in rows that already exist by matching what a
// line says in another language. An empty file has no rows to fill and no other language to match
// on, so starting one ADDS lines rather than filling cells - a different operation, which is why it
// lives here rather than as a branch inside the other.
//
// Both producers answer in the same shape, so the editor has one path that turns seed rows into
// staged inserts however they were sourced.

import { LocRow } from './locRow';
import { BaselineRow } from '../translationInherited';

/** One line to add: the formatting directive it uses, and what it says. */
export interface CreditsSeedRow {
    key: string;
    value: string;
}

/**
 * The game's credits for `language`, in the order the crawl plays them.
 *
 * Lines the game has nothing for in this language are left out rather than added blank: an empty
 * line is a line the export drops anyway, so adding one would only be a row to delete by hand. The
 * blank-line marker is kept - it is a value like any other, and it is what gives the crawl its
 * pauses.
 */
export function creditsBaselineSeed(
    language: string, baseline: readonly BaselineRow[],
): CreditsSeedRow[] {
    const wanted = language.toUpperCase();
    const seeded: CreditsSeedRow[] = [];

    for (const entry of baseline) {
        const value = entry.values.find(v => v.language.toUpperCase() === wanted)?.value;
        if (value === undefined || value.trim() === '') { continue; }

        seeded.push({ key: entry.key, value });
    }

    return seeded;
}

/**
 * Every line of a sibling language file, as a starting point to translate over.
 *
 * The whole file, in its own order, keys included: what is being copied is the shape of the crawl,
 * not a set of lookups. That is the difference from {@link creditsBaselineSeed}, which skips what
 * it has nothing for - there the baseline is a dictionary, here the source IS the template, and a
 * line the source leaves blank is still a line of the running order.
 *
 * Safe only into a file with nothing in it. Into a populated one this would append someone else's
 * list to content that is already there, which is the alignment problem seeding exists to avoid.
 */
export function creditsFileSeed(
    rows: readonly LocRow[], sourceLanguage: string,
): CreditsSeedRow[] {
    const wanted = sourceLanguage.toUpperCase();

    return rows.map(row => ({
        key: row.key,
        value: row.values.find(v => v.language.toUpperCase() === wanted)?.value ?? '',
    }));
}
