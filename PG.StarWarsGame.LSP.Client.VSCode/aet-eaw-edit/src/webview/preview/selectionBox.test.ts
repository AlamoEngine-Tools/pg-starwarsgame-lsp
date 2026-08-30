// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { boxRoots, boxTargetFor, rowsInBox } from './selectionBox';

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

describe('boxTargetFor', () => {
    /**
     * What a selected HARDPOINT's box should enclose.
     *
     * Not its attach bone's subtree, which is what it used to be and which is wrong in both
     * directions at once. Measured: the Executor's left turbolaser boxed 227 x 63 x 2211 on a 5094
     * hull - the turret's own meshes plus seven empty fire-point bones strung down the ship - while
     * the Nebulon-B's boxed 7 x 7 x 7, a fallback marker, because its attach bone carries no
     * geometry at all.
     */
    it('outlines the model a hardpoint attaches, when it has one', () => {
        assert.deepEqual(
            boxTargetFor('hp:HP_Turbolaser', new Set(['hull', 'hp:HP_Turbolaser'])),
            { kind: 'part', id: 'hp:HP_Turbolaser' });
    });

    it('falls back to the attach bone when the hardpoint attaches no model', () => {
        // 137 of foc's 355 hardpoints name no Model_To_Attach. A small cube on the bone is the
        // honest mark for those - there is no outline to draw.
        assert.deepEqual(boxTargetFor(null, new Set(['hull'])), { kind: 'bone' });
    });

    it('falls back to the bone while the part has not loaded yet', () => {
        // Geometry arrives one part at a time. Boxing a part that is not in the scene would draw
        // nothing, which reads as a selection that did not take.
        assert.deepEqual(
            boxTargetFor('hp:HP_Turbolaser', new Set(['hull'])), { kind: 'bone' });
    });

    it('treats an empty part id as no part', () => {
        assert.deepEqual(boxTargetFor('', new Set(['hull'])), { kind: 'bone' });
    });
});
