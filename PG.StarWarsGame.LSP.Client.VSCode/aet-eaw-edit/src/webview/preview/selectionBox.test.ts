// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { boxRoots, rowsInBox } from './selectionBox';

//   Root
//   +- Hull
//   |  +- Engine
//   |     +- Nozzle
//   +- Wing
const parents = new Map<string, string | null>([
    ['Root', null],
    ['Hull', 'Root'],
    ['Engine', 'Hull'],
    ['Nozzle', 'Engine'],
    ['Wing', 'Root'],
]);

describe('boxRoots', () => {
    it('boxes each selected row when none contains another', () => {
        assert.deepEqual(boxRoots(['Hull', 'Wing'], parents), ['Hull', 'Wing']);
    });

    it('drops a row whose parent is selected too', () => {
        // One box around the arm, not a second one inside it around the hand: two nested boxes read
        // as two separate things being pointed at.
        assert.deepEqual(boxRoots(['Hull', 'Engine'], parents), ['Hull']);
    });

    it('drops a row any ANCESTOR of which is selected, not just its parent', () => {
        assert.deepEqual(boxRoots(['Hull', 'Nozzle'], parents), ['Hull']);
    });

    it('keeps the order the reader selected in', () => {
        assert.deepEqual(boxRoots(['Wing', 'Hull'], parents), ['Wing', 'Hull']);
    });

    it('has nothing to box when nothing is selected', () => {
        assert.deepEqual(boxRoots([], parents), []);
    });

    it('keeps a row whose parent is unknown to the map', () => {
        // A row can arrive before the tree that explains it. Dropping it would silently box
        // nothing, which looks exactly like a broken selection.
        assert.deepEqual(boxRoots(['Stray'], parents), ['Stray']);
    });
});

describe('rowsInBox', () => {
    it('takes the row and everything beneath it', () => {
        assert.deepEqual(rowsInBox('Hull', parents).sort(), ['Engine', 'Hull', 'Nozzle']);
    });

    it('does not reach sideways', () => {
        assert.deepEqual(rowsInBox('Wing', parents), ['Wing']);
    });

    it('takes the whole model from the root', () => {
        assert.equal(rowsInBox('Root', parents).length, 5);
    });

    it('survives a parent map that loops back on itself', () => {
        // Never seen from the exporter, but a box that hangs the render loop is a far worse failure
        // than one that comes out too small.
        const looped = new Map<string, string | null>([['a', 'b'], ['b', 'a']]);

        assert.deepEqual(rowsInBox('a', looped).sort(), ['a', 'b']);
    });
});
