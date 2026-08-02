// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The grid's three-mode filter, lifted out of the old sidebar webview so it can be unit-tested.
// Match semantics are deliberately unchanged: users have this in their fingers.

export type FilterMode = 'text' | 'wildcard' | 'regex';

/** 'all', 'key', or a language identifier. */
export type FilterScope = string;

export interface FilterValue { language: string; value: string; }
export interface FilterRow { key: string; values: FilterValue[]; }

/** A pattern compiled once, plus why it could not be compiled. */
export interface CompiledMatcher {
    test: (text: string) => boolean;
    /** The engine's message for a regex that would not compile; undefined when the pattern is fine. */
    error?: string;
}

/** A whole-row predicate compiled once, carrying any pattern error for the filter box to show. */
export interface CompiledRowFilter {
    test: (row: FilterRow) => boolean;
    error?: string;
}

/**
 * Compiles a pattern into a predicate, reporting a regex that would not compile.
 *
 * An invalid regex yields a matcher that matches nothing rather than throwing: the box is half-typed
 * most of the time it is being used, and an exception would take the webview down. It reports the
 * reason as well, because "matches nothing" and "you have a typo" look identical on screen - an
 * empty grid - and the user has no other way to tell them apart.
 */
export function compileMatcher(pattern: string, mode: FilterMode): CompiledMatcher {
    if (!pattern) { return { test: () => true }; }

    if (mode === 'regex') {
        try {
            const re = new RegExp(pattern, 'i');
            return { test: text => re.test(text) };
        } catch (e) {
            return { test: () => false, error: e instanceof Error ? e.message : String(e) };
        }
    }

    if (mode === 'wildcard') {
        // Metacharacters are escaped first so only * and ? are special - otherwise a '.' in a key
        // would match any character and quietly widen the filter. Nothing here can fail to compile.
        const escaped = pattern
            .replace(/[.+^${}()|[\]\\]/g, '\\$&')
            .replace(/\*/g, '.*')
            .replace(/\?/g, '.');
        const re = new RegExp(escaped, 'i');
        return { test: text => re.test(text) };
    }

    const lower = pattern.toLowerCase();
    return { test: text => text.toLowerCase().includes(lower) };
}

/**
 * Compiles the whole row test once, for reuse across every row.
 *
 * The row test is the same for every row, so building it per row was waste - though measured waste:
 * about a millisecond across 19,000 rows, since V8 caches the compilation of a repeated pattern.
 * Worth doing, but it was never the cause of a stall.
 */
export function buildRowFilter(
    pattern: string, mode: FilterMode, scope: FilterScope
): CompiledRowFilter {
    const matcher = compileMatcher(pattern, mode);
    const match = matcher.test;

    if (scope === 'key') {
        return { test: row => match(row.key), error: matcher.error };
    }

    if (scope !== 'all') {
        return {
            test: row => match(row.values.find(v => v.language === scope)?.value ?? ''),
            error: matcher.error,
        };
    }

    return {
        test: row => match(row.key) || row.values.some(v => match(v.value)),
        error: matcher.error,
    };
}
