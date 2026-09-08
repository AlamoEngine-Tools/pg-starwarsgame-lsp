// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The graph's "clear all filters" control, which is present whether or not there is anything to
// clear.
//
// It used to be rendered only while a filter was set, and it is the first child of
// `.overview-tools` - a column, centred vertically in `.overview-mid` beside the minimap. Adding a
// fifth button to a centred column moves the other four up by half a button and re-centres the
// minimap against them, so typing one character in the filter box shifted the tool stack and put a
// button the reader had never seen at the top left of the minimap.
//
// A control with nothing to act on is disabled and says why; it is never removed. Here that also
// makes the column a fixed height, which is the whole of the layout fix.

import type { GraphFilters } from '../../protocol/story';
import { IconButton } from '../shared/Button';

/**
 * Whether any of the four filters is set.
 *
 * Reads the protocol's shape, where every field is optional, rather than the webview's own state
 * type where all four are required strings - the panel is entitled to send a subset.
 */
export function anyFilterSet(filters: GraphFilters): boolean {
    return (filters.nameFilter ?? '') !== ''
        || (filters.branch ?? '') !== ''
        || (filters.lifecycle ?? '') !== ''
        || (filters.reachableFrom ?? '') !== '';
}

export interface ClearFiltersButtonProps {
    filters: GraphFilters;
    onClear: () => void;
}

export function ClearFiltersButton(
    { filters, onClear }: ClearFiltersButtonProps,
): React.JSX.Element {
    const active = anyFilterSet(filters);

    return (
        <IconButton
            icon="clearFilter"
            title="Clear all filters"
            disabled={!active}
            disabledReason="Clear all filters - no filters are set"
            onClick={onClear}
        />
    );
}
