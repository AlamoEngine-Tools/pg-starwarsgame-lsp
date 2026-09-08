// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { anchorFlyout, GAP, MARGIN } from './flyoutAnchor';

const place = (over: Partial<Parameters<typeof anchorFlyout>[0]> = {}) => anchorFlyout({
    row: { top: 300, bottom: 320 },
    dockLeft: 900,
    window: { width: 1200, height: 800 },
    size: { width: 320, height: 400 },
    ...over,
});

describe('anchorFlyout', () => {
    it('lines the flyout up with the row that opened it', () => {
        // "Clearly aligned with the selected mesh" is the whole point: a panel that opens somewhere
        // else makes the reader find the row again to know what they are reading about.
        assert.equal(place().top, 300);
    });

    it('sits clear of the dock it was opened from', () => {
        assert.equal(place().left, 900 - 320 - GAP);
    });

    it('keeps a flyout that would run off the bottom inside the window', () => {
        const at = place({ row: { top: 700, bottom: 720 } });

        assert.equal(at.top, 800 - 400 - MARGIN);
    });

    it('keeps a flyout opened from the very first row below the top edge', () => {
        assert.equal(place({ row: { top: 2, bottom: 22 } }).top, MARGIN);
    });

    it('prefers the top edge when the flyout is taller than the window', () => {
        // Clamping to the bottom first would push the head - the title and the close button - off
        // the top of the screen, which is the one part that must stay reachable.
        const at = place({ size: { width: 320, height: 900 }, row: { top: 700, bottom: 720 } });

        assert.equal(at.top, MARGIN);
    });

    it('gives up the gap before it goes off the left edge', () => {
        // A narrow window with a wide dock: better flush against the edge than half off screen.
        const at = place({ dockLeft: 300, window: { width: 400, height: 800 } });

        assert.equal(at.left, MARGIN);
    });
});
