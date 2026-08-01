// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { gridFooterLabel } from './gridFooter';

describe('gridFooterLabel', () => {
    it('states the total when nothing is held back', () => {
        assert.equal(gridFooterLabel(19222, 19222, 0), '19,222 rows');
    });

    it('separates thousands, because 19222 does not read at a glance', () => {
        assert.ok(gridFooterLabel(19222, 19222, 0).includes('19,222'));
    });

    it('says how many rows are being held back', () => {
        assert.equal(
            gridFooterLabel(19222, 1203, 1),
            '19,222 rows, 18,019 filtered out, 1 filter active');
    });

    it('pluralises the filter count', () => {
        assert.ok(gridFooterLabel(100, 40, 2).endsWith('2 filters active'));
    });

    /**
     * A filter that happens to match everything is still worth reporting: it explains why the grid
     * will start hiding rows the moment the file changes.
     */
    it('reports an active filter even when it hides nothing', () => {
        assert.equal(gridFooterLabel(100, 100, 1), '100 rows, 1 filter active');
    });

    it('handles an empty file', () => {
        assert.equal(gridFooterLabel(0, 0, 0), '0 rows');
    });

    it('uses the singular for one row', () => {
        assert.equal(gridFooterLabel(1, 1, 0), '1 row');
    });
});
