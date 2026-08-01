// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The three kinds of line a credits file is built from, and where a dragged one lands.
//
// A credits file is not free-form text: every row is one of these three, and the whole shipped
// `creditstext_english.dat` is nothing but them. Making them things you can pick up and place says
// that far better than a menu item and a dropdown do.

import { BLANK_SENTINEL_VALUE, CREDITS_DIRECTIVES } from './creditsCrawlModel';

export interface CreditsStep {
    id: string;
    label: string;
    /** What it is for, shown on the tile. */
    description: string;
    /** The formatting directive the row carries. */
    key: string;
    /** The value every language gets. Empty for a line you are about to type into. */
    value: string;
    /**
     * What this kind of line looks like in the file itself. Shown on the tile so the friendly name
     * and the raw token are never two unrelated vocabularies - a blank line is `CENTER` carrying
     * the sentinel, so it is the sentinel that identifies it, not its directive.
     */
    token: string;
    codicon: string;
}

/**
 * The plain-language name for each formatting directive.
 *
 * One table, used by both the step library and the Format column, so the two cannot drift into
 * calling the same thing by different names.
 */
const DIRECTIVE_LABELS: Record<string, string> = {
    HEADER: 'Label',
    CENTER: 'Name',
};

/** The plain-language name for a directive, or null if it is not one the engine defines. */
export function directiveLabel(key: string): string | null {
    return DIRECTIVE_LABELS[key.trim().toUpperCase()] ?? null;
}

/** How a directive reads in the Format column: the token first, since that is what is in the file. */
export function directiveOptionText(key: string): string {
    const label = directiveLabel(key);
    return label === null ? key : `${key} - ${label}`;
}

export const CREDITS_STEPS: CreditsStep[] = [
    {
        id: 'header',
        label: DIRECTIVE_LABELS[CREDITS_DIRECTIVES[1]],
        description: 'A role or section heading, set small above the name',
        key: CREDITS_DIRECTIVES[1],
        value: '',
        token: CREDITS_DIRECTIVES[1],
        codicon: 'symbol-key',
    },
    {
        id: 'center',
        label: DIRECTIVE_LABELS[CREDITS_DIRECTIVES[0]],
        description: 'The larger line beneath a label',
        key: CREDITS_DIRECTIVES[0],
        value: '',
        token: CREDITS_DIRECTIVES[0],
        codicon: 'symbol-text',
    },
    {
        id: 'blank',
        label: 'Blank line',
        description: 'The gap between one block and the next',
        key: CREDITS_DIRECTIVES[0],
        value: BLANK_SENTINEL_VALUE,
        token: BLANK_SENTINEL_VALUE,
        codicon: 'dash',
    },
];

export function stepById(id: string): CreditsStep | null {
    return CREDITS_STEPS.find(step => step.id === id) ?? null;
}

/** Where a row dragged to `pointerY` would land, and the boundary to draw the indicator on. */
export interface DropTarget {
    /** Position in the document to insert at. */
    index: number;
    /** Y of the boundary the row will be inserted on, for the indicator. */
    y: number;
}

export interface DropRow { index: number; top: number; bottom: number; }

/**
 * Resolves a pointer position to an insertion point.
 *
 * The position comes from the row the pointer is over - its own document index, not how far down
 * the visible list it is. With a filter on, the rows on screen are not consecutive, and counting
 * screen positions would drop the new row somewhere else entirely.
 */
export function dropTargetAt(
    pointerY: number, rows: DropRow[], totalRows: number,
): DropTarget {
    if (rows.length === 0) { return { index: totalRows, y: 0 }; }

    const first = rows[0];
    if (pointerY < first.top) { return { index: first.index, y: first.top }; }

    for (const row of rows) {
        if (pointerY > row.bottom) { continue; }
        // The half the pointer is in decides which side of the row it goes.
        return pointerY < (row.top + row.bottom) / 2
            ? { index: row.index, y: row.top }
            : { index: row.index + 1, y: row.bottom };
    }

    const last = rows[rows.length - 1];
    return { index: last.index + 1, y: last.bottom };
}
