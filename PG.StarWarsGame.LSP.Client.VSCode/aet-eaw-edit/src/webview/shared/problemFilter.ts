// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Narrowing a problems table to what its editor is actually showing.
//
// The story graph raised it: the graph is filtered SERVER-side, so a filtered-out event is simply
// not in the response - but the diagnostics are computed for the whole campaign, so the table went
// on reporting findings about nodes that were not on screen. Filtering them on the server is the
// wrong fix twice over: it would make validation depend on view state, and the server has no
// business knowing what a client is rendering.
//
// Shared rather than written into the story graph, because every problems table has the same
// question eventually. Each editor supplies its own PREDICATE - what "in this view" means differs
// completely between a graph, a grid and a 3D scene - and an editor that has no answer yet passes
// none and is unaffected.

/** What "belongs to the current view" means. Supplied by the editor; there is no general answer. */
export type ProblemPredicate<T> = (problem: T) => boolean;

/** A problems table's contents, narrowed. */
export interface FilteredProblems<T> {
    /** The rows to render. Everything, when showing all or when there is no predicate. */
    shown: T[];
    /** How many the predicate excludes, whether or not they are being shown. */
    hidden: number;
    total: number;
    /**
     * Whether the filter has anything to act on.
     *
     * What a panel DISABLES its control on rather than hiding it: a filter holding nothing back is
     * a control with nothing to do, and a control that vanishes is one nobody learns exists.
     */
    filterable: boolean;
    /**
     * The count for the heading: <c>3 of 12</c> while something is held back, else <c>3</c>.
     *
     * Always both numbers when the filter bites, in either state. A bare count reads as the total,
     * and that is precisely how someone concludes a graph is clean while a filter holds an error
     * out of sight.
     */
    label: string;
}

/**
 * Narrows a problems table to its view.
 *
 * @param predicate What belongs to the view, or undefined for an editor that does not filter.
 * @param showAll The reader has asked to see the excluded ones too. They are still COUNTED as
 *     hidden, so the heading keeps saying the filter is there and can be turned off again.
 */
export function filterProblems<T>(
    problems: readonly T[],
    predicate: ProblemPredicate<T> | undefined,
    showAll: boolean,
): FilteredProblems<T> {
    const total = problems.length;

    if (predicate === undefined) {
        return {
            shown: [...problems],
            hidden: 0,
            total,
            filterable: false,
            label: String(total),
        };
    }

    const matching = problems.filter(predicate);
    const hidden = total - matching.length;

    return {
        shown: showAll ? [...problems] : matching,
        hidden,
        total,
        filterable: hidden > 0,
        label: hidden > 0 ? `${showAll ? total : matching.length} of ${total}` : String(total),
    };
}
