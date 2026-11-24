// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Presenting a set of single-language files as one table.
//
// NLS and DAT hold one language per file, so a project translated into six languages is six files.
// Editing them one at a time means never seeing a translation next to its source, which is the one
// view that makes translating possible. The grid therefore opens the whole set as one table: each
// file contributes its language as a column. Which file an edit to a cell goes back to is the
// server's to decide: the grid names the set, and the server takes the batch apart.
//
// CSV and XML need none of this - they already carry every language in one file - so a set is only
// ever built from the single-language formats.

/** One member of a set: a file, and the languages it holds. */
export interface MemberDocument {
    filePath: string;
    languages: string[];
    rows: {
        index: number;
        key: string;
        values: { language: string; value: string }[];
    }[];
}

export interface MergedDocument {
    languages: string[];
    rows: {
        index: number;
        key: string;
        values: { language: string; value: string }[];
    }[];
}

/**
 * One table from several files.
 *
 * Keys are unioned in first-seen order rather than sorted: the first member's order is the order
 * the file is written in, and a translator reading down the column expects it. A key only some
 * languages have still gets a row, with blanks in the rest - that is precisely the gap worth seeing.
 *
 * Merging is keyed, so it is only ever right for an actual set. One file is passed straight
 * through - see {@link passThrough}.
 */
export function mergeMembers(
    members: readonly MemberDocument[],
): MergedDocument {
    const languages: string[] = [];
    for (const member of members) {
        for (const language of member.languages) {
            if (
                !languages.some(
                    (l) => l.toUpperCase() === language.toUpperCase(),
                )
            ) {
                languages.push(language);
            }
        }
    }

    // A single file is not a set, and merging one by key is destructive rather than merely
    // pointless. A credits file keys every row by a formatting directive repeated hundreds of
    // times, so keying folded the whole file down to one row per distinct directive - a file that
    // is all CENTER became a single line. It also re-indexed what survived, and credits edits
    // address a row by its position, so the rows that were left took writes aimed at other lines.
    // A translation file lost its duplicate keys the same way, hiding the very row the validator
    // reports.
    if (members.length === 1) {
        return passThrough(members[0], languages);
    }

    // key -> language -> value, built once so the row assembly below is not quadratic in members.
    const byKey = new Map<string, Map<string, string>>();
    const order: string[] = [];

    for (const member of members) {
        for (const row of member.rows) {
            let values = byKey.get(row.key);
            if (values === undefined) {
                values = new Map<string, string>();
                byKey.set(row.key, values);
                order.push(row.key);
            }

            for (const value of row.values) {
                // First member wins a language it shares with another, which only happens if a set
                // was built from files that overlap - the merge stays deterministic either way.
                if (!values.has(value.language)) {
                    values.set(value.language, value.value);
                }
            }
        }
    }

    return {
        languages,
        rows: order.map((key, index) => ({
            index,
            key,
            values: languages.map((language) => ({
                language,
                value: byKey.get(key)?.get(language) ?? "",
            })),
        })),
    };
}

/**
 * One file as its own table: every row, in its own order, at its own position.
 *
 * Only the language columns are normalised - each row gets one entry per declared language, filling
 * a blank where the file says nothing - because that is what the grid renders against. Nothing
 * about the row's identity is touched: `index` stays the row's position in the file, which is what
 * a credits edit addresses, and two rows sharing a key stay two rows.
 */
function passThrough(
    member: MemberDocument,
    languages: string[],
): MergedDocument {
    return {
        languages,
        rows: member.rows.map((row) => ({
            index: row.index,
            key: row.key,
            values: languages.map((language) => ({
                language,
                value:
                    row.values.find(
                        (v) =>
                            v.language.toUpperCase() === language.toUpperCase(),
                    )?.value ?? "",
            })),
        })),
    };
}
