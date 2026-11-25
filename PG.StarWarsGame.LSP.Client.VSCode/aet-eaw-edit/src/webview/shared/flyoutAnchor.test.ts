// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { anchorFlyout, GAP, MARGIN, TAIL_INSET } from "./flyoutAnchor";

const place = (over: Partial<Parameters<typeof anchorFlyout>[0]> = {}) =>
    anchorFlyout({
        row: { top: 300, bottom: 320 },
        dockLeft: 900,
        window: { width: 1200, height: 800 },
        size: { width: 320, height: 400 },
        ...over,
    });

describe("anchorFlyout", () => {
    it("lines the flyout up with the row that opened it", () => {
        // "Clearly aligned with the selected mesh" is the whole point: a panel that opens somewhere
        // else makes the reader find the row again to know what they are reading about.
        assert.equal(place({ row: { top: 300, bottom: 340 } }).top, 300);
    });

    /**
     * A pill is shorter than a row. The tail cannot stand on the corner's rounding, so the flyout
     * starts a little above the pill and the tail still meets the pill's middle.
     */
    it("starts a little above a pill, so that its tail meets the pill's middle", () => {
        const at = place();

        // The pill is 300 to 320; its middle is 310, and the tail stands 12 down the edge.
        assert.equal(at.top, 310 - TAIL_INSET);
        assert.equal(at.top + at.tail, 310);
    });

    it("sits clear of the dock it was opened from", () => {
        assert.equal(place().left, 900 - 320 - GAP);
    });

    it("keeps a flyout that would run off the bottom inside the window", () => {
        const at = place({ row: { top: 700, bottom: 720 } });

        assert.equal(at.top, 800 - 400 - MARGIN);
    });

    it("keeps a flyout opened from the very first row below the top edge", () => {
        assert.equal(place({ row: { top: 2, bottom: 22 } }).top, MARGIN);
    });

    it("prefers the top edge when the flyout is taller than the window", () => {
        // Clamping to the bottom first would push the head - the title and the close button - off
        // the top of the screen, which is the one part that must stay reachable.
        const at = place({
            size: { width: 320, height: 900 },
            row: { top: 700, bottom: 720 },
        });

        assert.equal(at.top, MARGIN);
    });

    it("gives up the gap before it goes off the left edge", () => {
        // A narrow window with a wide dock: better flush against the edge than half off screen.
        const at = place({
            dockLeft: 300,
            window: { width: 400, height: 800 },
        });

        assert.equal(at.left, MARGIN);
    });

    /** The tail stands in the gap and points at the pill, so the gap is as deep as the tail. */
    it("leaves the gap its tail needs", () => {
        assert.equal(GAP, 8);
    });

    /** The tail joins the flyout to the row, so it is level with the row, not with the box. */
    it("puts its tail level with the middle of the row that opened it", () => {
        const at = place({ row: { top: 300, bottom: 340 } });

        assert.equal(at.tail, 20);
    });

    it("keeps the tail on the row when the window's foot has pushed the flyout up", () => {
        const at = place({ row: { top: 700, bottom: 720 } });

        // The flyout stopped at 392; the row's middle is still 710.
        assert.equal(at.tail, 710 - 392);
    });

    /** A tail on the rounded corner points at nothing and breaks the corner's line. */
    it("keeps the tail off the flyout's corners", () => {
        const above = place({ row: { top: 2, bottom: 12 } });
        const below = place({
            size: { width: 320, height: 100 },
            row: { top: 790, bottom: 800 },
        });

        assert.equal(above.tail, TAIL_INSET);
        assert.equal(below.tail, 100 - TAIL_INSET);
    });
});
