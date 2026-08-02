// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { generateStars } from './starfield';

describe('generateStars', () => {
    it('makes as many stars as asked for', () => {
        assert.equal(generateStars(300).length, 300);
    });

    it('keeps every star inside the field', () => {
        for (const star of generateStars(500)) {
            assert.ok(star.x >= 0 && star.x <= 100, `x was ${star.x}`);
            assert.ok(star.y >= 0 && star.y <= 100, `y was ${star.y}`);
        }
    });

    it('varies size and brightness', () => {
        const stars = generateStars(300);

        assert.ok(new Set(stars.map(s => s.size)).size > 1, 'every star is the same size');
        assert.ok(new Set(stars.map(s => s.opacity)).size > 10, 'every star is equally bright');
    });

    /** A reshuffled sky on every render would twinkle as the crawl scrolled. */
    it('is the same sky for the same seed', () => {
        assert.deepEqual(generateStars(50, 7), generateStars(50, 7));
    });

    it('is a different sky for a different seed', () => {
        assert.notDeepEqual(generateStars(50, 7), generateStars(50, 8));
    });

    /**
     * The failing property of the tiled-gradient version it replaces: the same handful of stars
     * recurred on a fixed pitch. Positions must not cluster onto a lattice.
     */
    it('does not repeat on a grid', () => {
        const stars = generateStars(400);
        const columns = new Set(stars.map(s => Math.round(s.x)));

        assert.ok(columns.size > 60, `stars occupied only ${columns.size} distinct columns`);
    });

    it('spreads over the whole height rather than bunching', () => {
        const stars = generateStars(400);
        const bands = new Set(stars.map(s => Math.floor(s.y / 10)));

        assert.equal(bands.size, 10, `stars reached only ${bands.size} of 10 horizontal bands`);
    });
});
