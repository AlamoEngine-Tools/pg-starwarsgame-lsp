// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    FLYOUT_GAP,
    FLYOUT_MARGIN,
    FLYOUT_TAIL_INSET,
    placeStageFlyout,
} from "./stageFlyoutPlace";

/** A pill on the top edge, in the middle of a stage with room on every side. */
const place = (over: Partial<Parameters<typeof placeStageFlyout>[0]> = {}) =>
    placeStageFlyout({
        pill: { left: 486, right: 514, top: 12, bottom: 40 },
        plate: { top: 8, bottom: 44 },
        width: 304,
        stage: { width: 1000, height: 600 },
        ...over,
    });

describe("placeStageFlyout", () => {
    it("centres on the pill that opens it", () => {
        // 500 is the middle of the pill; the flyout is 304 wide.
        assert.equal(place().left, 500 - 152);
    });

    it("opens below a pill on the top edge, clear of the pill's plate", () => {
        const at = place();

        assert.equal(at.side, "below");
        assert.equal(at.offset, 44 + FLYOUT_GAP);
    });

    /** A flyout at the top of the stage for a pill at the bottom reads as a different panel. */
    it("opens above a pill on the bottom edge, measured from the stage's foot", () => {
        const at = place({
            pill: { left: 486, right: 514, top: 560, bottom: 588 },
            plate: { top: 556, bottom: 592 },
        });

        assert.equal(at.side, "above");
        assert.equal(at.offset, 600 - 556 + FLYOUT_GAP);
    });

    it("stops at the stage's right edge for a pill in that corner", () => {
        const at = place({
            pill: { left: 960, right: 988, top: 12, bottom: 40 },
        });

        assert.equal(at.left, 1000 - 304 - FLYOUT_MARGIN);
    });

    it("stops at the stage's left edge for a pill in that corner", () => {
        const at = place({
            pill: { left: 12, right: 40, top: 12, bottom: 40 },
        });

        assert.equal(at.left, FLYOUT_MARGIN);
    });

    /** Two pills side by side open two flyouts side by side, not one on top of the other. */
    it("follows the pill, so two pills do not share one place", () => {
        const first = place({
            pill: { left: 400, right: 428, top: 12, bottom: 40 },
        });
        const second = place({
            pill: { left: 432, right: 460, top: 12, bottom: 40 },
        });

        assert.equal(second.left - first.left, 32);
    });

    /** Losing the right of a panel costs a scroll; losing the left costs the start of every label. */
    it("keeps its left edge on a stage narrower than itself", () => {
        const at = place({ stage: { width: 280, height: 600 } });

        assert.equal(at.left, FLYOUT_MARGIN);
    });

    it("is given the room between its pill's plate and the far edge, the margin kept", () => {
        assert.equal(
            place().maxHeight,
            600 - (44 + FLYOUT_GAP) - FLYOUT_MARGIN,
        );
        assert.equal(
            place({
                pill: { left: 486, right: 514, top: 560, bottom: 588 },
                plate: { top: 556, bottom: 592 },
            }).maxHeight,
            556 - FLYOUT_GAP - FLYOUT_MARGIN,
        );
    });

    it("never asks for a negative height on a stage too short to hold it", () => {
        const at = place({ stage: { width: 1000, height: 40 } });

        assert.equal(at.maxHeight, 0);
    });

    /** The tail is what joins the two: it needs the gap to stand in, or it lands on the plate. */
    it("leaves the gap a tail needs between the flyout and the pill's plate", () => {
        assert.equal(FLYOUT_GAP, 8);
    });

    it("points its tail at the middle of the pill", () => {
        // Centred on the pill, so the pill's middle is the flyout's own.
        assert.equal(place().tail, 152);
    });

    /**
     * Two pills side by side in a corner open two flyouts that both stop at the stage's edge. The
     * tail is then the only thing that says which pill a flyout belongs to.
     */
    it("keeps the tail on the pill when the stage's edge has stopped the flyout", () => {
        const at = place({
            pill: { left: 960, right: 988, top: 12, bottom: 40 },
        });

        // The flyout stopped at 688; the pill's middle is 974.
        assert.equal(at.tail, 974 - 688);
    });

    /** A tail on a rounded corner points at nothing and breaks the corner's line. */
    it("keeps the tail off the flyout's corners", () => {
        const farRight = place({
            pill: { left: 990, right: 1000, top: 12, bottom: 40 },
        });
        const farLeft = place({
            pill: { left: 0, right: 10, top: 12, bottom: 40 },
        });

        assert.equal(farRight.tail, 304 - FLYOUT_TAIL_INSET);
        assert.equal(farLeft.tail, FLYOUT_TAIL_INSET);
    });
});
