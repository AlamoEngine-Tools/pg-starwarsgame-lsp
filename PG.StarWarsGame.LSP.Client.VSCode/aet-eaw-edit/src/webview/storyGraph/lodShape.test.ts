// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { JUNCTION_TOKEN, LIFECYCLE_TOKENS } from './palette';
import { lodShape } from './lodShape';

/**
 * Zoomed out, every node was a rectangle and a junction took the colour a FIRED event takes - so
 * the structural nodes read as a lifecycle they do not have, and the two could not be told apart
 * at all. A junction has a shape of its own when mounted (AND is a circle, OR a rotated square);
 * the overview draws that shape now, in a neutral colour, so the two axes stay separate at every
 * zoom level.
 */
describe('lodShape', () => {
    it('gives AND a circle, mounted or staged', () => {
        assert.equal(lodShape('AndJunction'), 'circle');
        assert.equal(lodShape('StagingAnd'), 'circle');
    });

    it('gives OR a diamond, mounted or staged', () => {
        assert.equal(lodShape('OrJunction'), 'diamond');
        assert.equal(lodShape('StagingOr'), 'diamond');
    });

    it('leaves events and every other node a rectangle', () => {
        assert.equal(lodShape('Event'), 'rect');
        assert.equal(lodShape('Portal'), 'rect');
        assert.equal(lodShape('TacticalPlot'), 'rect');
    });

    // A kind the server grows later must not silently become a circle.
    it('falls back to a rectangle for an unknown kind', () => {
        assert.equal(lodShape('SomethingNew'), 'rect');
        assert.equal(lodShape(''), 'rect');
    });
});

describe('the junction colour', () => {
    // The reported complaint, as an assertion: purple IS Fired in the legend, so a junction
    // wearing it claims a lifecycle it cannot have.
    it('is not any lifecycle colour', () => {
        const lifecycles = new Set(Object.values(LIFECYCLE_TOKENS));
        assert.ok(!lifecycles.has(JUNCTION_TOKEN),
            `${JUNCTION_TOKEN} is a lifecycle colour, so a junction reads as that lifecycle`);
    });
});
