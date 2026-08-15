// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a flyout opened from a tree row goes.
//
// The row it describes is in a dock on the right, inside a scroller, so the panel cannot simply be
// a child of the row - it would be clipped by the list. It is placed against the window instead,
// which makes "which row is this about" a question of geometry: it lines up with the row's own top
// edge and sits just clear of the dock, so the two read as one thing.

/** The gap between the flyout and the dock edge it hangs off. */
export const GAP = 6;

/** How close to the window edge the flyout is allowed to get. */
export const MARGIN = 8;

export interface AnchorRequest {
    /** The row's box, in window coordinates. */
    row: { top: number; bottom: number };

    /** The left edge of the dock the row lives in - the flyout stops short of it. */
    dockLeft: number;

    window: { width: number; height: number };

    /** How big the flyout itself is. */
    size: { width: number; height: number };
}

/** Top-left corner for the flyout, in window coordinates. */
export function anchorFlyout(request: AnchorRequest): { left: number; top: number } {
    const { row, dockLeft, window: win, size } = request;

    // Clamped bottom FIRST, then top, so the top edge wins when the two disagree - which is what
    // happens when the flyout is taller than the window. Losing the foot of a long table costs a
    // scroll; losing the head costs the title and the close button.
    const lowest = win.height - size.height - MARGIN;
    const top = Math.max(MARGIN, Math.min(row.top, lowest));

    // Flush against the window edge rather than half off it, when the dock leaves no room.
    const left = Math.max(MARGIN, dockLeft - size.width - GAP);

    return { left, top };
}
