// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {handoffFor} from './handoff';

describe('handoffFor', () => {
    it('offers AloViewer for a model, and only AloViewer', () => {
        const handoff = handoffFor('Model', {path: 'C:/Game/Data/Art/Models/HULL.ALO', packed: false});

        assert.equal(handoff?.tool, 'model');
        assert.equal(handoff?.disabledReason, undefined);
    });

    it('offers the Particle Editor for a particle system', () => {
        // The bug this replaces: the particle button sat beside the model one on MODELS, and the
        // whole hand-off was hidden on particle scenes - the one place it belonged.
        const handoff = handoffFor('Particle', {path: 'C:/Game/Data/Art/Models/P_SMOKE.ALO', packed: false});

        assert.equal(handoff?.tool, 'particles');
        assert.equal(handoff?.disabledReason, undefined);
    });

    it('offers nothing for an assembled object', () => {
        assert.equal(handoffFor('Object', null), null);
    });

    it('disables the hand-off for a file packed in a MEG archive, and says why', () => {
        const handoff = handoffFor('Model', {path: null, packed: true});

        assert.equal(handoff?.tool, 'model');
        assert.equal(handoff?.disabledReason, 'Packed in a MEG archive');
    });

    it('disables the hand-off for a model that resolved nowhere', () => {
        const handoff = handoffFor('Model', null);

        assert.equal(handoff?.tool, 'model');
        assert.equal(handoff?.disabledReason, 'No file to open');
    });
});
