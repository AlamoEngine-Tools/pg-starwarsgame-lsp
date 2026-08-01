// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Inherited-value handling: which rows in this file merely repeat what a lower layer or the game
// baseline already says, so the grid can hide them and show what the mod actually changes.
//
// A mod's text file is usually a copy of the game's with a handful of lines altered. Without this,
// opening one means scrolling 19,000 identical rows looking for the dozen that differ.
//
// Keyed text files only. A credits file keys every row by a formatting directive, so matching by
// key against a baseline would pair unrelated lines.

import { LocRow, LocValue } from './loc/locRow';

export interface BaselineRow { key: string; values: LocValue[]; }

/** The shape `aet/getBaselineEntries` returns. */
export interface BaselineEntryDto { key: string; translations: Record<string, string> | undefined }

/**
 * Converts the server's entries into the grid's row shape.
 *
 * The language names are upper-cased on the way through: the server sends translations as a
 * dictionary, and OmniSharp camel-cases dictionary keys, so `ENGLISH` arrives as `eNGLISH`. Left
 * alone, no baseline language would ever match a displayed one and every row would look locally
 * overridden - the failure is silent, which is why it is handled at the boundary rather than at
 * each use.
 */
export function toBaselineRows(entries: BaselineEntryDto[]): BaselineRow[] {
    return entries.map(entry => ({
        key: entry.key,
        values: Object.entries(entry.translations ?? {})
            .map(([language, value]) => ({ language: language.toUpperCase(), value })),
    }));
}

/**
 * The keys whose row is identical to the baseline across every displayed language.
 *
 * Only the displayed languages are compared: a difference in a column the user cannot see is not a
 * difference they can act on, and counting it would keep rows on screen for no visible reason.
 */
export function findInheritedKeys(
    rows: LocRow[], baseline: BaselineRow[], languages: string[],
): Set<string> {
    const inherited = new Set<string>();
    if (baseline.length === 0) { return inherited; }

    const byKey = new Map(baseline.map(row => [row.key, row]));

    for (const row of rows) {
        const base = byKey.get(row.key);
        if (base === undefined) { continue; }
        // An absent cell and an empty one are the same thing to the reader, so they compare equal.
        if (languages.every(language => valueOf(row.values, language) === valueOf(base.values, language))) {
            inherited.add(row.key);
        }
    }

    return inherited;
}

/** Drops rows that only repeat an inherited value, unless the user has asked to see them. */
export function hideInherited(rows: LocRow[], inherited: Set<string>, show: boolean): LocRow[] {
    if (show || inherited.size === 0) { return rows; }
    return rows.filter(row => !inherited.has(row.key));
}

/**
 * The baseline's values for a key, ready to stage as a reset. Null when the baseline has no such
 * key - there is nothing to revert to, and the reset must not be offered.
 */
export function baselineValuesFor(
    key: string, baseline: BaselineRow[], languages: string[],
): LocValue[] | null {
    const base = baseline.find(row => row.key === key);
    if (base === undefined) { return null; }
    return languages.map(language => ({ language, value: valueOf(base.values, language) }));
}

function valueOf(values: LocValue[], language: string): string {
    return values.find(v => v.language === language)?.value ?? '';
}
