// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Whether the story graph is filtered at all - what turns the filter plate's icon into the reset.

import type { GraphFilters } from "../../protocol/story";

/**
 * Whether any of the filters is set.
 *
 * Reads the protocol's shape, where every field is optional, rather than the webview's own state
 * type where all of them are required strings - the panel is entitled to send a subset. The mode a
 * pattern is read in is not a filter: it narrows nothing without a pattern, and the reset keeps it.
 */
export function anyFilterSet(filters: GraphFilters): boolean {
    return (
        (filters.nameFilter ?? "") !== "" ||
        (filters.branch ?? "") !== "" ||
        (filters.lifecycle ?? "") !== "" ||
        (filters.reachableFrom ?? "") !== "" ||
        (filters.plotState ?? "") !== ""
    );
}
