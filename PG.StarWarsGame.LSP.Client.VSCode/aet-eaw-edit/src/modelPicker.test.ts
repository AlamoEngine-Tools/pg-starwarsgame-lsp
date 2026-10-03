// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {modelPickItems, pickedModelName} from './modelPicker';

describe('modelPickItems', () => {
    it('labels each model by name and says where it comes from', () => {
        const items = modelPickItems([
            {name: 'ev_stardestroyer.alo', baseGame: true},
            {name: 'my_frigate.alo', baseGame: false},
        ]);

        assert.deepEqual(items.map(i => [i.label, i.description]), [
            ['ev_stardestroyer.alo', 'Base game'],
            ['my_frigate.alo', 'Project'],
        ]);
    });
});

describe('pickedModelName', () => {
    it('opens the highlighted model', () => {
        assert.equal(pickedModelName({label: 'my_frigate.alo'}, 'frig'), 'my_frigate.alo');
    });

    it('opens what was typed when nothing in the list matches', () => {
        // The list is the catalog, and the catalog can lag the disk - a model saved a moment ago
        // must still be reachable by name, as it was before the list existed.
        assert.equal(pickedModelName(undefined, '  New_Model.ALO '), 'New_Model.ALO');
    });

    it('opens nothing for an empty entry', () => {
        assert.equal(pickedModelName(undefined, '   '), null);
    });
});
