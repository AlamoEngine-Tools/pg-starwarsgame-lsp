// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { ARRANGE_OPTIONS } from './arrangeOptions';
import { arrangePositions } from './modelArrange';

const NODE_SPACING = Number(ARRANGE_OPTIONS['elk.spacing.nodeNode']);

function event(id: string, height = 220) {
    return { id, width: 280, height, hasIn: true, hasOut: true };
}

describe('arrangePositions', () => {
    /**
     * #131: the first open used to mount all 2860 nodes of a campaign purely so elk could measure
     * them - 18 of its 28 seconds. The model already knows every size, so the layout runs on the
     * model and nothing mounts until the window is reconciled.
     */
    it('places every node it is given', async () => {
        const positions = await arrangePositions(
            [event('a'), event('b'), event('c')],
            [{ id: 'e0', source: 'a', target: 'b' }, { id: 'e1', source: 'b', target: 'c' }]);

        assert.deepEqual([...positions.keys()].sort(), ['a', 'b', 'c']);
        for (const [id, at] of positions) {
            assert.ok(Number.isFinite(at.x) && Number.isFinite(at.y), `${id} has no position`);
        }
    });

    // elk.direction RIGHT: a chain runs left to right, one layer per link.
    it('lays a chain out in order, left to right', async () => {
        const positions = await arrangePositions(
            [event('a'), event('b'), event('c')],
            [{ id: 'e0', source: 'a', target: 'b' }, { id: 'e1', source: 'b', target: 'c' }]);

        assert.ok(positions.get('a')!.x < positions.get('b')!.x);
        assert.ok(positions.get('b')!.x < positions.get('c')!.x);
    });

    /** The spacing the options promise, which is what keeps mounted nodes from overlapping. */
    it('keeps siblings in one layer apart by the configured spacing', async () => {
        const positions = await arrangePositions(
            [event('root'), event('a', 300), event('b', 160)],
            [{ id: 'e0', source: 'root', target: 'a' }, { id: 'e1', source: 'root', target: 'b' }]);

        const a = positions.get('a')!, b = positions.get('b')!;
        assert.equal(Math.round(a.x), Math.round(b.x));

        const [upper, upperHeight] = a.y < b.y ? [a, 300] : [b, 160];
        const lower = a.y < b.y ? b : a;
        assert.ok(lower.y - (upper.y + upperHeight) >= NODE_SPACING - 1,
            `siblings only ${lower.y - (upper.y + upperHeight)}px apart`);
    });

    // A node nothing connects still has to land somewhere, or it would be dropped from the layout.
    it('places an unconnected node too', async () => {
        const positions = await arrangePositions([event('lonely')], []);

        assert.ok(positions.has('lonely'));
    });

    // Junctions carry no sockets until an edge gives them one; the layout must not assume any.
    it('accepts nodes without sockets', async () => {
        const positions = await arrangePositions(
            [{ id: 'j', width: 48, height: 48, hasIn: false, hasOut: false }], []);

        assert.ok(positions.has('j'));
    });

    it('returns nothing for an empty graph', async () => {
        assert.equal((await arrangePositions([], [])).size, 0);
    });
});
