// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Keeping a context menu inside the window.
//
// A menu opened at the raw pointer position runs off the edge whenever it is opened near one -
// right-clicking the last row of a full table put half of it below the window, where none of its
// items could be reached.

export interface MenuPlacement {
    left: number;
    top: number;
    /** Set only when the menu is taller than the window and has to scroll within it. */
    maxHeight: number | null;
}

/** Kept clear of the window edge, so the menu does not sit flush against it. */
const MARGIN = 4;

/**
 * Where to draw a menu opened at `anchor`.
 *
 * Opens down and to the right, as menus do. If that would overflow, it flips to the other side of
 * the pointer rather than merely sliding, so the menu never covers the thing that was clicked. If
 * flipping still does not fit - a window smaller than the menu - it is clamped inside the window,
 * and given a scrollable height if it is too tall to fit at all.
 */
export function placeMenu(
    anchor: { x: number; y: number },
    size: { width: number; height: number },
    viewport: { width: number; height: number },
): MenuPlacement {
    return {
        left: axis(anchor.x, size.width, viewport.width),
        top: axis(anchor.y, size.height, viewport.height),
        maxHeight: size.height > viewport.height - MARGIN * 2
            ? viewport.height - MARGIN * 2
            : null,
    };
}

function axis(at: number, extent: number, available: number): number {
    // Fits going forward - the ordinary case.
    if (at + extent <= available - MARGIN) { return at; }

    // Flip to the other side of the pointer. The flipped menu ends at the pointer, so this only
    // helps when the pointer is itself inside the window - which is not a given, since the anchor
    // is whatever coordinate the caller passed.
    const flipped = at - extent;
    if (flipped >= MARGIN && at <= available - MARGIN) { return flipped; }

    // Fits neither way: clamp inside the window.
    return Math.max(MARGIN, available - extent - MARGIN);
}
