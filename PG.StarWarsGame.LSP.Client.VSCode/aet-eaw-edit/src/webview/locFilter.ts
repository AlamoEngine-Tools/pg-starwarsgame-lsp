// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The grid's three-mode filter, lifted out of the old sidebar webview so it can be unit-tested.
// Match semantics are deliberately unchanged: users have this in their fingers.

import {
    compileMatcher,
    type CompiledMatcher,
    type SearchMode,
} from "./shared/searchPattern";

export { compileMatcher, type CompiledMatcher };

/** The three ways a pattern is read. The search plate's, which every search shares. */
export type FilterMode = SearchMode;

/** 'all', 'key', or a language identifier. */
export type FilterScope = string;

export interface FilterValue {
    language: string;
    value: string;
}

export interface FilterRow {
    key: string;
    values: FilterValue[];
}

/** A whole-row predicate compiled once, carrying any pattern error for the filter box to show. */
export interface CompiledRowFilter {
    test: (row: FilterRow) => boolean;
    error?: string;
}

/**
 * Compiles the whole row test once, for reuse across every row.
 *
 * The row test is the same for every row, so building it per row was waste - though measured waste:
 * about a millisecond across 19,000 rows, since V8 caches the compilation of a repeated pattern.
 * Worth doing, but it was never the cause of a stall.
 */
export function buildRowFilter(
    pattern: string,
    mode: FilterMode,
    scope: FilterScope,
): CompiledRowFilter {
    const matcher = compileMatcher(pattern, mode);
    const match = matcher.test;

    if (scope === "key") {
        return { test: (row) => match(row.key), error: matcher.error };
    }

    if (scope !== "all") {
        return {
            test: (row) =>
                match(
                    row.values.find((v) => v.language === scope)?.value ?? "",
                ),
            error: matcher.error,
        };
    }

    return {
        test: (row) => match(row.key) || row.values.some((v) => match(v.value)),
        error: matcher.error,
    };
}
