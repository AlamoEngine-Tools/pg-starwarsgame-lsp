// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a dragged or resized modal is allowed to end up.
//
// Kept free of React and the DOM so the rules can be tested directly - the pointer plumbing in
// LocModal is then only about turning events into these calls.

export interface Point { x: number; y: number }
export interface Size { width: number; height: number }
export interface Viewport { width: number; height: number }

/** Smallest usable dialog: below this the buttons start overlapping the body. */
export const MIN_MODAL_SIZE: Size = { width: 260, height: 160 };

/**
 * Keeps a moved dialog reachable.
 *
 * The title bar is the only way to move it back, so it must never leave the viewport - dragging it
 * off the top or past an edge would strand it with no way to recover short of closing the tab. A
 * dialog larger than the viewport is pinned to the top-left rather than pushed off-screen.
 */
export function clampPosition(position: Point, size: Size, viewport: Viewport): Point {
    return {
        x: Math.max(0, Math.min(position.x, viewport.width - size.width)),
        y: Math.max(0, Math.min(position.y, viewport.height - size.height)),
    };
}

/**
 * Keeps a resized dialog usable and on screen: never smaller than {@link MIN_MODAL_SIZE}, never
 * larger than what is left of the viewport from where it currently sits.
 */
export function clampSize(size: Size, origin: Point, viewport: Viewport): Size {
    return {
        width: Math.max(MIN_MODAL_SIZE.width, Math.min(size.width, viewport.width - origin.x)),
        height: Math.max(MIN_MODAL_SIZE.height, Math.min(size.height, viewport.height - origin.y)),
    };
}

/** Which edge or corner is being dragged. Named like the CSS cursors they map to. */
export type ResizeDirection = 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw';

export interface Rect extends Point, Size {}

/**
 * The rectangle a resize drag produces.
 *
 * Expressed by moving edges rather than by adjusting width and height, because that is what makes
 * the north and west handles behave: dragging the left edge moves the origin and changes the width
 * at once, and the right edge has to stay exactly where it was. Clamping each moved edge against
 * both the viewport and the opposite edge's minimum then falls out for free - a dialog dragged past
 * its own minimum stops growing in the other direction instead of walking across the screen.
 */
export function resizeRect(
    base: Rect, direction: ResizeDirection, dx: number, dy: number, viewport: Viewport,
): Rect {
    let left = base.x;
    let top = base.y;
    let right = base.x + base.width;
    let bottom = base.y + base.height;

    if (direction.includes('w')) {
        left = clamp(base.x + dx, 0, right - MIN_MODAL_SIZE.width);
    }
    if (direction.includes('e')) {
        right = clamp(right + dx, left + MIN_MODAL_SIZE.width, viewport.width);
    }
    if (direction.includes('n')) {
        top = clamp(base.y + dy, 0, bottom - MIN_MODAL_SIZE.height);
    }
    if (direction.includes('s')) {
        bottom = clamp(bottom + dy, top + MIN_MODAL_SIZE.height, viewport.height);
    }

    return { x: left, y: top, width: right - left, height: bottom - top };
}

function clamp(value: number, min: number, max: number): number {
    // max wins when the two cross, which happens for a dialog already larger than the viewport:
    // the minimum size has to give way, or the rectangle would invert.
    return Math.max(Math.min(value, max), Math.min(min, max));
}

/**
 * Where a dialog of this size sits when it has not been moved yet.
 *
 * Used to seed the drag: the dialog starts centred by layout, so the first move has to begin from
 * wherever that put it, or it would jump before it moved.
 */
export function centredPosition(size: Size, viewport: Viewport): Point {
    return clampPosition(
        {
            x: Math.round((viewport.width - size.width) / 2),
            y: Math.round((viewport.height - size.height) / 2),
        },
        size,
        viewport);
}
