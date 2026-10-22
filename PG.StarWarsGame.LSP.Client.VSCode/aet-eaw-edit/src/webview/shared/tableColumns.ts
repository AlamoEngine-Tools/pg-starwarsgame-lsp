// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A table's columns as the reader arranged them: their order, dragged in the header, and the
// widths they were dragged to. Kept per table in the panel size store every editor keeps its sizes
// in - a width as itself, an order as each column's place counted from one - so it follows the
// reader rather than the project, as every other size does. Pure, so it is testable without a DOM.

import { readPanelSize, writePanelSize } from "./panelLayout";

/** How one table's columns are arranged: their keys in order, and the widths set by hand. */
export interface ColumnLayout {
    order: string[];
    widths: Record<string, number>;
}

/**
 * The columns in the reader's order: the stored order, then any column it does not name - one the
 * table gained since - where the table puts it. A stored column the table no longer has is dropped.
 */
export function orderedKeys(
    keys: readonly string[],
    order: readonly string[],
): string[] {
    const known = order.filter((key) => keys.includes(key));
    return [...known, ...keys.filter((key) => !known.includes(key))];
}

/** The order after a column is dragged onto another: it takes that column's place. */
export function moveColumn(
    order: readonly string[],
    from: string,
    to: string,
): string[] {
    if (from === to || !order.includes(from) || !order.includes(to)) {
        return [...order];
    }
    const rest = order.filter((key) => key !== from);
    const at =
        rest.indexOf(to) + (order.indexOf(from) < order.indexOf(to) ? 1 : 0);
    return [...rest.slice(0, at), from, ...rest.slice(at)];
}

const widthKey = (table: string, column: string): string =>
    `${table}.${column}.width`;
const placeKey = (table: string, column: string): string =>
    `${table}.${column}.order`;

/** A table's stored arrangement, for the columns it has. */
export function readColumnLayout(
    table: string,
    keys: readonly string[],
): ColumnLayout {
    const placed = keys
        .map((key) => ({ key, place: readPanelSize(placeKey(table, key), 0) }))
        .filter((column) => column.place > 0)
        .sort((a, b) => a.place - b.place)
        .map((column) => column.key);
    const widths: Record<string, number> = {};
    for (const key of keys) {
        const width = readPanelSize(widthKey(table, key), 0);
        if (width > 0) {
            widths[key] = width;
        }
    }
    return { order: orderedKeys(keys, placed), widths };
}

export function writeColumnWidth(
    table: string,
    column: string,
    width: number,
): void {
    writePanelSize(widthKey(table, column), width);
}

export function writeColumnOrder(
    table: string,
    order: readonly string[],
): void {
    order.forEach((key, index) =>
        writePanelSize(placeKey(table, key), index + 1),
    );
}
