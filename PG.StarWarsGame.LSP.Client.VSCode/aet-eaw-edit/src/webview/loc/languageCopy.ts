// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Copying one language's values into the cells another language has left empty.
//
// Most of a credits file is proper nouns - a voice actor's name is the same in every language - so a
// second language column is mostly a copy of the first with a handful of role labels translated.
// Typing it out again is work nobody should do.
//
// It matters beyond convenience: the DAT export skips empty values, so a row the target language has
// not filled in is dropped from that language's file entirely. In a crawl that closes the gap up -
// a label can end up with no name under it, and a spacer marked only in the source language
// disappears, so the German crawl runs on where the English one breathes.

import { LocRow } from './locRow';

export interface LanguageCopyValue { index: number; key: string; value: string }

function valueOf(row: LocRow, language: string): string | undefined {
    return row.values.find(v => v.language.toUpperCase() === language.toUpperCase())?.value;
}

/**
 * What `to` would gain by copying `from`.
 *
 * Only cells the target has left empty - anything it already says is a deliberate translation and
 * is never overwritten. The blank-line marker is copied like any other value, because a gap the
 * target language does not carry is a gap it loses on export.
 */
export function languageCopyValues(
    from: string, to: string, rows: LocRow[]
): LanguageCopyValue[] {
    if (from.toUpperCase() === to.toUpperCase()) { return []; }

    const copied: LanguageCopyValue[] = [];
    for (const row of rows) {
        const source = valueOf(row, from);
        if (source === undefined || source.trim() === '') { continue; }

        // Undefined and blank are both "not filled in": a row may have no cell for the language at
        // all, which is how the XML format represents an absent translation.
        const target = valueOf(row, to);
        if (target !== undefined && target.trim() !== '') { continue; }

        copied.push({ index: row.index, key: row.key, value: source });
    }

    return copied;
}
