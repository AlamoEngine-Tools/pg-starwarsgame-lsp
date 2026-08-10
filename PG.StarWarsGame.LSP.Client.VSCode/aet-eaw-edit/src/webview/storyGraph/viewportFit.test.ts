// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { FIT_MARGIN, fitZoom } from './viewportFit';

describe('fitZoom', () => {
    // The one that mattered: a webview whose container has not been laid out yet reports 0x0.
    // The old inline arithmetic turned that into k = 0, which is below K_DETAIL, so the graph
    // silently took the windowed branch, mounted nothing, and rendered blank.
    it('refuses to answer for an unmeasured view rather than returning zero', () => {
        assert.equal(fitZoom({ width: 0, height: 0 }, { width: 800, height: 600 }), null);
        assert.equal(fitZoom({ width: 1200, height: 0 }, { width: 800, height: 600 }), null);
        assert.equal(fitZoom({ width: 0, height: 900 }, { width: 800, height: 600 }), null);
    });

    it('refuses a view of nonsense size', () => {
        assert.equal(fitZoom({ width: -10, height: 900 }, { width: 800, height: 600 }), null);
        assert.equal(fitZoom({ width: Number.NaN, height: 900 }, { width: 800, height: 600 }), null);
    });

    it('shrinks a graph that is larger than the view, leaving the margin', () => {
        // Content twice the view's width; height is not the constraint.
        const k = fitZoom({ width: 1000, height: 1000 }, { width: 2000, height: 500 });
        assert.equal(k, 0.5 * FIT_MARGIN);
    });

    it('is limited by whichever axis is tighter', () => {
        const k = fitZoom({ width: 1000, height: 1000 }, { width: 2000, height: 4000 });
        assert.equal(k, 0.25 * FIT_MARGIN, 'height is the tighter axis here');
    });

    it('never zooms in past 1:1 for a graph that already fits', () => {
        assert.equal(fitZoom({ width: 4000, height: 4000 }, { width: 100, height: 100 }), 1);
    });

    // A single node, or a degenerate bounding box, must not divide by zero.
    it('treats an empty bounding box as one unit rather than dividing by zero', () => {
        const k = fitZoom({ width: 1000, height: 1000 }, { width: 0, height: 0 });
        assert.equal(k, 1);
        assert.ok(Number.isFinite(k!));
    });
});
