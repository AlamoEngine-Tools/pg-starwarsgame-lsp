// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { pickShipName } from '../shipNamePick';

describe('pickShipName', () => {
    const names = ['Allecto', 'Devastator', 'Tyrant'];

    it('always returns a name from the pool', () => {
        // The property that matters: it must never invent a name the author's data lacks.
        for (let i = 0; i < 200; i++) {
            assert.ok(names.includes(pickShipName(names)!));
        }
    });

    it('can reach every name in the pool', () => {
        assert.equal(pickShipName(names, () => 0), 'Allecto');
        assert.equal(pickShipName(names, () => 0.5), 'Devastator');
        assert.equal(pickShipName(names, () => 0.99), 'Tyrant');
    });

    it('stays in range when the generator returns exactly 1', () => {
        // Math.random() never returns 1, but a seeded generator in a test might, and running off
        // the end would hand back undefined as if it were a name.
        assert.equal(pickShipName(names, () => 1), 'Tyrant');
    });

    it('returns null for an empty pool rather than a blank name', () => {
        // The card then keeps the object's class line.
        assert.equal(pickShipName([]), null);
    });

    it('returns the only name for a single-entry pool', () => {
        assert.equal(pickShipName(['Home One']), 'Home One');
    });
});
