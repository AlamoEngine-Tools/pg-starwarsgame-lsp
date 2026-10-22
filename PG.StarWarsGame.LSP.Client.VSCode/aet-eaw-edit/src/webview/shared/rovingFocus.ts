// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where the keys move focus in a menu and in a table of rows, as the editor's own: a menu walks its
// enabled items and wraps, a table walks its rows and stops at the ends. Pure, so it is testable
// without a DOM; the components hold the elements.

/**
 * The menu item the key moves to, by index: the next or previous ENABLED item, wrapping, or the
 * first or last one. Undefined for any other key, or a menu with nothing enabled.
 */
export function menuStep(
    enabled: readonly boolean[],
    at: number,
    key: string,
): number | undefined {
    const indices = enabled.flatMap((on, i) => (on ? [i] : []));
    if (indices.length === 0) {
        return undefined;
    }
    switch (key) {
        case "Home":
            return indices[0];
        case "End":
            return indices[indices.length - 1];
        case "ArrowDown":
            return indices.find((i) => i > at) ?? indices[0];
        case "ArrowUp":
            return (
                [...indices].reverse().find((i) => at !== -1 && i < at) ??
                indices[indices.length - 1]
            );
        default:
            return undefined;
    }
}

/** One visible row of a tree, as `treeStep` needs it. */
export interface TreeStepRow {
    depth: number;
    /** It has rows below it. */
    expandable: boolean;
    expanded: boolean;
}

/** What a key does in a tree: move to a visible row, or fold the focused one open or shut. */
export type TreeStep = { to: number } | { expand: true } | { collapse: true };

/**
 * What the key does in a tree, as VS Code's own: up, down, a page, Home and End walk the visible
 * rows as a table's; right opens a closed row and then goes to its first child; left shuts an open
 * row and otherwise goes to its parent. Undefined for any other key, or where the key has nothing to
 * do.
 */
export function treeStep(
    rows: readonly TreeStepRow[],
    at: number,
    key: string,
    page: number,
): TreeStep | undefined {
    const row = rows[at];
    if (key === "ArrowRight") {
        if (!row?.expandable) {
            return undefined;
        }
        if (!row.expanded) {
            return { expand: true };
        }
        return rows[at + 1]?.depth > row.depth ? { to: at + 1 } : undefined;
    }
    if (key === "ArrowLeft") {
        if (row?.expandable && row.expanded) {
            return { collapse: true };
        }
        for (let i = at - 1; i >= 0 && row; i--) {
            if (rows[i].depth < row.depth) {
                return { to: i };
            }
        }
        return undefined;
    }
    const to = rowStep(rows.length, at, key, page);
    return to === undefined ? undefined : { to };
}

/**
 * The row the key moves to, by index: one row, a page of `page` rows, or the first or last,
 * clamped to the table. Undefined for any other key, or a table with no rows.
 */
export function rowStep(
    count: number,
    at: number,
    key: string,
    page: number,
): number | undefined {
    if (count === 0) {
        return undefined;
    }
    const to = (i: number): number => Math.max(0, Math.min(count - 1, i));
    switch (key) {
        case "ArrowDown":
            return to(at + 1);
        case "ArrowUp":
            return to(at - 1);
        case "PageDown":
            return to(at + page);
        case "PageUp":
            return to(at - page);
        case "Home":
            return 0;
        case "End":
            return count - 1;
        default:
            return undefined;
    }
}
