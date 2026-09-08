// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    contrastRatio, deltaE2000, rgbToLab, simulateColourBlindness, reviewFactionColour,
} from './colourUsability';

const rgba = (r: number, g: number, b: number) => ({ r, g, b, a: 255 });

describe('deltaE2000', () => {
    // Sharma, Wu and Dalal's published test data for CIEDE2000. The formula has several terms that
    // are easy to get subtly wrong - the hue-difference wrap, the rotation term - and every one of
    // them still produces plausible-looking numbers, so checking against the reference pairs is the
    // only way to know it is right rather than merely reasonable.
    const pairs: [number[], number[], number][] = [
        [[50, 2.6772, -79.7751], [50, 0, -82.7485], 2.0425],
        [[50, 3.1571, -77.2803], [50, 0, -82.7485], 2.8615],
        [[50, 2.8361, -74.02], [50, 0, -82.7485], 3.4412],
        [[50, -1.3802, -84.2814], [50, 0, -82.7485], 1.0],
        [[50, -0.9009, -85.5211], [50, 0, -82.7485], 1.0],
        [[50, 0, 0], [50, -1, 2], 2.3669],
        [[50, 2.49, -0.001], [50, -2.49, 0.0009], 7.1792],
        [[60.2574, -34.0099, 36.2677], [60.4626, -34.1751, 39.4387], 1.2644],
        [[2.0776, 0.0795, -1.135], [0.9033, -0.0636, -0.5514], 0.9082],
    ];

    for (const [a, b, expected] of pairs) {
        it(`matches the reference for ${expected}`, () => {
            const actual = deltaE2000(
                { l: a[0], a: a[1], b: a[2] }, { l: b[0], a: b[1], b: b[2] });

            assert.ok(Math.abs(actual - expected) < 0.0002,
                `expected ${expected}, got ${actual}`);
        });
    }

    it('is zero for a colour against itself', () => {
        const lab = rgbToLab(rgba(54, 134, 242));
        assert.equal(deltaE2000(lab, lab), 0);
    });
});

describe('rgbToLab', () => {
    it('maps white and black to the ends of the lightness axis', () => {
        assert.ok(Math.abs(rgbToLab(rgba(255, 255, 255)).l - 100) < 0.01);
        assert.ok(Math.abs(rgbToLab(rgba(0, 0, 0)).l) < 0.01);
    });

    it('leaves grey with no chroma', () => {
        const grey = rgbToLab(rgba(128, 128, 128));

        assert.ok(Math.abs(grey.a) < 0.01);
        assert.ok(Math.abs(grey.b) < 0.01);
    });
});

describe('contrastRatio', () => {
    it('gives the WCAG extremes', () => {
        assert.ok(Math.abs(contrastRatio(rgba(255, 255, 255), rgba(0, 0, 0)) - 21) < 0.01);
        assert.equal(contrastRatio(rgba(50, 50, 50), rgba(50, 50, 50)), 1);
    });

    it('does not care which colour is given first', () => {
        const a = contrastRatio(rgba(20, 40, 60), rgba(200, 200, 200));
        const b = contrastRatio(rgba(200, 200, 200), rgba(20, 40, 60));

        assert.ok(Math.abs(a - b) < 1e-9);
    });
});

describe('simulateColourBlindness', () => {
    it('collapses red and green for a deuteranope', () => {
        // The whole point of the check: two colours a trichromat separates easily can land on top of
        // each other here.
        const red = simulateColourBlindness(rgba(220, 40, 40), 'deuteranopia');
        const green = simulateColourBlindness(rgba(40, 180, 40), 'deuteranopia');

        assert.ok(deltaE2000(rgbToLab(red), rgbToLab(green))
            < deltaE2000(rgbToLab(rgba(220, 40, 40)), rgbToLab(rgba(40, 180, 40))));
    });

    it('leaves grey alone, whichever kind', () => {
        for (const kind of ['protanopia', 'deuteranopia', 'tritanopia'] as const) {
            const grey = simulateColourBlindness(rgba(128, 128, 128), kind);

            assert.ok(Math.abs(grey.r - 128) < 3, `${kind} r=${grey.r}`);
            assert.ok(Math.abs(grey.g - 128) < 3, `${kind} g=${grey.g}`);
            assert.ok(Math.abs(grey.b - 128) < 3, `${kind} b=${grey.b}`);
        }
    });

    it('keeps every channel inside the byte range', () => {
        for (const kind of ['protanopia', 'deuteranopia', 'tritanopia'] as const) {
            const extreme = simulateColourBlindness(rgba(255, 0, 255), kind);

            for (const channel of [extreme.r, extreme.g, extreme.b]) {
                assert.ok(channel >= 0 && channel <= 255, `${kind}: ${channel}`);
            }
        }
    });
});

describe('reviewFactionColour', () => {
    // The vanilla data is the fixture: an analyser that does not flag these is wrong.
    const empire = { name: 'Empire', color: rgba(54, 134, 242) };
    const imperial = { name: 'Imperial', color: rgba(54, 134, 242) };
    const rebel = { name: 'Rebel', color: rgba(190, 40, 40) };

    it('flags two factions that ship the very same colour', () => {
        const findings = reviewFactionColour(empire.color, 'Empire', [imperial, rebel]);

        assert.ok(findings.some(f => f.message.includes('Imperial')),
            `expected Imperial to be flagged, got ${JSON.stringify(findings)}`);
    });

    it('says which faction and why, not just a number', () => {
        const message = reviewFactionColour(empire.color, 'Empire', [imperial])
            .map(f => f.message).join(' ');

        assert.match(message, /Imperial/);
        assert.doesNotMatch(message, /^\d/);
    });

    it('does not flag a colour against itself', () => {
        const findings = reviewFactionColour(empire.color, 'Empire', [empire, rebel]);

        assert.ok(!findings.some(f => f.message.includes('Empire is')),
            'a faction cannot collide with itself');
    });

    it('catches a pair that only collapses under colour blindness', () => {
        // Distinct to a trichromat, indistinguishable to a deuteranope - the case the naked eye and
        // a plain RGB comparison both miss.
        const findings = reviewFactionColour(
            rgba(150, 120, 0), 'Gold', [{ name: 'Olive', color: rgba(110, 135, 0) }]);

        assert.ok(findings.some(f => /deuteranopia|protanopia|tritanopia/.test(f.message)),
            `expected a colour-blindness finding, got ${JSON.stringify(findings)}`);
    });

    it('warns when a colour disappears into the space backdrop', () => {
        const findings = reviewFactionColour(rgba(8, 8, 12), 'Void', []);

        assert.ok(findings.some(f => /backdrop|space/i.test(f.message)),
            `expected a contrast finding, got ${JSON.stringify(findings)}`);
    });

    it('does not flag a colour that only differs from the ground in hue', () => {
        // Amber has nearly the luminance of daylight terrain and is still impossible to miss.
        // Judging that on WCAG contrast alone - a text measure, blind to hue - is a false positive,
        // and a checker that cries wolf gets switched off.
        const findings = reviewFactionColour(rgba(240, 160, 20), 'Amber', []);

        assert.ok(!findings.some(f => /terrain/.test(f.message)),
            `expected no terrain finding, got ${JSON.stringify(findings)}`);
    });

    it('says nothing about a colour that is fine', () => {
        assert.deepEqual(reviewFactionColour(rgba(240, 160, 20), 'Amber', [
            { name: 'Blue', color: rgba(30, 80, 220) },
        ]), []);
    });
});
