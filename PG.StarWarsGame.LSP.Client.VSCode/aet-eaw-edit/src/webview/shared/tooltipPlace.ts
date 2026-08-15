// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a tooltip bubble goes, given the thing it describes.
//
// The bubble is placed against the WINDOW rather than parented to its control, for the same reason
// the details flyout is: the controls it belongs to live inside panels that scroll, and a child of
// one is clipped by it. Against the window nothing can cut it off - but then nothing positions it
// either, so this is the part that does.

/** Between the bubble and the control it points at. */
export const TIP_GAP = 8;

/** How close to the window edge the bubble is allowed to get. */
export const TIP_MARGIN = 6;

export interface TooltipRequest {
    /** The control's box, in window coordinates. */
    anchor: { left: number; right: number; top: number; bottom: number };

    /** The bubble's own size, measured after it is rendered. */
    size: { width: number; height: number };

    window: { width: number; height: number };
}

export interface TooltipPlacement {
    left: number;
    top: number;

    /** Which way the bubble sits, so the arrow can be drawn on the correct edge. */
    side: 'above' | 'below';

    /** Where the arrow goes along the bubble's own width. */
    arrowLeft: number;
}

export function placeTooltip(request: TooltipRequest): TooltipPlacement {
    const { anchor, size, window: win } = request;
    const middle = (anchor.left + anchor.right) / 2;

    // Above by preference: a tooltip under a control covers the next control down, which is the one
    // the reader is most likely to want next.
    const fits = anchor.top - size.height - TIP_GAP >= TIP_MARGIN;
    const side = fits ? 'above' : 'below';

    const left = Math.max(
        TIP_MARGIN,
        Math.min(middle - size.width / 2, win.width - size.width - TIP_MARGIN));

    // In the BUBBLE's coordinates, and kept on it: clamped against an edge the bubble is no longer
    // centred on its control, and an arrow drawn at its middle would point at nothing.
    const arrowLeft = Math.max(0, Math.min(middle - left, size.width));

    return {
        left,
        top: fits ? anchor.top - size.height - TIP_GAP : anchor.bottom + TIP_GAP,
        side,
        arrowLeft,
    };
}
