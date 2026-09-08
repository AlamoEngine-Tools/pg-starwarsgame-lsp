// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { colourTokens } from '../shared/tokens';
import {
    BRANCH_PALETTE,
    JUNCTION_TOKEN,
    LANE_PALETTE,
    LIFECYCLE_TOKENS,
    UNKNOWN_LIFECYCLE_TOKEN,
    branchToken,
    laneToken,
} from './palette';

const known = new Set(colourTokens.map(t => t.name));

describe('the story graph palettes', () => {
    /**
     * The reason this module exists. The same six chart colours were written out four times: the
     * lifecycle border rules, the legend swatches beside them, a hex mirror for the LOD canvas, and
     * the branch palette in both a var and a hex form - the last pair carrying a comment asking the
     * next reader to keep them in the same order by hand. Nothing checked that they agreed.
     */
    it('names only tokens the layer actually defines', () => {
        const every = [
            ...BRANCH_PALETTE,
            ...LANE_PALETTE,
            ...Object.values(LIFECYCLE_TOKENS),
            JUNCTION_TOKEN,
            UNKNOWN_LIFECYCLE_TOKEN,
        ];

        for (const token of every) {
            assert.ok(known.has(token), `${token} is not a defined colour token`);
        }
    });

    it('maps each lifecycle to the colour its node border already used', () => {
        assert.equal(LIFECYCLE_TOKENS.Waiting, '--colour-data-blue');
        assert.equal(LIFECYCLE_TOKENS.Armed, '--colour-data-green');
        assert.equal(LIFECYCLE_TOKENS.Fired, '--colour-data-purple');
        assert.equal(LIFECYCLE_TOKENS.Disabled, '--colour-data-red');
    });

    // Order is load-bearing: a branch keeps its colour across sessions only because the hash indexes
    // this list, so reordering it repaints every graph.
    it('keeps the branch palette in the order the hash already indexed', () => {
        assert.deepEqual(BRANCH_PALETTE, [
            '--colour-data-blue',
            '--colour-data-green',
            '--colour-data-orange',
            '--colour-data-purple',
            '--colour-data-red',
            '--colour-data-yellow',
        ]);
    });

    /**
     * Seven lanes against six published chart hues. The count is kept at seven so the modulus does
     * not move and the first five lanes keep exactly the colour they have today; only the sixth and
     * seventh change, from a teal and a pink that were never theme-backed.
     */
    it('keeps seven lane slots so existing lane colours do not shift', () => {
        assert.equal(LANE_PALETTE.length, 7);
        assert.deepEqual(LANE_PALETTE.slice(0, 5), [
            '--colour-data-blue',
            '--colour-data-green',
            '--colour-data-orange',
            '--colour-data-purple',
            '--colour-data-yellow',
        ]);
    });

    it('gives a branch the same slot the old hash gave it', () => {
        // Reproduces the shipped hash so a rename here cannot silently repaint a graph.
        const legacy = (branch: string): string => {
            let hash = 0;
            for (let i = 0; i < branch.length; i++) { hash = (hash * 31 + branch.charCodeAt(i)) | 0; }
            return BRANCH_PALETTE[Math.abs(hash) % BRANCH_PALETTE.length];
        };

        for (const branch of ['main', 'Rebel_Intro', 'act2', '', 'a', 'zzzzzzzzzzzz']) {
            assert.equal(branchToken(branch), legacy(branch), branch);
        }
    });

    it('gives a lane the same slot the old hash gave it', () => {
        const legacy = (key: string): string => {
            let hash = 0;
            for (let i = 0; i < key.length; i++) { hash = (hash * 31 + key.charCodeAt(i)) >>> 0; }
            return LANE_PALETTE[hash % LANE_PALETTE.length];
        };

        for (const key of ['thread:one', 'chapter:two', 'thread:', 'x']) {
            assert.equal(laneToken(key), legacy(key), key);
        }
    });

    it('has no raw hex left in it', () => {
        const every = [...BRANCH_PALETTE, ...LANE_PALETTE, ...Object.values(LIFECYCLE_TOKENS)];
        for (const token of every) {
            assert.ok(!token.includes('#'), token);
        }
    });
});
