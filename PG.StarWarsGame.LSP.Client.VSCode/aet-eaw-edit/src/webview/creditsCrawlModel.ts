// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Turns credits rows into the blocks the crawl preview renders. Pure, so the pacing rules are
// unit-testable without a DOM.
//
// The preview earns its place beyond being fun: row order and blank spacer rows are exactly what
// the ordered model exists to preserve, and this is the only place a user can see both at once.

import { LocRow } from './loc/locRow';

export interface CrawlBlock {
    kind: 'line' | 'gap';
    /**
     * 'header' for a label line - a role, or a section title - and 'line' for the centred line
     * beneath it. Taken from the row's key, which in a credits file is a formatting directive
     * rather than an identifier; that is why the format allows the same "key" hundreds of times.
     */
    style: 'header' | 'line';
    text: string;
    /** True when the chosen language had no value and the key is standing in for it. */
    untranslated: boolean;
}

/**
 * The engine's blank-line sentinel, written as the *value* of a row. It appears 182 times in the
 * shipped `creditstext_english.dat` and is never text - rendering it literally would print it
 * across the whole crawl.
 */
export const BLANK_SENTINEL_VALUE = '[TBL]';

const BLANK_SENTINEL = BLANK_SENTINEL_VALUE.toLowerCase();

/** The directive the engine uses for a label line - a role, or a section heading. */
const HEADER_DIRECTIVE = 'header';

/**
 * The formatting directives a credits row's key may carry. In a credits file the key is an
 * instruction to the renderer rather than an identifier, which is why the format repeats it: the
 * whole shipped file uses only these two.
 */
export const CREDITS_DIRECTIVES = ['CENTER', 'HEADER'] as const;

/** True when the value is the blank-line sentinel (tolerant of padding and casing). */
export function isBlankSentinel(value: string): boolean {
    return value.trim().toLowerCase() === BLANK_SENTINEL;
}

/**
 * Whether a row is a blank-line marker rather than content.
 *
 * Only meaningful in a credits file, which is why it lives here rather than anywhere the
 * translation editor can reach: `[TBL]` is an ordinary value in a text file, and drawing such a row
 * as a spacer hid its key and its translations behind a marker offering nothing but deletion.
 */
export function isSpacerRow(row: LocRow): boolean {
    return row.values.length > 0 && row.values.every(v => isBlankSentinel(v.value));
}

function isBlank(value: string): boolean {
    return value.trim().length === 0 || isBlankSentinel(value);
}

/**
 * Builds the crawl for one language.
 *
 * Blank rows become gaps, consecutive gaps collapse to one, and gaps at either end are dropped -
 * otherwise the crawl opens and closes on dead time. A row with no value in the chosen language
 * but a value in another shows its key, so a missing translation is visible rather than silently
 * absent; a row blank in every language is a spacer and stays a gap.
 */
export function buildCrawl(rows: LocRow[], language: string): CrawlBlock[] {
    const blocks: CrawlBlock[] = [];

    for (const row of rows) {
        const value = valueFor(row, language);
        const style = row.key.trim().toLowerCase() === HEADER_DIRECTIVE ? 'header' : 'line';

        if (!isBlank(value)) {
            blocks.push({ kind: 'line', style, text: value, untranslated: false });
            continue;
        }

        // Blank here but filled elsewhere: a translation gap, worth showing as the key - unless
        // this row is a spacer in every language, in which case there is nothing missing.
        if (hasAnyValue(row)) {
            blocks.push({ kind: 'line', style, text: row.key, untranslated: true });
            continue;
        }

        pushGap(blocks);
    }

    return trimGaps(blocks);
}

function valueFor(row: LocRow, language: string): string {
    return row.values.find(v => v.language === language)?.value ?? '';
}

/**
 * Whether the row carries real text in any language. A sentinel does not count: a spacer row is a
 * spacer in every language, so treating it as "translated elsewhere" would print its key.
 */
function hasAnyValue(row: LocRow): boolean {
    return row.values.some(v => !isBlank(v.value));
}

function pushGap(blocks: CrawlBlock[]): void {
    // One pause, however many blank rows produced it.
    if (blocks.length > 0 && blocks[blocks.length - 1].kind === 'gap') { return; }
    blocks.push({ kind: 'gap', style: 'line', text: '', untranslated: false });
}

function trimGaps(blocks: CrawlBlock[]): CrawlBlock[] {
    let start = 0;
    let end = blocks.length;
    while (start < end && blocks[start].kind === 'gap') { start++; }
    while (end > start && blocks[end - 1].kind === 'gap') { end--; }
    return blocks.slice(start, end);
}
