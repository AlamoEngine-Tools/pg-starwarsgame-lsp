// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model tree's search element: whether the tree is narrowed, and how its header says so.
//
// The box is closed until it is wanted, and closing it does not clear it: a pattern stays in force
// behind a closed plate. That brings its own hazard, and `treeIsFiltered` and `treeFilterSummary`
// both exist to answer it: a filter you cannot see is a filter nobody can undo, so the plate's
// filter icon turns into the reset and the header counts what is left.

/**
 * How many rows a tree holds, counting the ones a fold is currently hiding.
 *
 * NOT the rendered row count. `visibleTreeRows` drops the children of a collapsed branch, and
 * `defaultCollapsed` folds the scaffolding on open - 50 of the Star Destroyer's 74 rows are bones
 * carrying nothing. Measuring the filter against what is on screen therefore reported a fresh,
 * unfiltered model as "50 of 58", which is the panel accusing itself of hiding things.
 */
export function countTreeNodes(
    roots: readonly { children: readonly unknown[] }[],
): number {
    return roots.reduce(
        (n, node) =>
            n +
            1 +
            countTreeNodes(
                node.children as readonly { children: readonly unknown[] }[],
            ),
        0,
    );
}

/** The three kinds a tree row can be, so "all of them" has a number rather than a magic 3. */
export const TREE_KIND_COUNT = 3;

/**
 * Whether anything narrows the tree: a pattern, or a kind switched off.
 *
 * A switched-off kind counts for as much as a pattern. It hides rows just the same, and it is what
 * the plate's reset puts back.
 */
export function treeIsFiltered(pattern: string, kindsOn: number): boolean {
    // Length, not `trim()`: the reset has to be there to take a stray space out of the box.
    return pattern.length > 0 || kindsOn < TREE_KIND_COUNT;
}

/**
 * What the tree's header says when it is showing less than the whole model, or null when it is not.
 *
 * The count alone cannot carry this. "40" over a Star Destroyer reads as a small model, and the two
 * ways to get there - a pattern, or a kind switched off - are both invisible once the search
 * element is collapsed.
 */
export function treeFilterSummary(
    shown: number,
    total: number,
    kindsOn: number,
): string | null {
    const narrowed = shown < total || kindsOn < TREE_KIND_COUNT;

    return narrowed && shown !== total ? `${shown} of ${total}` : null;
}

/**
 * One tree row, in pixels. Measured on a rendered panel: pitch is 24 for every row at every depth.
 */
const ROW_HEIGHT_PX = 24;

/**
 * How tall the tree list should be held, as a CSS length.
 *
 * Filtering must not resize the tree. A pattern that matches nothing - or every kind switched off -
 * emptied the list, and the skeleton control and the effect groups underneath jumped up the panel
 * to fill the gap, so the reader is left reaching for a control that moved while they typed.
 *
 * Takes the count of rows the tree shows with NO filter applied, which is the whole guarantee: this
 * cannot see the filtered count, so it cannot move when the filter does. Collapsing a branch still
 * resizes it, and should - that is the reader asking for less tree.
 *
 * A CSS `min()` rather than a clamped number, because a `min-height` larger than a `max-height`
 * WINS in CSS: a Star Destroyer's 133 rows would otherwise ask for 3192px and run off the panel.
 */
export function treeMinHeight(unfilteredRows: number): string {
    return `min(${unfilteredRows * ROW_HEIGHT_PX}px, 48vh)`;
}
