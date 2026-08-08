// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { BLANK_SENTINEL_VALUE } from '../creditsCrawlModel';
import { LocRow } from './locRow';

/**
 * The columns hidden before the user has chosen anything.
 *
 * There are two such defaults and they must be computed in one place, because one of them is what
 * the grid renders and the other is what the first click on the column menu starts editing. They
 * used to be written out separately: the render honoured the focus language, the click seeded
 * itself from {@link emptyLanguages} alone. So on a set opened on one language, ticking a second
 * language did not add it - it started from "hide the empty ones", removed the ticked language
 * from that, and revealed every column at once.
 *
 * @param focusLanguage The single language to show, when the tab was opened on one language of a
 *     set. Null or undefined for the ordinary case, where the default is to hide empty columns.
 */
export function defaultHiddenLanguages(
    rows: LocRow[], languages: string[], focusLanguage: string | null | undefined,
): string[] {
    if (focusLanguage === null || focusLanguage === undefined) {
        return emptyLanguages(rows, languages);
    }

    // Everything but the focus - hidden, not absent, so the column menu brings any of them back.
    // That is what makes this a view of the set rather than a different document.
    return languages.filter(l => l.toUpperCase() !== focusLanguage.toUpperCase());
}

/**
 * The languages a file declares but says nothing in.
 *
 * Hidden by default: a project that declares six languages and has written two shows four columns
 * of nothing, which is most of the table's width spent on emptiness. They stay one click away in
 * the column menu, and a language with even a single value is never hidden - that one is the
 * language being worked on.
 */
export function emptyLanguages(rows: LocRow[], languages: string[]): string[] {
    // An empty file is not evidence about any language, and hiding on no evidence would open a new
    // file with most of its columns missing.
    if (rows.length === 0) { return []; }

    const marker = BLANK_SENTINEL_VALUE.toLowerCase();
    const empty = languages.filter(language => !rows.some(row => {
        const value = row.values.find(v => v.language.toUpperCase() === language.toUpperCase())?.value;
        // The blank-line marker is a spacer, not something written in that language.
        return value !== undefined
            && value.trim() !== ''
            && value.trim().toLowerCase() !== marker;
    }));

    // A table of keys with no values at all is not a view anyone asked for; the point is to get
    // emptiness out of the way, not the content.
    return empty.length === languages.length ? [] : empty;
}
