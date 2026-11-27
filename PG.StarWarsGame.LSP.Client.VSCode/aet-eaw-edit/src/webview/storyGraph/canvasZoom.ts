// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The story graph canvas's zoom from the dock's foot: its range, a press of zoom in or out, and the
// pan that keeps the middle of the view where it is.

/**
 * The slider's range. A large campaign fits the view at a few percent, and 200% is as close as a
 * node is worth reading; the wheel can still go past either end.
 */
export const CANVAS_ZOOM_MIN = 0.01;
export const CANVAS_ZOOM_MAX = 2;

/** One press of zoom in or out: a quarter, so a press each way comes back to where it started. */
const ZOOM_STEP = 1.25;

/** The zoom one press of zoom in (1) or zoom out (-1) gives, inside the range. */
export function stepZoom(zoom: number, direction: 1 | -1): number {
    const next = direction > 0 ? zoom * ZOOM_STEP : zoom / ZOOM_STEP;
    return Math.min(CANVAS_ZOOM_MAX, Math.max(CANVAS_ZOOM_MIN, next));
}

/**
 * The pan that keeps the graph point under the middle of the view where it is through a zoom - the
 * offset rete's area adds to its translation as it zooms. A control in the foot has no pointer to
 * zoom about, and zooming about the canvas's corner throws the reader's place off the screen.
 */
export function zoomAboutCentre(
    transform: { k: number; x: number; y: number },
    view: { width: number; height: number },
    next: number,
): { ox: number; oy: number } {
    const ratio = next / transform.k - 1;
    // `+ 0` folds a negative zero, so no zoom change is no pan at all.
    return {
        ox: (transform.x - view.width / 2) * ratio + 0,
        oy: (transform.y - view.height / 2) * ratio + 0,
    };
}
