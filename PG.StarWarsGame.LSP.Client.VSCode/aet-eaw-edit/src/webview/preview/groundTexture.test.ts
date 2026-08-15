// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { GROUND_TEXTURE_SIZE, concreteNoise } from './groundTexture';

const at = (pixels: Uint8ClampedArray, x: number, y: number): number =>
    pixels[(y * GROUND_TEXTURE_SIZE + x) * 4];

describe('concreteNoise', () => {
    it('tiles seamlessly, or the ground shows a grid of seams', () => {
        // The floor repeats this across a plane wider than the model, so an edge that does not meet
        // its opposite draws a hard line every tile - which reads as a defect in the scene rather
        // than as a surface.
        const pixels = concreteNoise();
        const last = GROUND_TEXTURE_SIZE - 1;

        for (let y = 0; y < GROUND_TEXTURE_SIZE; y += 16) {
            assert.ok(Math.abs(at(pixels, 0, y) - at(pixels, last, y)) <= 12,
                `row ${y} does not meet its opposite edge`);
        }

        for (let x = 0; x < GROUND_TEXTURE_SIZE; x += 16) {
            assert.ok(Math.abs(at(pixels, x, 0) - at(pixels, x, last)) <= 12,
                `column ${x} does not meet its opposite edge`);
        }
    });

    it('stays near white, because both maps it feeds are multipliers', () => {
        // A mid-grey tile took the floor five times darker than its own colour and halved its
        // roughness into a sheen. The grain has to modulate the material, not replace it - and
        // still never reach white, which would drag the bloom threshold along with it.
        const pixels = concreteNoise();

        for (let at2 = 0; at2 < pixels.length; at2 += 4) {
            assert.ok(pixels[at2] >= 180 && pixels[at2] <= 248,
                `value ${pixels[at2]} is outside the band`);
        }
    });

    it('is grey, so the floor takes its colour from the material', () => {
        const pixels = concreteNoise();

        for (let at2 = 0; at2 < 4000; at2 += 4) {
            assert.equal(pixels[at2], pixels[at2 + 1]);
            assert.equal(pixels[at2], pixels[at2 + 2]);
            assert.equal(pixels[at2 + 3], 255);
        }
    });

    it('actually varies, or it is just a flat colour again', () => {
        const pixels = concreteNoise();
        const values = new Set<number>();

        for (let at2 = 0; at2 < pixels.length; at2 += 4) {
            values.add(pixels[at2]);
        }

        assert.ok(values.size > 40, `only ${values.size} distinct values`);
    });

    it('is the same every time, so a redraw does not reshuffle the floor', () => {
        assert.deepEqual(concreteNoise(), concreteNoise());
    });
});
