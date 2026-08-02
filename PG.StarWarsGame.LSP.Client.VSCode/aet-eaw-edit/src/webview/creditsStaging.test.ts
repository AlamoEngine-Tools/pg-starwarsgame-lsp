// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { applyStaged, coalesce, LocCommand, STAGED_KINDS } from './creditsStaging';
import { LocRow } from './loc/locRow';

function rows(): LocRow[] {
    return [
        { index: 0, key: 'A', values: [{ language: 'ENGLISH', value: 'a' }] },
        { index: 1, key: 'B', values: [{ language: 'ENGLISH', value: 'b' }] },
        { index: 2, key: 'C', values: [{ language: 'ENGLISH', value: 'c' }] },
    ];
}

function setCell(index: number, value: string, language = 'ENGLISH'): LocCommand {
    return { kind: 'setCell', index, language, value };
}

function keysOf(list: LocRow[]): string[] {
    return list.map(r => r.key);
}

describe('applyStaged', () => {
    it('shows a cell edit immediately', () => {
        const result = applyStaged(rows(), setCell(1, 'changed'));

        assert.equal(result[1].values[0].value, 'changed');
    });

    // Every row's index has to stay equal to its position, or the next staged command addresses
    // the wrong row and the server composes something the user never asked for.
    it('renumbers rows after an insert', () => {
        const result = applyStaged(rows(), { kind: 'insertRow', index: 0, key: 'NEW', values: [] });

        assert.deepEqual(keysOf(result), ['NEW', 'A', 'B', 'C']);
        assert.deepEqual(result.map(r => r.index), [0, 1, 2, 3]);
    });

    it('renumbers rows after a delete', () => {
        const result = applyStaged(rows(), { kind: 'deleteRow', index: 0 });

        assert.deepEqual(keysOf(result), ['B', 'C']);
        assert.deepEqual(result.map(r => r.index), [0, 1]);
    });

    it('moves a row to the requested position', () => {
        const result = applyStaged(rows(), { kind: 'moveRow', index: 2, toIndex: 0 });

        assert.deepEqual(keysOf(result), ['C', 'A', 'B']);
    });

    it('renames a key without touching values', () => {
        const result = applyStaged(rows(), { kind: 'setKey', index: 0, key: 'RENAMED' });

        assert.equal(result[0].key, 'RENAMED');
        assert.equal(result[0].values[0].value, 'a');
    });

    it('adds an empty cell to every row for a new language', () => {
        const result = applyStaged(rows(), { kind: 'addLanguage', language: 'GERMAN' });

        assert.equal(result.length, 3);
        assert.deepEqual(result.map(r => r.values.length), [2, 2, 2]);
        assert.equal(result[0].values[1].value, '');
    });

    // Duplicate keys are the whole reason rows are addressed by index; editing one must not touch
    // its twin.
    it('edits only the addressed row when two share a key', () => {
        const duplicates: LocRow[] = [
            { index: 0, key: 'SAME', values: [{ language: 'ENGLISH', value: 'first' }] },
            { index: 1, key: 'SAME', values: [{ language: 'ENGLISH', value: 'second' }] },
        ];

        const result = applyStaged(duplicates, setCell(1, 'changed'));

        assert.equal(result[0].values[0].value, 'first');
        assert.equal(result[1].values[0].value, 'changed');
    });

    it('leaves the input untouched', () => {
        const original = rows();
        applyStaged(original, { kind: 'deleteRow', index: 0 });

        assert.equal(original.length, 3);
    });

    it('ignores a command whose index is out of range', () => {
        const result = applyStaged(rows(), setCell(99, 'x'));

        assert.deepEqual(keysOf(result), ['A', 'B', 'C']);
    });
});

describe('coalesce', () => {
    // Typing into a cell stages a command per keystroke; sending 200 of them for one cell would
    // make the batch enormous and the failure messages meaningless.
    it('folds repeated edits of one cell into the last', () => {
        const result = coalesce([setCell(0, 'a'), setCell(0, 'ab'), setCell(0, 'abc')]);

        assert.equal(result.length, 1);
        assert.equal(result[0].value, 'abc');
    });

    it('keeps edits to different cells', () => {
        const result = coalesce([setCell(0, 'x'), setCell(1, 'y'), setCell(0, 'z', 'GERMAN')]);

        assert.equal(result.length, 3);
    });

    // Folding must not reorder: a later insert changes what a subsequent index means, so an edit
    // hoisted above it would land on the wrong row.
    it('does not move an edit across a structural command', () => {
        const result = coalesce([
            setCell(0, 'first'),
            { kind: 'deleteRow', index: 0 },
            setCell(0, 'second'),
        ]);

        assert.equal(result.length, 3);
        assert.deepEqual(result.map(c => c.kind), ['setCell', 'deleteRow', 'setCell']);
    });

    it('replays to the same result as the uncoalesced queue', () => {
        const queue = [setCell(0, 'a'), setCell(0, 'ab'), setCell(1, 'z'), setCell(0, 'abc')];

        const direct = queue.reduce(applyStaged, rows());
        const folded = coalesce(queue).reduce(applyStaged, rows());

        assert.deepEqual(folded, direct);
    });

    it('leaves an empty queue empty', () => {
        assert.deepEqual(coalesce([]), []);
    });
});

describe('STAGED_KINDS', () => {
    // Nothing may reach disk outside Save; an unlisted kind would have no staged representation.
    it('covers every command the grid can produce', () => {
        for (const kind of ['setCell', 'setKey', 'insertRow', 'deleteRow', 'moveRow', 'addLanguage']) {
            assert.ok(STAGED_KINDS.has(kind), `${kind} must be staged`);
        }
    });
});
