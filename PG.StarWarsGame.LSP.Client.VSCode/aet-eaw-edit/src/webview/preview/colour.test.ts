// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    affiliationColour, colorizationFor, parseHex, teamColour, toHex,
} from './colour';

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

describe('colorizationFor', () => {
    const rgba = (r: number, g: number, b: number) => ({ r, g, b, a: 255 });

    it('uses the team tint when a faction is chosen', () => {
        const colour = colorizationFor(rgba(54, 134, 242), rgba(75, 75, 75));

        assert.deepEqual(colour, teamColour(rgba(54, 134, 242)));
    });

    it('wears the subject\'s own colour when no faction applies', () => {
        // The user's word: faction colour is a SKIRMISH thing, so this is the usual case. A TIE
        // Fighter is 75,75,75 whoever owns it, and the preview showed it untinted.
        const colour = colorizationFor(null, rgba(75, 75, 75));

        assert.deepEqual(colour, teamColour(rgba(75, 75, 75)));
    });

    it('leaves the texture alone when the subject declares nothing', () => {
        // Null, not white: absent means the object said nothing, and the viewport's own identity is
        // what a model with no opinion should wear.
        assert.equal(colorizationFor(null, null), null);
        assert.equal(colorizationFor(null, undefined), null);
    });

    it('honours a subject that declares pure white', () => {
        // Ten of the 25 do. White IS the identity for the multiply, so the result is the same
        // picture - but it is the object's own word rather than a fallback, and it must survive.
        assert.deepEqual(colorizationFor(null, rgba(255, 255, 255)), teamColour(rgba(255, 255, 255)));
    });
});

describe('the affiliation fallback', () => {
    const rgba = (r: number, g: number, b: number) => ({ r, g, b, a: 255 });
    const factions = [
        { name: 'Rebel', color: rgba(185, 40, 39), noColorizationColor: rgba(199, 105, 59) },
        { name: 'Empire', color: rgba(54, 134, 242), noColorizationColor: rgba(255, 255, 255) },
        { name: 'Hutts', color: rgba(213, 209, 102) },
    ];

    it('finds the owning faction uncoloured colour', () => {
        // 772 objects declare an Affiliation and only 24 carry a colour of their own, so this is
        // what most units actually wear. Rebel is the case that matters - its fallback is a real
        // orange-brown, where Empire's and Neutral's are white and change nothing.
        assert.deepEqual(affiliationColour(factions, 'Rebel'), rgba(199, 105, 59));
    });

    it('is nothing when the faction declares none', () => {
        assert.equal(affiliationColour(factions, 'Hutts'), null);
    });

    it('is nothing for an affiliation no faction matches', () => {
        // A mod can name a faction that was never defined; that is the author's mistake to see
        // elsewhere, not a reason to invent a colour here.
        assert.equal(affiliationColour(factions, 'Zann'), null);
        assert.equal(affiliationColour(factions, null), null);
    });

    it('lets the subject OWN colour win over its faction', () => {
        // A TIE is 75,75,75 whoever owns it - the object's word outranks the faction's default.
        assert.deepEqual(
            colorizationFor(null, rgba(75, 75, 75), affiliationColour(factions, 'Empire')),
            teamColour(rgba(75, 75, 75)));
    });

    it('falls back to the faction when the subject says nothing', () => {
        assert.deepEqual(
            colorizationFor(null, null, affiliationColour(factions, 'Rebel')),
            teamColour(rgba(199, 105, 59)));
    });

    it('still leaves the texture alone when neither says anything', () => {
        assert.equal(colorizationFor(null, null, null), null);
    });

    it('and a chosen faction still beats both', () => {
        assert.deepEqual(
            colorizationFor(rgba(1, 2, 3), rgba(75, 75, 75), affiliationColour(factions, 'Rebel')),
            teamColour(rgba(1, 2, 3)));
    });
});
