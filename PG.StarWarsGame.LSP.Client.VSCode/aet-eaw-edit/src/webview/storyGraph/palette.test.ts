// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { colourTokens } from '../shared/tokens';
import {
    BRANCH_PALETTE,
    EDGE_KINDS,
    JUNCTION_TOKEN,
    LANE_PALETTE,
    LIFECYCLE_TOKENS,
    UNKNOWN_LIFECYCLE_TOKEN,
    branchColours,
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

    /**
     * Yellow and orange were reported as undocumented, and they were: the legend was generated from
     * LIFECYCLE_TOKENS alone, which is blue/green/purple/red plus a faint swatch. Both hues live on
     * EDGES, where they mean something fixed - so the edge kinds are a palette entry now, read by
     * the stroke rules and the legend alike rather than hand-written in each.
     */
    it('names only tokens the layer defines for edges too', () => {
        for (const kind of EDGE_KINDS) {
            assert.ok(known.has(kind.token), `${kind.token} is not a defined colour token`);
        }
    });

    it('documents the two hues that were reported as unexplained', () => {
        const byToken = new Map(EDGE_KINDS.map(k => [k.token, k]));
        assert.ok(byToken.has('--colour-data-orange'), 'orange must be in the legend');
        assert.ok(byToken.has('--colour-data-yellow'), 'yellow must be in the legend');
    });

    // A swatch that does not carry the dash pattern of the edge it describes is a swatch that
    // disagrees with the graph - the exact failure the lifecycle map was extracted to prevent.
    it('gives every edge kind a label and keeps its dash pattern with it', () => {
        for (const kind of EDGE_KINDS) {
            assert.ok(kind.label.length > 0, `${kind.kind} has no label`);
            assert.equal(typeof kind.dash, 'string');
        }
        assert.equal(EDGE_KINDS.find(k => k.kind === 'Tactical')?.dash, '8 4');
        assert.equal(EDGE_KINDS.find(k => k.kind === 'Flag')?.dash, '2 4');
        assert.equal(EDGE_KINDS.find(k => k.kind === 'Prereq')?.dash, '');
    });

    it('maps each lifecycle to the colour its node border already used', () => {
        assert.equal(LIFECYCLE_TOKENS.Waiting, '--colour-data-blue');
        assert.equal(LIFECYCLE_TOKENS.Armed, '--colour-data-green');
        assert.equal(LIFECYCLE_TOKENS.Fired, '--colour-data-purple');
        assert.equal(LIFECYCLE_TOKENS.Disabled, '--colour-data-red');
    });

    // Order is load-bearing: the campaign's first branch takes the first slot, so reordering this list
    // repaints every graph.
    it('keeps the branch palette in its slot order', () => {
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

    /**
     * Issue #128, second pass. A hash of the name put 5 branches on 4 colours in Empire Act I and 14
     * on 5 in the Underworld campaign, so two branches shared a colour well before the six ran out.
     * Slots are handed out in campaign order instead - the order the server's branch facet and the
     * branch dropdown already use.
     */
    it('hands the campaign`s branches consecutive slots, in campaign order', () => {
        const colourOf = branchColours(['Act2', 'Act1', 'Act3']);

        assert.equal(colourOf('Act1'), BRANCH_PALETTE[0]);
        assert.equal(colourOf('Act2'), BRANCH_PALETTE[1]);
        assert.equal(colourOf('Act3'), BRANCH_PALETTE[2]);
    });

    it('orders by character code, the way the server sorts the facet', () => {
        // Ordinal: every capital sorts before every lower-case letter.
        const colourOf = branchColours(['alpha', 'Beta']);

        assert.equal(colourOf('Beta'), BRANCH_PALETTE[0]);
        assert.equal(colourOf('alpha'), BRANCH_PALETTE[1]);
    });

    it('gives six branches six different colours', () => {
        const names = ['A', 'B', 'C', 'D', 'E', 'F'];
        const colourOf = branchColours(names);

        assert.equal(new Set(names.map(colourOf)).size, 6);
    });

    it('cycles from the first slot once the six are used', () => {
        const colourOf = branchColours(['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H']);

        assert.equal(colourOf('G'), BRANCH_PALETTE[0]);
        assert.equal(colourOf('H'), BRANCH_PALETTE[1]);
    });

    /**
     * The point of taking the CAMPAIGN list: what a filter hides does not move anyone's slot, because
     * the list does not change when the filter does.
     */
    it('ignores duplicates and blanks, so they cannot shift a slot', () => {
        const colourOf = branchColours(['Act2', '', 'Act1', 'Act2']);

        assert.equal(colourOf('Act1'), BRANCH_PALETTE[0]);
        assert.equal(colourOf('Act2'), BRANCH_PALETTE[1]);
    });

    // A branch typed into an edit reaches the graph before the server's next facet list does.
    it('falls back to the name hash for a branch the campaign list does not name yet', () => {
        const colourOf = branchColours(['Act1']);

        assert.equal(colourOf('Brand_New'), branchToken('Brand_New'));
    });

    it('hashes a branch the same way the shipped hash did', () => {
        // Reproduces the shipped hash so a rename here cannot silently repaint the fallback.
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
