// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The order a table of rows is drawn in, as a header click cycles it: the translation grid's and
// every DataTable's. What a column sorts BY is the table's own.

/** A column's key: in the translation grid 'key' or a language identifier. */
export type SortColumn = string;
export type SortDirection = "asc" | "desc";

export interface SortState {
    column: SortColumn;
    direction: SortDirection;
}

/**
 * The sort a header click produces: ascending, then descending, then back to the order the rows
 * came in.
 *
 * The third state is not cycling back to ascending because the order the rows came in means
 * something - in a translation file it is the order on disk - and without it there would be no
 * way to return to it.
 */
export function nextSort(
    current: SortState | null,
    column: SortColumn,
): SortState | null {
    if (current === null || current.column !== column) {
        return { column, direction: "asc" };
    }
    if (current.direction === "asc") {
        return { column, direction: "desc" };
    }
    return null;
}
