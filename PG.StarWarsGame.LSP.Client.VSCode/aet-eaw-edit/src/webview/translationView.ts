// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The translation grid's view projection: in what order rows are drawn, and the stats that describe
// the file. Kept free of React so the rules are unit-testable.
//
// Nothing here is reachable from the credits editor, which is the point: sorting and duplicate-key
// counting are both meaningless in a file whose order is content and whose keys repeat by design.

import { LocRow } from './loc/locRow';

/** 'key', or a language identifier. */
export type SortColumn = string;
export type SortDirection = 'asc' | 'desc';
export interface SortState { column: SortColumn; direction: SortDirection; }

/**
 * Orders rows for display.
 *
 * View-only: the returned rows keep the `index` the grid identifies them by, and edits address
 * entries by key regardless, so sorting cannot move an edit onto the wrong entry. Null is the order
 * the file has on disk.
 */
export function sortRows(rows: LocRow[], sort: SortState | null): LocRow[] {
    if (sort === null) { return rows; }

    return [...rows].sort((a, b) => {
        const compared = compare(sortKeyOf(a, sort.column), sortKeyOf(b, sort.column));
        if (compared !== 0) { return sort.direction === 'asc' ? compared : -compared; }
        // Equal cells fall back to document order, in both directions: identity is the index, and
        // rows that look alike should not shuffle between renders.
        return a.index - b.index;
    });
}

/**
 * The sort a header click produces: ascending, then descending, then back to document order.
 *
 * The third state is not cycling back to ascending because document order is the order on disk,
 * and without it there would be no way to return to the view the file actually has.
 */
export function nextSort(current: SortState | null, column: SortColumn): SortState | null {
    if (current === null || current.column !== column) { return { column, direction: 'asc' }; }
    if (current.direction === 'asc') { return { column, direction: 'desc' }; }
    return null;
}

/**
 * How many rows repeat a key already used above them.
 *
 * Meaningful only in a text file, where a key is an identifier and a repeat is an error the batch
 * validator reports. A credits file keys every row by a formatting directive, so the same count
 * there is just the row total minus two.
 */
export function countDuplicateKeys(rows: LocRow[]): number {
    const seen = new Set<string>();
    let duplicates = 0;
    for (const row of rows) {
        if (seen.has(row.key)) { duplicates++; } else { seen.add(row.key); }
    }
    return duplicates;
}

/**
 * Empty cells therefore sort first ascending, which is deliberate and confirmed: sorting a language
 * column is how untranslated rows get found, so they belong at the top where the sort lands you.
 * Do not "fix" this by pushing blanks to the end.
 */
function sortKeyOf(row: LocRow, column: SortColumn): string {
    if (column === 'key') { return row.key; }
    // A row that does not carry the language sorts as empty rather than being dropped or thrown
    // to an arbitrary end - the same place an explicitly empty cell goes.
    return row.values.find(v => v.language === column)?.value ?? '';
}

function compare(a: string, b: string): number {
    // Numeric so TEXT_ITEM_2 precedes TEXT_ITEM_10, and base sensitivity so case and accents do not
    // split names that read as neighbours.
    return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
}
