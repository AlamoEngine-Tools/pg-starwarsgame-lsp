// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { placeMenu } from './menuPlacement';

const VIEWPORT = { width: 1000, height: 800 };
const SIZE = { width: 200, height: 300 };

describe('placeMenu', () => {
    it('opens at the pointer when there is room', () => {
        assert.deepEqual(
            placeMenu({ x: 100, y: 100 }, SIZE, VIEWPORT),
            { left: 100, top: 100, maxHeight: null });
    });

    /**
     * The case that prompted this: right-clicking the last row of a full table put the menu's
     * bottom half below the window, where it could not be reached at all.
     */
    it('flips above the pointer when it would run off the bottom', () => {
        const placement = placeMenu({ x: 100, y: 700 }, SIZE, VIEWPORT);

        assert.equal(placement.top, 400);
    });

    it('flips left of the pointer when it would run off the right', () => {
        const placement = placeMenu({ x: 900, y: 100 }, SIZE, VIEWPORT);

        assert.equal(placement.left, 700);
    });

    it('flips both ways at once in the bottom-right corner', () => {
        const placement = placeMenu({ x: 950, y: 780 }, SIZE, VIEWPORT);

        assert.equal(placement.left, 750);
        assert.equal(placement.top, 480);
    });

    // Flipping is the first choice, but on a small window it can put the menu off the *other* edge.
    it('never places the menu off the top or left edge', () => {
        const placement = placeMenu({ x: 20, y: 40 }, { width: 200, height: 300 },
            { width: 210, height: 320 });

        assert.ok(placement.left >= 0, `left was ${placement.left}`);
        assert.ok(placement.top >= 0, `top was ${placement.top}`);
    });

    /**
     * A menu taller than the window cannot fit whichever way it is flipped, so it is pinned to the
     * top and given a height it can scroll within - the alternative is items reachable by nobody.
     */
    it('caps the height when the menu is taller than the window', () => {
        const placement = placeMenu({ x: 10, y: 10 }, { width: 200, height: 900 }, VIEWPORT);

        assert.ok(placement.maxHeight !== null && placement.maxHeight <= VIEWPORT.height,
            `maxHeight was ${placement.maxHeight}`);
        assert.equal(placement.top, 4);
    });

    /**
     * Flipping puts the menu's far edge at the pointer, so it only helps when the pointer is itself
     * on screen. It is not always: a virtualised grid renders rows past the fold, and a menu opened
     * against one of those would otherwise be placed off the window in both directions.
     */
    it('clamps into the window when the anchor is outside it', () => {
        const placement = placeMenu({ x: 100, y: 1025 }, { width: 200, height: 189 }, VIEWPORT);

        assert.ok(placement.top >= 0 && placement.top + 189 <= VIEWPORT.height,
            `top was ${placement.top} for a 189px menu in an ${VIEWPORT.height}px window`);
    });

    it('keeps a small margin from the edge it is pushed against', () => {
        const placement = placeMenu({ x: 995, y: 795 }, SIZE, VIEWPORT);

        assert.ok(placement.left + SIZE.width <= VIEWPORT.width,
            `menu right edge was ${placement.left + SIZE.width}`);
        assert.ok(placement.top + SIZE.height <= VIEWPORT.height,
            `menu bottom edge was ${placement.top + SIZE.height}`);
    });
});
