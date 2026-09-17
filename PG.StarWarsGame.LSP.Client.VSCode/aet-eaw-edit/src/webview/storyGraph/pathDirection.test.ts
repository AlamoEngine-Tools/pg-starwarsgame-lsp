// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { PATH_DIRECTIONS, directionLabel, isPathDirection } from './pathDirection';

describe('path directions', () => {
    /**
     * The filter used to be one way only: what an event leads to. An author asked for the inverse, so
     * the node's filter button offers three - what leads here, both, what this leads to.
     */
    it('offers the three directions, in reading order in to out', () => {
        assert.deepEqual(PATH_DIRECTIONS.map(d => d.direction), ['Upstream', 'Both', 'Downstream']);
    });

    // One word each, naming the edges kept - the icon carries the direction, so the label need not.
    it('labels each one in a single word', () => {
        assert.equal(directionLabel('Upstream'), 'Incoming');
        assert.equal(directionLabel('Both'), 'Both');
        assert.equal(directionLabel('Downstream'), 'Outgoing');
    });

    // The wire values the server parses; the default is what the filter always did.
    it('treats a missing or unknown direction as downstream', () => {
        assert.equal(directionLabel(undefined), 'Outgoing');
        assert.equal(directionLabel('sideways'), 'Outgoing');
    });

    it('gives every direction an icon of its own', () => {
        const icons = PATH_DIRECTIONS.map(d => d.icon);
        assert.equal(new Set(icons).size, PATH_DIRECTIONS.length);
    });

    it('recognises only the three wire values', () => {
        assert.ok(isPathDirection('Upstream'));
        assert.ok(isPathDirection('Both'));
        assert.ok(isPathDirection('Downstream'));
        assert.equal(isPathDirection('upstream'), false);
        assert.equal(isPathDirection(''), false);
    });
});
