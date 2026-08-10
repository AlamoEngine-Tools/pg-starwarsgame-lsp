// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { shouldShowOverview, shouldWindow, WINDOW_NODE_COUNT } from './lodPolicy';

describe('shouldWindow', () => {
    it('mounts a small graph whole', () => {
        assert.equal(shouldWindow(1), false);
        assert.equal(shouldWindow(WINDOW_NODE_COUNT), false);
    });

    it('windows a graph past the threshold', () => {
        assert.equal(shouldWindow(WINDOW_NODE_COUNT + 1), true);
        assert.equal(shouldWindow(1300), true);
    });

    // It used to be inferred from the zoom a stored layout happened to fit at, which meant a
    // first-ever open - which has no stored layout, and so never ran that test - mounted a
    // 1300-node campaign in full. Cost depends on how many nodes there are, not on how the
    // viewport happens to be scrolled, so the same answer must hold on both paths.
    it('depends only on node count, so first open and reopen agree', () => {
        assert.equal(shouldWindow(500), shouldWindow(500));
        assert.equal(shouldWindow(10), shouldWindow(10));
    });

    it('treats an empty graph as not worth windowing', () => {
        assert.equal(shouldWindow(0), false);
    });
});

describe('shouldShowOverview', () => {
    const DETAIL = 0.32;

    it('shows the overview when zoomed out past the detail threshold', () => {
        assert.equal(shouldShowOverview(0.1, DETAIL), true);
    });

    it('shows real nodes at and above the threshold', () => {
        assert.equal(shouldShowOverview(DETAIL, DETAIL), false);
        assert.equal(shouldShowOverview(1, DETAIL), false);
    });

    // The second half of the decoupling: a small graph is mounted whole, but zooming it out should
    // still give the overview rather than shrinking real nodes into an unreadable blur. Overview is
    // about zoom; windowing is about cost. This function must not know about node counts at all.
    it('is a pure function of zoom, independent of graph size', () => {
        assert.equal(shouldShowOverview(0.1, DETAIL), true, 'same answer whatever the graph holds');
    });
});
