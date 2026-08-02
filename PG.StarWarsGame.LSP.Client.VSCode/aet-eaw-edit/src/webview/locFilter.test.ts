// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { buildMatcher, buildRowFilter, compileMatcher, FilterRow, matchesFilter } from './locFilter';

function row(key: string, values: Record<string, string>): FilterRow {
    return { key, values: Object.entries(values).map(([language, value]) => ({ language, value })) };
}

const sample = row('TEXT_XWING', { ENGLISH: 'X-Wing Fighter', GERMAN: 'X-Fluegel Jaeger' });

describe('buildMatcher', () => {
    it('matches everything when the pattern is empty', () => {
        assert.equal(buildMatcher('', 'text')('anything'), true);
    });

    it('matches a substring case-insensitively in text mode', () => {
        const match = buildMatcher('wing', 'text');

        assert.equal(match('X-Wing Fighter'), true);
        assert.equal(match('TIE Fighter'), false);
    });

    // Wildcards are what a modder reaches for on key prefixes: TEXT_UNIT_*.
    it('treats * as any run in wildcard mode', () => {
        assert.equal(buildMatcher('TEXT_*_NAME', 'wildcard')('TEXT_UNIT_NAME'), true);
        assert.equal(buildMatcher('TEXT_*_NAME', 'wildcard')('TEXT_UNIT_LABEL'), false);
    });

    // '?' stands for exactly one character - A?C wants something between the A and the C.
    it('treats ? as exactly one character in wildcard mode', () => {
        const match = buildMatcher('A?C', 'wildcard');

        assert.equal(match('ABC'), true);
        assert.equal(match('AC'), false);
    });

    // Patterns match anywhere in the text rather than having to span it, which is the behaviour
    // the sidebar shipped with and what makes plain text mode and wildcard mode consistent.
    it('matches a pattern anywhere in the text', () => {
        assert.equal(buildMatcher('WING', 'wildcard')('X-WING FIGHTER'), true);
        assert.equal(buildMatcher('TEXT_?', 'wildcard')('TEXT_AB'), true);
    });

    // Regex metacharacters in a wildcard pattern are literal, or a '.' in a key would match
    // anything and quietly widen the filter.
    it('escapes regex metacharacters in wildcard mode', () => {
        const match = buildMatcher('A.B', 'wildcard');

        assert.equal(match('A.B'), true);
        assert.equal(match('AXB'), false);
    });

    it('matches a regular expression in regex mode', () => {
        assert.equal(buildMatcher('^TEXT_.*NAME$', 'regex')('TEXT_UNIT_NAME'), true);
    });

    // A half-typed regex is the normal state of the box while typing. Matching nothing shows an
    // empty grid; throwing would take the whole webview down.
    it('matches nothing for an invalid regex rather than throwing', () => {
        const match = buildMatcher('([unclosed', 'regex');

        assert.equal(match('anything'), false);
    });
});

describe('compileMatcher', () => {
    // An invalid regex used to silently match nothing, so the grid emptied with no way to tell a
    // typo from a pattern that genuinely has no hits.
    it('reports why an invalid regex could not be compiled', () => {
        const compiled = compileMatcher('([unclosed', 'regex');

        assert.equal(typeof compiled.error, 'string');
        assert.notEqual(compiled.error, '');
    });

    it('reports no error for a valid pattern', () => {
        assert.equal(compileMatcher('^TEXT_', 'regex').error, undefined);
        assert.equal(compileMatcher('A*B', 'wildcard').error, undefined);
        assert.equal(compileMatcher('([unclosed', 'text').error, undefined);
    });

    // The behaviour is unchanged - an invalid pattern still matches nothing. Only now it says so.
    it('still matches nothing for an invalid regex', () => {
        assert.equal(compileMatcher('([unclosed', 'regex').test('anything'), false);
    });
});

describe('buildRowFilter', () => {
    // The reported hang: matchesFilter compiled a fresh RegExp for every row, so filtering a
    // 19k-row file recompiled the same pattern 19k times per keystroke. The compiled filter must
    // build its matcher once and reuse it across rows.
    it('compiles the pattern once however many rows it is applied to', () => {
        const originalRegExp = globalThis.RegExp;
        let constructed = 0;
        class CountingRegExp extends originalRegExp {
            constructor(pattern: string | RegExp, flags?: string) {
                super(pattern as string, flags);
                constructed++;
            }
        }
        (globalThis as { RegExp: unknown }).RegExp = CountingRegExp;
        try {
            const filter = buildRowFilter('TEXT_', 'regex', 'all');
            constructed = 0;
            for (let i = 0; i < 500; i++) { filter.test(sample); }
        } finally {
            (globalThis as { RegExp: unknown }).RegExp = originalRegExp;
        }

        assert.equal(constructed, 0);
    });

    it('matches the same rows matchesFilter does', () => {
        for (const [pattern, mode, scope] of [
            ['XWING', 'text', 'all'], ['Fighter', 'text', 'key'],
            ['Fluegel', 'text', 'GERMAN'], ['^TEXT_', 'regex', 'all'],
            ['TEXT_*', 'wildcard', 'key'], ['', 'text', 'all'],
        ] as const) {
            assert.equal(
                buildRowFilter(pattern, mode, scope).test(sample),
                matchesFilter(sample, pattern, mode, scope),
                `${pattern} / ${mode} / ${scope}`);
        }
    });

    it('carries the compile error so the box can show it', () => {
        assert.equal(typeof buildRowFilter('([unclosed', 'regex', 'all').error, 'string');
    });
});

describe('matchesFilter', () => {
    it('searches keys and every value under the all scope', () => {
        assert.equal(matchesFilter(sample, 'XWING', 'text', 'all'), true);
        assert.equal(matchesFilter(sample, 'Fluegel', 'text', 'all'), true);
        assert.equal(matchesFilter(sample, 'Falcon', 'text', 'all'), false);
    });

    it('ignores values under the key scope', () => {
        assert.equal(matchesFilter(sample, 'XWING', 'text', 'key'), true);
        assert.equal(matchesFilter(sample, 'Fighter', 'text', 'key'), false);
    });

    it('searches only the named language under a language scope', () => {
        assert.equal(matchesFilter(sample, 'Fluegel', 'text', 'GERMAN'), true);
        assert.equal(matchesFilter(sample, 'Fighter', 'text', 'GERMAN'), false);
    });

    // A row with no value for the scoped language must simply not match, not crash the render.
    it('handles a language the row has no value for', () => {
        assert.equal(matchesFilter(sample, 'anything', 'text', 'FRENCH'), false);
    });

    it('keeps every row when the pattern is empty', () => {
        assert.equal(matchesFilter(sample, '', 'text', 'all'), true);
    });
});
