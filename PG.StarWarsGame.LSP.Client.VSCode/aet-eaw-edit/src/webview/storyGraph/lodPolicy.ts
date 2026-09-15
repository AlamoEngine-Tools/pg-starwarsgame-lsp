// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The two independent LOD questions.
//
// They used to be one latched decision taken at load: whether the stored layout happened to fit at
// a readable zoom set BOTH "limit what we mount" and "draw the overview". That conflated two
// unrelated things and produced two bugs at once - a first-ever open (no stored layout, so the test
// never ran) mounted an entire campaign, and a small graph could never show the overview however
// far it was zoomed out, just shrinking real nodes into a blur.
//
// They are separate questions with separate inputs:
//   - windowing is about COST      -> how many nodes exist
//   - the overview is about ZOOM   -> how far out the viewport is

/** Above this many nodes, mount only what is on screen rather than the whole graph. */
export const WINDOW_NODE_COUNT = 60;

/**
 * Whether to mount only the visible screenful instead of the entire graph.
 *
 * A function of node count alone, so a campaign gets the same treatment on its first open as on
 * every one after it.
 */
export function shouldWindow(nodeCount: number): boolean {
    return nodeCount > WINDOW_NODE_COUNT;
}

/**
 * Whether to draw the cheap overview instead of real nodes.
 *
 * Purely the current zoom, for every graph regardless of size. Below the detail threshold a real
 * node is an unreadable smudge, and drawing hundreds of them is the expensive way to render a
 * blur.
 */
export function shouldShowOverview(zoom: number, detailZoom: number): boolean {
    return zoom < detailZoom;
}

/**
 * Whether an auto-arrange must mount the rest of the model before elk runs.
 *
 * The third question, and it is answered by neither of the two above. elk lays out what rete has
 * MOUNTED, and the model rebuild that captures the result reads the editor too - so a layout run
 * against a partial mount both arranges a fragment and shrinks the model to it.
 *
 * A function of the mounted set alone. `windowed` is not the same question: it is about cost, while
 * unmounting also happens for zoom, on graphs of every size, whenever the overview is up.
 */
export function needsFullMountForLayout(mountedCount: number, modelCount: number): boolean {
    return mountedCount < modelCount;
}
