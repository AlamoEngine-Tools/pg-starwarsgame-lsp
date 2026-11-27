// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a stage flyout goes, given the pill that opens it.
//
// A flyout used to be told a corner of the stage - left, middle or right - and hung there at a fixed
// distance from the edge, whichever pill had opened it. That holds for as long as the pill happens
// to be in that corner: the abilities flyout sat 55px off its pill and 10px into the command bar it
// was opened from, and the weapon settings and the damage log, two pills side by side, were both
// "right" and nothing more.
//
// Everything here is in the STAGE's own coordinates, because the flyout is a child of the stage and
// is placed inside it.

/**
 * Between the flyout and the plate its pill sits on. The flyout's tail stands in this gap and
 * points at the pill, so the gap is as deep as the tail is long plus a pixel of air.
 */
export const FLYOUT_GAP = 8;

/** How close to the stage's edge the flyout is allowed to get. */
export const FLYOUT_MARGIN = 8;

/** How close to a corner of the flyout its tail may stand. A tail on the rounding points at nothing. */
export const FLYOUT_TAIL_INSET = 12;

export interface StageFlyoutRequest {
    /** The pill's box. */
    pill: { left: number; right: number; top: number; bottom: number };

    /**
     * The top and bottom of the plate the pill sits on. The flyout clears the plate rather than the
     * pill: a plate can be taller than its buttons, and a flyout that clears only the button lands
     * on the rest of it.
     */
    plate: { top: number; bottom: number };

    /** The flyout's own width. Its height is not asked for - the room decides that. */
    width: number;

    stage: { width: number; height: number };
}

export interface StageFlyoutPlacement {
    left: number;

    /**
     * Which way it opens: below a pill on the top edge, above one on the bottom edge. A flyout that
     * appears at the top of the stage for a pill at the bottom reads as a different panel opening.
     */
    side: "below" | "above";

    /** From the stage's top edge when it opens below, from its bottom edge when it opens above. */
    offset: number;

    /** The room it has, the margin kept. The box is capped at this and its body scrolls. */
    maxHeight: number;

    /**
     * Where the tail stands along the edge that faces the pill, from the flyout's own left edge to
     * the tail's middle. It is over the middle of the pill, also when the stage's edge has stopped
     * the flyout and the two are no longer centred - which is when it matters: two pills side by
     * side in a corner open two flyouts in one place, and the tail says which is whose.
     */
    tail: number;
}

export function placeStageFlyout(
    request: StageFlyoutRequest,
): StageFlyoutPlacement {
    const { pill, plate, width, stage } = request;

    const side =
        (pill.top + pill.bottom) / 2 <= stage.height / 2 ? "below" : "above";
    const offset =
        side === "below"
            ? plate.bottom + FLYOUT_GAP
            : stage.height - plate.top + FLYOUT_GAP;

    // Centred on the pill, then kept on the stage. The right edge is clamped FIRST, so the left one
    // wins on a stage narrower than the flyout: losing the right of a panel costs a scroll, losing
    // the left costs the start of every label.
    const middle = (pill.left + pill.right) / 2;
    const left = Math.max(
        FLYOUT_MARGIN,
        Math.min(middle - width / 2, stage.width - width - FLYOUT_MARGIN),
    );

    return {
        left,
        side,
        offset,
        maxHeight: Math.max(0, stage.height - offset - FLYOUT_MARGIN),
        tail: Math.max(
            FLYOUT_TAIL_INSET,
            Math.min(middle - left, width - FLYOUT_TAIL_INSET),
        ),
    };
}
