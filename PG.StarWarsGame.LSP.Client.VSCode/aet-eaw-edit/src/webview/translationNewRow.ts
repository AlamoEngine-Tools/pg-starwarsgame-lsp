// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Rules for adding a translation to a keyed text file, kept free of React so they are testable.
//
// A key is an identifier the game looks up, so it is worth getting right before the row exists
// rather than after: an empty or duplicated key is a row the engine will never read, and a blank
// row in the grid gives no hint that either is a problem.
//
// The rules mirror what the server checks when it validates a batch. They are deliberately not
// stricter: inventing a shape rule here would reject keys the engine accepts.

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
 * The key as the engine will see it when it addresses the entry.
 *
 * An entry is keyed by the CRC32 of its key encoded as ASCII, and .NET's ASCII encoder replaces
 * every character above 0x7F with '?'. So `TEST_A" + "Ä` and `TEST_Ö` both become `TEST_?`
 * and land on the same entry - a collision no comparison of the strings themselves could find.
 *
 * Folding rather than hashing: two keys collide exactly when their folded forms match, save for a
 * true CRC collision between different folded strings. That residue is vanishingly rare and the
 * server still catches it, which is a much better trade than keeping a second CRC implementation
 * here for the client and the server to drift apart on.
 */
export function foldToEngineKey(key: string): string {
    let folded = '';
    for (const character of normaliseKey(key)) {
        folded += character.codePointAt(0)! > 0x7f ? '?' : character;
    }
    return folded;
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

    // Compared on the folded form, and case-sensitively - which is what the engine does. A
    // case-insensitive compare here used to refuse a differently-cased key the engine reads
    // perfectly well, and at the same time waved through two keys that genuinely collide.
    const folded = foldToEngineKey(candidate);
    const clash = existingKeys.find(existing => foldToEngineKey(existing) === folded);

    if (clash === undefined) { return null; }

    return clash === candidate
        ? `'${clash}' is already in this file. Only one row per key is ever read.`
        : `'${clash}' is already in this file, and the game cannot tell it apart from `
          + `'${candidate}' - it reads keys as ASCII, so both become '${folded}'.`;
}

/**
 * The values to stage, including the languages left blank - a row has to carry a cell for every
 * column, or the grid would show a ragged row and later edits would have nothing to address.
 */
export function draftToCommandValues(draft: NewRowDraft): LocValue[] {
    return draft.values.map(value => ({ language: value.language, value: value.value }));
}
