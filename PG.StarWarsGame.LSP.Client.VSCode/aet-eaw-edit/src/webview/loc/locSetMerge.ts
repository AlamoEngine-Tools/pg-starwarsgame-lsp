// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Presenting a set of single-language files as one table, and taking edits back apart again.
//
// NLS and DAT hold one language per file, so a project translated into six languages is six files.
// Editing them one at a time means never seeing a translation next to its source, which is the one
// view that makes translating possible. The grid therefore opens the whole set as one table: each
// file contributes its language as a column, and an edit to a cell goes back to the file that owns
// that language.
//
// CSV and XML need none of this - they already carry every language in one file - so a set is only
// ever built from the single-language formats.

/** One member of a set: a file, and the languages it holds. */
export interface MemberDocument {
    filePath: string;
    languages: string[];
    rows: { index: number; key: string; values: { language: string; value: string }[] }[];
}

export interface MergedDocument {
    languages: string[];
    rows: { index: number; key: string; values: { language: string; value: string }[] }[];
}

/** A key-addressed command as the editors stage it. */
export interface KeyedCommand {
    kind: string;
    key?: string;
    newKey?: string;
    language?: string;
    value?: string;
    values?: { language: string; value: string }[];
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
export function mergeMembers(members: readonly MemberDocument[]): MergedDocument {
    const languages: string[] = [];
    for (const member of members) {
        for (const language of member.languages) {
            if (!languages.some(l => l.toUpperCase() === language.toUpperCase())) {
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
    if (members.length === 1) { return passThrough(members[0], languages); }

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
                if (!values.has(value.language)) { values.set(value.language, value.value); }
            }
        }
    }

    return {
        languages,
        rows: order.map((key, index) => ({
            index,
            key,
            values: languages.map(language => ({
                language,
                value: byKey.get(key)?.get(language) ?? '',
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
function passThrough(member: MemberDocument, languages: string[]): MergedDocument {
    return {
        languages,
        rows: member.rows.map(row => ({
            index: row.index,
            key: row.key,
            values: languages.map(language => ({
                language,
                value: row.values.find(
                    v => v.language.toUpperCase() === language.toUpperCase())?.value ?? '',
            })),
        })),
    };
}

/** The member holding a language, or undefined when no file in the set does. */
export function memberForLanguage(
    members: readonly MemberDocument[], language: string,
): MemberDocument | undefined {
    return members.find(
        member => member.languages.some(l => l.toUpperCase() === language.toUpperCase()));
}

/**
 * Splits staged edits into one batch per file.
 *
 * A value belongs to the one file that holds its language. Everything that changes the shape of the
 * table - adding, renaming or deleting a key - belongs to every file in the set, or the files would
 * drift apart into different key lists and stop being one table at all.
 *
 * A member with nothing to do is left out entirely, so an untouched language is not rewritten and
 * its content hash is not spent.
 */
export function partitionCommands(
    commands: readonly KeyedCommand[], members: readonly MemberDocument[],
): Map<string, KeyedCommand[]> {
    const batches = new Map<string, KeyedCommand[]>();
    const add = (filePath: string, command: KeyedCommand): void => {
        const existing = batches.get(filePath);
        if (existing) { existing.push(command); } else { batches.set(filePath, [command]); }
    };

    for (const command of commands) {
        if (command.kind === 'setValue') {
            const owner = command.language === undefined
                ? undefined
                : memberForLanguage(members, command.language);
            // A value for a language no file holds has nowhere to go. Dropped rather than guessed
            // at: writing it into an arbitrary file would put German text in the French file.
            if (owner !== undefined) { add(owner.filePath, command); }
            continue;
        }

        if (command.kind === 'addEntry') {
            // Each file gets only the values for the languages it holds; the rest of the row is
            // that file's business to leave blank.
            for (const member of members) {
                add(member.filePath, {
                    ...command,
                    values: (command.values ?? []).filter(v =>
                        member.languages.some(l => l.toUpperCase() === v.language.toUpperCase())),
                });
            }
            continue;
        }

        if (command.kind === 'renameKey' || command.kind === 'deleteEntry') {
            for (const member of members) { add(member.filePath, command); }
            continue;
        }

        // addLanguage has no meaning here: in a single-language format another language is another
        // file, which the navigator's "add language" action creates.
    }

    return batches;
}
