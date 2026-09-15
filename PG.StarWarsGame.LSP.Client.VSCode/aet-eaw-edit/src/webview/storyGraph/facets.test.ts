// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { facetList } from './facets';

/**
 * The branch dropdown used to be built from the nodes the server had just returned - which are the
 * FILTERED nodes. Filtering to a branch therefore left that branch as the only one on offer, so the
 * one thing the control exists for (switching to a different branch) was the one thing it could not
 * do. The list now comes from the server as a facet of the whole campaign; these pin both that and
 * the fallback for a server too old to send one.
 */
describe('facetList', () => {
    it('prefers what the server reports over what survived the filter', () => {
        assert.deepEqual(
            facetList(['Act1', 'Act2', 'Act3'], ['Act1'], 'Act1'),
            ['Act1', 'Act2', 'Act3']);
    });

    // The whole bug, stated as a test: filtered to Act1, Act2 must still be reachable.
    it('keeps a branch the active filter has hidden', () => {
        const shown = facetList(['Act1', 'Act2'], ['Act1'], 'Act1');
        assert.ok(shown.includes('Act2'));
    });

    it('falls back to the returned nodes when the server sends nothing', () => {
        assert.deepEqual(facetList(undefined, ['Act2', 'Act1'], ''), ['Act1', 'Act2']);
    });

    // Degraded, the old defect is unavoidable - but the select must still be able to show its own
    // value, or it renders blank and reads as "no filter" while a filter is applied.
    it('re-adds the active value when only the fallback is available', () => {
        assert.deepEqual(facetList(undefined, ['Act1'], 'Act2'), ['Act1', 'Act2']);
    });

    it('does not duplicate the active value', () => {
        assert.deepEqual(facetList(['Act1'], [], 'Act1'), ['Act1']);
        assert.deepEqual(facetList(undefined, ['Act1'], 'Act1'), ['Act1']);
    });

    it('drops empty entries and sorts the fallback', () => {
        assert.deepEqual(facetList(undefined, ['b', '', 'a'], ''), ['a', 'b']);
    });

    // An empty array from the server is an answer ("this campaign has no branches"), not a
    // missing field - it must not silently fall back to the filtered nodes.
    it('treats an empty server list as authoritative', () => {
        assert.deepEqual(facetList([], ['Act1'], ''), []);
    });
});
