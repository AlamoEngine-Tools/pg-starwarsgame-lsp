// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which rows a selection box has to enclose.
//
// Selecting a bone in the tree means selecting what hangs off it - a box around the bone's own
// origin would be a dot in the middle of the thing the reader is pointing at. So a box spans the
// row AND its subtree, and a selection that already contains an ancestor does not get a second box
// nested inside the first.

/**
 * The selected rows that actually get a box.
 *
 * A row with a selected ancestor is dropped: one box around the arm says what two boxes - one
 * around the arm and one around the hand inside it - only make harder to read. Order is the
 * reader's, so the first box drawn is the first row they picked.
 */
export function boxRoots(
    selected: readonly string[], parents: ReadonlyMap<string, string | null>,
): string[] {
    const chosen = new Set(selected);

    return selected.filter(row => !hasSelectedAncestor(row, chosen, parents));
}

/** Every row one box covers: the row itself and everything beneath it. */
export function rowsInBox(root: string, parents: ReadonlyMap<string, string | null>): string[] {
    const covered = new Set<string>([root]);

    // Repeated sweeps rather than recursion down a child map, because the map runs the other way
    // and a model is shallow. A row is only ever added once, so this terminates even if the map
    // somehow loops - which recursion would not.
    let grew = true;
    while (grew) {
        grew = false;

        for (const [row, parent] of parents) {
            if (parent !== null && covered.has(parent) && !covered.has(row)) {
                covered.add(row);
                grew = true;
            }
        }
    }

    return [...covered];
}

/** Walks up from a row looking for a selected ancestor, without trusting the map to be a tree. */
function hasSelectedAncestor(
    row: string, selected: ReadonlySet<string>, parents: ReadonlyMap<string, string | null>,
): boolean {
    const seen = new Set<string>([row]);

    for (let at = parents.get(row) ?? null; at !== null; at = parents.get(at) ?? null) {
        if (selected.has(at)) {
            return true;
        }

        // A loop in the parent map would spin here forever, and a hung render loop is a far worse
        // failure than a box drawn around too much.
        if (seen.has(at)) {
            return false;
        }

        seen.add(at);
    }

    return false;
}
