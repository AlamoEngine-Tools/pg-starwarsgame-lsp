// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';
import {drawsPopulationBlip} from './encyclopediaBlip';

describe('drawsPopulationBlip', () => {
    it('draws for a real population', () => {
        assert.equal(drawsPopulationBlip(1), true);
        assert.equal(drawsPopulationBlip(12), true);
    });

    // The one this was written for. A card for an object whose Population_Value is 0 was drawing a
    // blip reading "0", which the game never shows: its own branch is "if (population != 0)", and
    // a zero also selects the band variant with NO disc baked into it. Caught by screenshotting a
    // real object, not by reasoning about the card.
    it('draws nothing for a population of zero', () => {
        assert.equal(drawsPopulationBlip(0), false);
    });

    it('draws nothing when the object declares no population at all', () => {
        assert.equal(drawsPopulationBlip(null), false);
        assert.equal(drawsPopulationBlip(undefined), false);
    });

    // A negative value is still a population - the game draws it, just in a different colour - so
    // it must not be swept up by the zero rule.
    it('draws for a negative population', () => {
        assert.equal(drawsPopulationBlip(-3), true);
    });
});
