// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    centredPosition, clampPosition, clampSize, fromStoredGeometry, MIN_MODAL_SIZE, resizeRect,
    toStoredGeometry,
} from './modalGeometry';

const viewport = { width: 1000, height: 800 };
const size = { width: 400, height: 300 };

describe('clampPosition', () => {
    it('leaves a position that is already on screen alone', () => {
        assert.deepEqual(clampPosition({ x: 100, y: 50 }, size, viewport), { x: 100, y: 50 });
    });

    // The title bar is the only way to drag it back, so it must never leave the viewport.
    it('stops the dialog being dragged off the top or left', () => {
        assert.deepEqual(clampPosition({ x: -80, y: -40 }, size, viewport), { x: 0, y: 0 });
    });

    it('stops the dialog being dragged past the right or bottom edge', () => {
        assert.deepEqual(clampPosition({ x: 5000, y: 5000 }, size, viewport), { x: 600, y: 500 });
    });

    // A dialog larger than the window pins to the top-left rather than being pushed off-screen,
    // which is what a naive clamp against a negative bound would do.
    it('pins a dialog larger than the viewport to the top-left', () => {
        const huge = { width: 1400, height: 1200 };

        assert.deepEqual(clampPosition({ x: 200, y: 200 }, huge, viewport), { x: 0, y: 0 });
    });
});

describe('clampSize', () => {
    it('leaves a size that fits alone', () => {
        assert.deepEqual(clampSize(size, { x: 0, y: 0 }, viewport), size);
    });

    it('never shrinks below the point the buttons start overlapping the body', () => {
        const tiny = clampSize({ width: 10, height: 10 }, { x: 0, y: 0 }, viewport);

        assert.deepEqual(tiny, MIN_MODAL_SIZE);
    });

    // Resizing grows from the top-left corner, so what is left of the viewport depends on where the
    // dialog currently sits.
    it('does not let a resize run past the edge from where the dialog sits', () => {
        const clamped = clampSize({ width: 900, height: 700 }, { x: 300, y: 200 }, viewport);

        assert.deepEqual(clamped, { width: 700, height: 600 });
    });

    it('prefers the minimum over the viewport when the dialog sits near the edge', () => {
        const clamped = clampSize({ width: 900, height: 700 }, { x: 990, y: 790 }, viewport);

        assert.deepEqual(clamped, MIN_MODAL_SIZE);
    });
});

describe('remembered geometry', () => {
    it('stores the corner as a fraction of the viewport', () => {
        const stored = toStoredGeometry({ x: 250, y: 200, width: 400, height: 300 }, viewport);

        assert.deepEqual(stored, { xRatio: 0.25, yRatio: 0.25, width: 400, height: 300 });
    });

    it('round-trips unchanged when the window has not moved', () => {
        const rect = { x: 250, y: 200, width: 400, height: 300 };

        assert.deepEqual(fromStoredGeometry(toStoredGeometry(rect, viewport), viewport), rect);
    });

    // A dialog parked against the right edge of a wide window belongs against the right edge of a
    // narrow one, not off the side of it.
    it('keeps a dialog against the edge it was parked at when the window is smaller', () => {
        const stored = toStoredGeometry({ x: 700, y: 500, width: 300, height: 300 }, viewport);
        const restored = fromStoredGeometry(stored, { width: 500, height: 400 });

        assert.equal(restored.x + restored.width, 500);
        assert.equal(restored.y + restored.height, 400);
    });

    // Proportional placement is only the starting point - it still has to fit.
    it('places a dialog by ratio when there is room for it', () => {
        const stored = toStoredGeometry({ x: 250, y: 200, width: 300, height: 200 }, viewport);
        const restored = fromStoredGeometry(stored, { width: 800, height: 600 });

        assert.deepEqual([restored.x, restored.y], [200, 150]);
    });

    // "Resize relatively": one scale factor for both axes, so it keeps its shape rather than being
    // squashed into whatever is left.
    it('scales an oversized dialog down by a single factor', () => {
        const restored = fromStoredGeometry(
            { xRatio: 0, yRatio: 0, width: 800, height: 600 }, { width: 400, height: 600 });

        assert.deepEqual([restored.width, restored.height], [400, 300]);
    });

    it('does not enlarge a dialog just because the window grew', () => {
        const stored = toStoredGeometry({ x: 0, y: 0, width: 400, height: 300 }, viewport);
        const restored = fromStoredGeometry(stored, { width: 4000, height: 3000 });

        assert.deepEqual([restored.width, restored.height], [400, 300]);
    });

    it('never restores below the minimum usable size', () => {
        const restored = fromStoredGeometry(
            { xRatio: 0, yRatio: 0, width: 10, height: 10 }, viewport);

        assert.deepEqual([restored.width, restored.height], [MIN_MODAL_SIZE.width, MIN_MODAL_SIZE.height]);
    });

    // Restoring must not put the title bar off-screen: it is the only way to drag it back.
    it('pulls a remembered position back inside the window', () => {
        const restored = fromStoredGeometry(
            { xRatio: 0.95, yRatio: 0.95, width: 400, height: 300 }, viewport);

        assert.ok(restored.x + restored.width <= viewport.width);
        assert.ok(restored.y + restored.height <= viewport.height);
    });

    // A hidden webview reports a zero-sized viewport; that must not produce NaN ratios that then
    // poison the stored value for every later session.
    it('survives a zero-sized viewport without producing NaN', () => {
        const stored = toStoredGeometry({ x: 10, y: 10, width: 400, height: 300 },
            { width: 0, height: 0 });

        assert.ok(Number.isFinite(stored.xRatio));
        assert.ok(Number.isFinite(stored.yRatio));
    });
});

