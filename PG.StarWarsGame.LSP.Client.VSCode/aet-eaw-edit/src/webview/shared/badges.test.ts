// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { badgeIcon, type BadgeSeverity } from './badges';

describe('badgeIcon', () => {
    it('gives each severity its own mark, not just its own colour', () => {
        // Colour alone is not a signal: a reader who cannot separate yellow from grey has to be
        // able to see that something changed.
        const marks = (['info', 'warning', 'error'] as BadgeSeverity[]).map(badgeIcon);

        assert.equal(new Set(marks).size, 3);
    });

    it('stays quiet for plain explanation', () => {
        // The same neutral mark a row's details button carries, so an explanation reads as an
        // explanation rather than as something needing attention.
        assert.equal(badgeIcon('info'), 'details');
    });
});
