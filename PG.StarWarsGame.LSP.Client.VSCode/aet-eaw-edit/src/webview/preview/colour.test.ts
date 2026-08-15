// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { parseHex, teamColour, toHex } from './colour';

describe('toHex', () => {
    it('writes a colour the way an <input type=color> wants it', () => {
        assert.equal(toHex({ r: 255, g: 0, b: 0, a: 255 }), '#ff0000');
        assert.equal(toHex({ r: 54, g: 134, b: 242, a: 255 }), '#3686f2');
    });

    it('pads single digits, which is what breaks a naive toString(16)', () => {
        assert.equal(toHex({ r: 1, g: 2, b: 3, a: 255 }), '#010203');
    });

    it('clamps out-of-range channels rather than emitting nonsense', () => {
        assert.equal(toHex({ r: 300, g: -5, b: 128, a: 255 }), '#ff0080');
    });
});

describe('parseHex', () => {
    it('reads the form the picker produces', () => {
        assert.deepEqual(parseHex('#3686f2'), { r: 54, g: 134, b: 242, a: 255 });
    });

    it('accepts a missing hash, because pasted values often lack one', () => {
        assert.deepEqual(parseHex('ff0000'), { r: 255, g: 0, b: 0, a: 255 });
    });

    it('returns null for something that is not a colour', () => {
        assert.equal(parseHex('nonsense'), null);
        assert.equal(parseHex('#12345'), null);
    });
});

describe('teamColour', () => {
    it('normalises to 0-1 for the renderer', () => {
        const colour = teamColour({ r: 255, g: 128, b: 0, a: 128 });

        assert.equal(colour.r, 1);
        assert.ok(Math.abs(colour.g - 128 / 255) < 1e-6);
        assert.equal(colour.b, 0);
    });

    it('forces alpha to one, the way the engine does', () => {
        // RenderObject::SetColorization: "always force alpha channel to 100%". A faction whose Color
        // ships a low alpha must not come out translucent.
        assert.equal(teamColour({ r: 10, g: 20, b: 30, a: 0 }).a, 1);
    });
});