describe('resizeRect', () => {
    const base = { x: 300, y: 200, width: 400, height: 300 };

    it('grows to the right without moving the origin', () => {
        assert.deepEqual(resizeRect(base, 'e', 100, 0, viewport),
            { x: 300, y: 200, width: 500, height: 300 });
    });

    it('grows downward without moving the origin', () => {
        assert.deepEqual(resizeRect(base, 's', 0, 100, viewport),
            { x: 300, y: 200, width: 400, height: 400 });
    });

    // The whole reason the north and west handles are not just a sign flip: the opposite edge has
    // to stay exactly where it was while the origin moves.
    it('moves the origin when dragging the west edge and leaves the right edge alone', () => {
        const result = resizeRect(base, 'w', -100, 0, viewport);

        assert.deepEqual(result, { x: 200, y: 200, width: 500, height: 300 });
        assert.equal(result.x + result.width, base.x + base.width);
    });

    it('moves the origin when dragging the north edge and leaves the bottom alone', () => {
        const result = resizeRect(base, 'n', -100, 0 - 0, viewport);

        assert.equal(result.y, 200);
        assert.equal(result.y + result.height, base.y + base.height);
    });

    it('drags both axes from a corner', () => {
        assert.deepEqual(resizeRect(base, 'se', 50, 60, viewport),
            { x: 300, y: 200, width: 450, height: 360 });
    });

    it('drags a corner that moves the origin on both axes', () => {
        assert.deepEqual(resizeRect(base, 'nw', -50, -60, viewport),
            { x: 250, y: 140, width: 450, height: 360 });
    });

    // Dragging the left edge rightwards past the minimum must stop, not push the right edge along.
    it('stops at the minimum width instead of dragging the far edge with it', () => {
        const result = resizeRect(base, 'w', 10_000, 0, viewport);

        assert.equal(result.width, MIN_MODAL_SIZE.width);
        assert.equal(result.x + result.width, base.x + base.width);
    });

    it('stops at the minimum height when the north edge is dragged down past it', () => {
        const result = resizeRect(base, 'n', 0, 10_000, viewport);

        assert.equal(result.height, MIN_MODAL_SIZE.height);
        assert.equal(result.y + result.height, base.y + base.height);
    });

    it('does not let an edge leave the viewport', () => {
        assert.equal(resizeRect(base, 'e', 10_000, 0, viewport).width, viewport.width - base.x);
        assert.equal(resizeRect(base, 'w', -10_000, 0, viewport).x, 0);
        assert.equal(resizeRect(base, 'n', 0, -10_000, viewport).y, 0);
        assert.equal(resizeRect(base, 's', 0, 10_000, viewport).height, viewport.height - base.y);
    });

    it('leaves the untouched axis exactly as it was', () => {
        const result = resizeRect(base, 'e', 100, 999, viewport);

        assert.equal(result.y, base.y);
        assert.equal(result.height, base.height);
    });
});

describe('centredPosition', () => {
    it('centres a dialog that fits', () => {
        assert.deepEqual(centredPosition(size, viewport), { x: 300, y: 250 });
    });

    it('keeps a dialog larger than the viewport reachable', () => {
        assert.deepEqual(centredPosition({ width: 1400, height: 1200 }, viewport), { x: 0, y: 0 });
    });
});
