// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which rows a selection box has to enclose.
//
// Selecting a bone in the tree means selecting what is attached to it - a box around the bone's own
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

/** What a selected hardpoint's box encloses. */
export type BoxTarget = { kind: 'part'; id: string } | { kind: 'bone' };

/**
 * Whether a hardpoint's box should outline its own model, or just mark its attach bone.
 *
 * A hardpoint is not a bone, and selecting one used to box the attach bone's SUBTREE - which is
 * wrong in both directions at once. Measured on the shipped data: the Executor's left turbolaser
 * boxed 227 x 63 x 2211 on a 5094-unit hull, because the subtree gathers the turret's meshes AND
 * seven empty fire-point bones strung down the ship, each one stretching the box to reach it. The
 * Nebulon-B's boxed 7 x 7 x 7 - the fallback marker for a row with no geometry at all - because its
 * attach bone carries none.
 *
 * So: outline the MODEL where there is one, and mark the BONE where there is not. 137 of foc's 355
 * hardpoints name no `Model_To_Attach`, and for those a small cube on the bone is the honest answer
 * rather than a box around nothing.
 *
 * A part that is named but has not loaded yet also takes the bone: geometry arrives one part at a
 * time, and boxing a part that is not in the scene draws nothing, which reads as a selection that
 * did not take.
 */
export function boxTargetFor(
    partId: string | null | undefined, loadedParts: ReadonlySet<string>,
): BoxTarget {
    return partId !== null && partId !== undefined && partId !== '' && loadedParts.has(partId)
        ? { kind: 'part', id: partId }
        : { kind: 'bone' };
}
