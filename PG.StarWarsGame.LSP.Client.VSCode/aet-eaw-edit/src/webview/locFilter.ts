// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The grid's three-mode filter, lifted out of the old sidebar webview so it can be unit-tested.
// Behaviour is deliberately unchanged: users have this in their fingers.

export type FilterMode = 'text' | 'wildcard' | 'regex';

/** 'all', 'key', or a language identifier. */
export type FilterScope = string;

export interface FilterValue { language: string; value: string; }
export interface FilterRow { key: string; values: FilterValue[]; }

/**
 * Compiles a pattern into a predicate.
 *
 * An invalid regex yields a matcher that matches nothing rather than throwing: the box is
 * half-typed most of the time it is being used, and an exception would take the webview down.
 */
export function buildMatcher(pattern: string, mode: FilterMode): (text: string) => boolean {
    if (!pattern) { return () => true; }

    if (mode === 'regex') {
        try {
            const re = new RegExp(pattern, 'i');
            return text => re.test(text);
        } catch {
            return () => false;
        }
    }

    if (mode === 'wildcard') {
        // Metacharacters are escaped first so only * and ? are special - otherwise a '.' in a key
        // would match any character and quietly widen the filter.
        const escaped = pattern
            .replace(/[.+^${}()|[\]\\]/g, '\\$&')
            .replace(/\*/g, '.*')
            .replace(/\?/g, '.');
        const re = new RegExp(escaped, 'i');
        return text => re.test(text);
    }

    const lower = pattern.toLowerCase();
    return text => text.toLowerCase().includes(lower);
}

/** Whether a row survives the current filter. */
export function matchesFilter(
    row: FilterRow, pattern: string, mode: FilterMode, scope: FilterScope
): boolean {
    const match = buildMatcher(pattern, mode);

    if (scope === 'key') { return match(row.key); }

    if (scope !== 'all') {
        const value = row.values.find(v => v.language === scope)?.value ?? '';
        return match(value);
    }

    return match(row.key) || row.values.some(v => match(v.value));
}
