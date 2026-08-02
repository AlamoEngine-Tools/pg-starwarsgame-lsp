// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Rules for adding a translation to a keyed text file, kept free of React so they are testable.
//
// A key is an identifier the game looks up, so it is worth getting right before the row exists
// rather than after: an empty or duplicated key is a row the engine will never read, and a blank
// row in the grid gives no hint that either is a problem.
//
// The two rules mirror what the server checks when it validates a batch (blank keys, and duplicates
// compared case-insensitively). They are deliberately not stricter: inventing a shape rule here
// would reject keys the engine accepts.

import { LocValue } from './loc/locRow';

export interface NewRowDraft { key: string; values: LocValue[]; }

/** A draft with a field for every language the file declares. */
export function blankDraft(languages: string[]): NewRowDraft {
    return { key: '', values: languages.map(language => ({ language, value: '' })) };
}

/** The key as it will be stored. Surrounding whitespace is not part of an identifier. */
export function normaliseKey(key: string): string {
    return key.trim();
}

/**
 * Why the key cannot be used, or null when it can.
 *
 * Returns a message rather than a boolean because the dialog shows it: "already defined" and
 * "cannot be empty" call for different corrections.
 */
export function validateNewKey(key: string, existingKeys: string[]): string | null {
    const candidate = normaliseKey(key);

    if (candidate.length === 0) {
        return 'A key is required - it is how the game refers to this text.';
    }

    const clash = existingKeys.find(
        existing => existing.toLowerCase() === candidate.toLowerCase());

    if (clash !== undefined) {
        return `'${clash}' is already in this file. Only one row per key is ever read.`;
    }

    return null;
}

/**
 * The values to stage, including the languages left blank - a row has to carry a cell for every
 * column, or the grid would show a ragged row and later edits would have nothing to address.
 */
export function draftToCommandValues(draft: NewRowDraft): LocValue[] {
    return draft.values.map(value => ({ language: value.language, value: value.value }));
}
