// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Completing a new entry's key against what the layers below already define.
//
// A mod's text file holds a fraction of the keys the game does - the ones it changes. Overriding
// one means knowing its exact name, which until now meant going and reading the game's own file.
// The baseline is already loaded for the Inherited toggle, so this costs no extra server call.

import { LocValue } from './loc/locRow';
import { BaselineRow } from './translationInherited';

export interface BaselineSuggestion {
    key: string;
    /** The inherited text, ready to drop into the dialog for editing. */
    values: LocValue[];
}

/** Enough to choose from without turning a 19,000-key baseline into a scroll. */
const MAX_SUGGESTIONS = 20;

/**
 * Baseline keys matching what has been typed, minus the ones this file already defines.
 *
 * Prefix matches come first because that is usually what was meant; substring matches follow, for
 * when only the middle of a name is remembered. Nothing is suggested for an empty query - the
 * alternative is dumping the whole game into a dropdown the moment the dialog opens.
 */
export function suggestBaselineKeys(
    query: string,
    baseline: BaselineRow[],
    existingKeys: string[],
    languages: string[],
): BaselineSuggestion[] {
    const needle = query.trim().toLowerCase();
    if (needle.length === 0) { return []; }

    const taken = new Set(existingKeys.map(key => key.toLowerCase()));
    const prefix: BaselineRow[] = [];
    const substring: BaselineRow[] = [];

    for (const row of baseline) {
        const key = row.key.toLowerCase();
        if (taken.has(key)) { continue; }

        if (key.startsWith(needle)) { prefix.push(row); } else if (key.includes(needle)) {
            substring.push(row);
        }

        if (prefix.length >= MAX_SUGGESTIONS) { break; }
    }

    return [...prefix, ...substring]
        .slice(0, MAX_SUGGESTIONS)
        .map(row => ({
            key: row.key,
            // Every displayed language gets a cell, empty where the baseline has none, so the
            // dialog never shows a ragged row.
            values: languages.map(language => ({
                language,
                value: row.values.find(v => v.language === language)?.value ?? '',
            })),
        }));
}
