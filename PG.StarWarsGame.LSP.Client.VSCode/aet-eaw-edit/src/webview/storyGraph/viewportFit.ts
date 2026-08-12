// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Working out the zoom that fits the graph into the viewport.
//
// Extracted from the editor closure for one reason: the answer decides whether the graph mounts
// its nodes or hides them behind the LOD overview, and that decision was being made from
// `container.clientWidth/clientHeight` with no check that the container had been measured. An
// unlaid-out webview reports 0x0, the arithmetic produced k = 0, and 0 is below every detail
// threshold - so the graph quietly took the windowed branch, mounted nothing and rendered blank.
// A pure function can be asked about that case directly.

export interface Extent {
    width: number;
    height: number;
}

/** Fraction of the viewport a fitted graph fills, leaving a little air around it. */
export const FIT_MARGIN = 0.9;

/**
 * The zoom at which `content` fits inside `view`, or <see langword="null" /> when `view` has no
 * usable size yet.
 *
 * Null rather than a number, deliberately: "I do not know how big the viewport is" and "the graph
 * needs to be shrunk to nothing" are different answers, and collapsing them into `0` is what made
 * an unmeasured container look like an enormous graph. Callers must decide what to do when the
 * viewport is not ready - waiting, or falling back to mounting everything - instead of silently
 * acting on a fabricated zoom.
 *
 * Never returns more than 1: a graph smaller than the viewport is shown at its natural size rather
 * than magnified.
 */
export function fitZoom(view: Extent, content: Extent): number | null {
    if (!isUsable(view.width) || !isUsable(view.height)) { return null; }

    // A single node, or every node on one row, gives a zero-extent axis; one unit keeps the
    // division finite and lets the other axis (or the 1:1 clamp) decide.
    const contentWidth = Math.max(1, content.width);
    const contentHeight = Math.max(1, content.height);

    return Math.min(
        (view.height / contentHeight) * FIT_MARGIN,
        (view.width / contentWidth) * FIT_MARGIN,
        1);
}

function isUsable(size: number): boolean {
    return Number.isFinite(size) && size > 0;
}
