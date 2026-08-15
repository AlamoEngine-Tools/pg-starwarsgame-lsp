// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { markerPixels } from './missingTexture';

/** A 2x2 glyph: opaque black top-left, transparent elsewhere - the shape the logo PNG has. */
const glyph = new Uint8ClampedArray([
    0, 0, 0, 255, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 128,
]);

describe('markerPixels', () => {
    it('turns the glyph white, because a black one is invisible where it matters', () => {
        // The logo ships as black on transparent. Particles blend additively as often as not, and
        // additive black adds nothing at all - the marker would be a missing marker.
        const marker = markerPixels(glyph);

        assert.deepEqual(Array.from(marker.slice(0, 4)), [255, 255, 255, 255]);
    });

    it('keeps the glyph`s own coverage, so it stays the shape it is', () => {
        const marker = markerPixels(glyph);

        assert.equal(marker[7], 0);
        assert.equal(marker[15], 128);
    });

    it('leaves black where the glyph is not', () => {
        // Not just transparent: an additive blend ignores alpha and adds the colour anyway, so
        // anything but black outside the glyph would wash a square over the scene.
        const marker = markerPixels(glyph);

        assert.deepEqual(Array.from(marker.slice(4, 8)), [0, 0, 0, 0]);
    });

    it('does not alias a partly covered edge into a hard one', () => {
        // Half-covered stays half-covered: the logo has curves, and rounding coverage to on or off
        // would leave it jagged at the size a particle actually draws at.
        assert.equal(markerPixels(glyph)[15], 128);
    });

    it('answers with a buffer of its own, leaving the decoded image alone', () => {
        const source = new Uint8ClampedArray(glyph);
        markerPixels(source);

        assert.deepEqual(Array.from(source), Array.from(glyph));
    });
});
