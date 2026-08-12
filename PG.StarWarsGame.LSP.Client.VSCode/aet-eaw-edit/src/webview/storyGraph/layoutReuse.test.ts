// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { canReuseStoredLayout } from './layoutReuse';

describe('canReuseStoredLayout', () => {
    it('reuses a layout that covers every event', () => {
        assert.equal(canReuseStoredLayout(['a', 'b'], new Set(['a', 'b'])), true);
    });

    // The regression this exists for: the check used to be `every`, so adding one event to a
    // campaign discarded the whole saved arrangement, re-ran elk and moved every node the user had
    // placed - and did it again on every open until the layout was saved afresh.
    it('reuses a layout that is missing a newly added event', () => {
        assert.equal(canReuseStoredLayout(['a', 'b', 'new'], new Set(['a', 'b'])), true);
    });

    it('reuses a layout even when most events are new', () => {
        // Still better than a full re-layout: the stored nodes keep the positions the user chose,
        // and the new ones are placed beside their neighbours. Rearrange is there for when the
        // user actually wants everything moved.
        assert.equal(canReuseStoredLayout(['a', 'b', 'c', 'd'], new Set(['a'])), true);
    });

    it('rebuilds when nothing at all is stored - a genuine first open', () => {
        assert.equal(canReuseStoredLayout(['a', 'b'], new Set()), false);
    });

    it('rebuilds when the stored entries are for other events entirely', () => {
        assert.equal(canReuseStoredLayout(['a', 'b'], new Set(['x', 'y'])), false);
    });

    it('rebuilds a graph with no events', () => {
        assert.equal(canReuseStoredLayout([], new Set(['a'])), false);
    });
});
