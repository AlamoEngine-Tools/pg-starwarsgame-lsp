// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { placeTooltip, TIP_GAP, TIP_MARGIN } from './tooltipPlace';

const place = (over: Partial<Parameters<typeof placeTooltip>[0]> = {}) => placeTooltip({
    anchor: { left: 500, right: 515, top: 400, bottom: 415 },
    size: { width: 240, height: 60 },
    window: { width: 1200, height: 800 },
    ...over,
});

describe('placeTooltip', () => {
    it('centres on the thing it describes', () => {
        // 507.5 is the middle of the anchor; the bubble is 240 wide.
        assert.equal(place().left, 507.5 - 120);
    });

    it('sits above it by default, clear of it', () => {
        const at = place();

        assert.equal(at.top, 400 - 60 - TIP_GAP);
        assert.equal(at.side, 'above');
    });

    it('flips below when there is no room above', () => {
        const at = place({ anchor: { left: 500, right: 515, top: 10, bottom: 25 } });

        assert.equal(at.top, 25 + TIP_GAP);
        assert.equal(at.side, 'below');
    });

    it('stays inside the left edge', () => {
        assert.equal(place({ anchor: { left: 4, right: 19, top: 400, bottom: 415 } }).left,
            TIP_MARGIN);
    });

    it('stays inside the right edge', () => {
        const at = place({ anchor: { left: 1180, right: 1195, top: 400, bottom: 415 } });

        assert.equal(at.left, 1200 - 240 - TIP_MARGIN);
    });

    it('reports where the arrow has to point, in the bubble s own coordinates', () => {
        // Clamped against an edge the bubble is no longer centred on the badge, so an arrow drawn
        // at its middle would point at nothing.
        const at = place({ anchor: { left: 4, right: 19, top: 400, bottom: 415 } });

        assert.equal(at.arrowLeft, 11.5 - TIP_MARGIN);
    });

    it('keeps the arrow on the bubble even when the anchor is nowhere near it', () => {
        // Cannot happen with a badge inside the window, but a stale rect from a scrolled-away
        // control can - and an arrow drawn outside its own bubble looks like a rendering fault.
        const at = place({ anchor: { left: -400, right: -385, top: 400, bottom: 415 } });

        assert.ok(at.arrowLeft >= 0 && at.arrowLeft <= 240);
    });
});
