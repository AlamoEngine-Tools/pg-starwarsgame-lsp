// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    FLYOUT_GAP,
    FLYOUT_MARGIN,
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
});
