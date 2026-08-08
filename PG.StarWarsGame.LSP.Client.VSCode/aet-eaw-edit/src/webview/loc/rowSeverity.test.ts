// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { LocRow } from './locRow';
import { rowSeverityClass, severityByRow } from './rowSeverity';

function row(index: number, key: string): LocRow {
    return { index, key, values: [] };
}

const rows = [row(0, 'TEXT_A'), row(1, 'TEXT_B'), row(2, 'TEXT_A')];

describe('severityByRow', () => {
    it('marks nothing when the file checked out clean', () => {
        assert.equal(severityByRow([], rows).size, 0);
    });

    // A valid row must stay unmarked, or the tint covers the whole table and means nothing.
    it('leaves rows with no finding out of the map entirely', () => {
        const marked = severityByRow([{ severity: 'error', message: '', index: 1 }], rows);

        assert.deepEqual([...marked.keys()], [1]);
    });

    it('marks the row an index-addressed problem points at', () => {
        const marked = severityByRow([{ severity: 'info', message: '', index: 2 }], rows);

        assert.equal(marked.get(2), 'info');
    });

    // Keys are not unique - a duplicate is the point of one of these findings - so both rows the
    // key appears on have to light up, not just the first.
    it('marks every row carrying a key-addressed problem', () => {
        const marked = severityByRow([{ severity: 'error', message: '', key: 'TEXT_A' }], rows);

        assert.deepEqual([...marked.keys()].sort(), [0, 2]);
    });

    it('keeps the worst severity when a row has several findings', () => {
        const marked = severityByRow([
            { severity: 'info', message: '', index: 1 },
            { severity: 'error', message: '', index: 1 },
            { severity: 'warning', message: '', index: 1 },
        ], rows);

        assert.equal(marked.get(1), 'error');
    });

    it('does not let a milder finding arriving later downgrade a row', () => {
        const marked = severityByRow([
            { severity: 'error', message: '', index: 0 },
            { severity: 'info', message: '', key: 'TEXT_A' },
        ], rows);

        assert.equal(marked.get(0), 'error');
    });

    // The one problem a key cannot identify is a row that has no key.
    it('locates a blank-key problem by its index', () => {
        const withBlank = [row(0, ''), row(1, 'TEXT_B')];
        const marked = severityByRow(
            [{ severity: 'error', message: 'no key', key: null, index: 0 }], withBlank);

        assert.equal(marked.get(0), 'error');
    });

    it('ignores a problem that names neither a row nor a key', () => {
        assert.equal(severityByRow([{ severity: 'error', message: 'file-wide' }], rows).size, 0);
    });
});

describe('rowSeverityClass', () => {
    it('has no class for a row with nothing against it', () => {
        assert.equal(rowSeverityClass(undefined), undefined);
    });

    it('names a class per level', () => {
        assert.equal(rowSeverityClass('error'), 'row-sev-error');
        assert.equal(rowSeverityClass('warning'), 'row-sev-warning');
        assert.equal(rowSeverityClass('info'), 'row-sev-info');
    });

    it('shows an unknown severity as a warning rather than hiding it', () => {
        assert.equal(rowSeverityClass('something-new'), 'row-sev-warning');
    });
});
