// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the strip under the table says about what it is showing.

/**
 * Describes the grid's contents: how much is in the file, how much is being held back, and how many
 * things are holding it back.
 *
 * The last part matters most on a large file. "1,203 rows" on its own looks like a small file; the
 * same view described as 19,222 rows with 18,019 filtered out by 2 filters is the truth, and says
 * why the rest is missing without the user having to go looking for the control that hid it.
 */
export function gridFooterLabel(total: number, shown: number, activeFilters: number): string {
    const parts = [`${format(total)} ${total === 1 ? 'row' : 'rows'}`];

    const hidden = total - shown;
    if (hidden > 0) { parts.push(`${format(hidden)} filtered out`); }

    // Reported even when nothing is currently hidden: a filter that matches everything today will
    // start hiding rows the moment the file changes, and its being on is not otherwise obvious.
    if (activeFilters > 0) {
        parts.push(`${activeFilters} ${activeFilters === 1 ? 'filter' : 'filters'} active`);
    }

    return parts.join(', ');
}

function format(n: number): string {
    return n.toLocaleString('en-US');
}
