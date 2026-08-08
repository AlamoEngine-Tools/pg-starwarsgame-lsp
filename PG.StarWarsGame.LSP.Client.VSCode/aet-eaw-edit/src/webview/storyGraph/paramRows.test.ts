// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { StoryParamSchemaDto } from '../../protocol';
import { paramRowSpecs } from './paramRows';

function param(position: number, optional = false): StoryParamSchemaDto {
    return { position, valueType: 'string', optional };
}

describe('paramRowSpecs', () => {
    it('renders every declared param, filled or not', () => {
        const rows = paramRowSpecs(
            [{ position: 0, value: 'REBEL' }],
            [param(0), param(1)]);

        assert.deepEqual(rows, [
            { position: 0, value: 'REBEL', missing: false },
            { position: 1, value: '', missing: true },
        ]);
    });

    // The type dictates the fields: an optional slot is shown empty rather than absent, so the
    // shape of a node says what the type takes.
    it('shows an unset optional param as present but not missing', () => {
        const rows = paramRowSpecs([], [param(0, true)]);

        assert.deepEqual(rows, [{ position: 0, value: '', missing: false }]);
    });

    // Hiding a slot the schema does not know about would silently drop it on the next edit.
    it('keeps a value the schema does not declare', () => {
        const rows = paramRowSpecs(
            [{ position: 0, value: 'A' }, { position: 7, value: 'LEGACY' }],
            [param(0)]);

        assert.deepEqual(rows, [
            { position: 0, value: 'A', missing: false },
            { position: 7, value: 'LEGACY', missing: false },
        ]);
    });

    it('never calls an undeclared slot missing', () => {
        const rows = paramRowSpecs([{ position: 3, value: '' }], []);

        assert.deepEqual(rows, [{ position: 3, value: '', missing: false }]);
    });

    // Rendering and height estimation both read this; out of order they would disagree about
    // which row is where, and the node would be measured for a layout it does not have.
    it('orders by position regardless of how the parts arrived', () => {
        const rows = paramRowSpecs(
            [{ position: 5, value: 'five' }, { position: 0, value: 'zero' }],
            [param(2), param(0)]);

        assert.deepEqual(rows.map(r => r.position), [0, 2, 5]);
    });

    it('treats no params at all as an empty body', () => {
        assert.deepEqual(paramRowSpecs(null, []), []);
        assert.deepEqual(paramRowSpecs(undefined, []), []);
    });

    it('prefers the written value over the schema default for a declared slot', () => {
        const rows = paramRowSpecs([{ position: 0, value: '' }], [param(0)]);

        // Written-but-empty is not the same as unset: the slot was addressed, so it is not a gap.
        assert.deepEqual(rows, [{ position: 0, value: '', missing: false }]);
    });
});
