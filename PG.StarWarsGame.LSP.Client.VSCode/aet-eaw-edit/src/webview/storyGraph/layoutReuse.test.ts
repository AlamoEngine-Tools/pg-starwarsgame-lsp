// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {canReuseStoredLayout, layoutEntryKey, nodeLayoutKey} from './layoutReuse';

describe('layout keys', () => {
    it('name an event by thread and event, folded, and a stored event entry the same way', () => {
        const node = {
            kind: 'Event',
            id: 'file:///ws/Data/XML/Story.xml#start',
            threadUri: 'file:///ws/Data/XML/Story.xml',
            label: 'Start'
        };
        assert.equal(nodeLayoutKey(node), 'file:///ws/data/xml/story.xml start');
        assert.equal(layoutEntryKey({
            threadUri: 'file:///ws/Data/XML/Story.xml',
            eventName: 'START'
        }), nodeLayoutKey(node));
    });

    /**
     * A battle graph is a third portals, junctions and script states. Named by id, they meet the
     * entries the server hands back by the same id; before this they had no key at all and were
     * re-placed on every open.
     */
    it('name any other node by its id, as the stored entry carries it', () => {
        const portal = {kind: 'GalacticPortal', id: 'galactic#m2.xml#file:///ws/Data/XML/Story.xml#e'};
        assert.equal(nodeLayoutKey(portal), portal.id);
        assert.equal(layoutEntryKey({threadUri: '', eventName: '', nodeId: portal.id}), portal.id);
    });
});

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
