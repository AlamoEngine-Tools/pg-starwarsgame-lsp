// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { canvasEdgeStyle, MUTED_EDGE_TOKEN } from './canvasEdgeStyle';
import { BRANCH_PALETTE, EDGE_KINDS, branchColours } from './palette';

const colourOf = branchColours(['Act1', 'Act2']);
const kindToken = (kind: string): string => EDGE_KINDS.find(k => k.kind === kind)!.token;

describe('canvasEdgeStyle', () => {
    /**
     * Reported 2026-09-15: branch colours only showed while filtered to a branch. Past 60 nodes the
     * graph is windowed and every edge was one muted canvas path, so the branch strand - the colour
     * that is most visible on a small graph - vanished on every real campaign.
     */
    it('strokes a Requires edge into a branch in that branch`s colour', () => {
        const style = canvasEdgeStyle('Prereq', 'Act2', colourOf);

        assert.equal(style.token, BRANCH_PALETTE[1]);
        assert.deepEqual(style.dash, []);
    });

    it('leaves a Requires edge with no branch muted', () => {
        assert.equal(canvasEdgeStyle('Prereq', null, colourOf).token, MUTED_EDGE_TOKEN);
    });

    /** The same edge kinds the mounted view colours, so zooming in does not change what an edge says. */
    it('gives every other kind its EDGE_KINDS colour and dash', () => {
        const control = canvasEdgeStyle('Control', null, colourOf);
        const tactical = canvasEdgeStyle('Tactical', null, colourOf);
        const flag = canvasEdgeStyle('Flag', null, colourOf);

        assert.equal(control.token, kindToken('Control'));
        assert.deepEqual(control.dash, []);
        assert.equal(tactical.token, kindToken('Tactical'));
        assert.deepEqual(tactical.dash, [8, 4]);
        assert.equal(flag.token, kindToken('Flag'));
        assert.deepEqual(flag.dash, [2, 4]);
    });

    it('draws TacticalEntry as Tactical, as the mounted view does', () => {
        assert.deepEqual(canvasEdgeStyle('TacticalEntry', null, colourOf), canvasEdgeStyle('Tactical', null, colourOf));
    });

    /** The branch colours Requires edges only, as on a mounted connection. */
    it('does not let a branch recolour a kind that has its own colour', () => {
        assert.equal(canvasEdgeStyle('Control', 'Act1', colourOf).token, kindToken('Control'));
    });

    it('leaves a kind it does not know muted', () => {
        assert.equal(canvasEdgeStyle('LuaLink', null, colourOf).token, MUTED_EDGE_TOKEN);
    });

    /**
     * The draw pass buckets edges by this key and strokes each bucket once, which is what keeps a
     * coloured overview as cheap as the single grey path it replaced.
     */
    it('gives edges drawn alike the same key, and different strokes different keys', () => {
        assert.equal(canvasEdgeStyle('Prereq', 'Act1', colourOf).key, canvasEdgeStyle('Prereq', 'Act1', colourOf).key);
        assert.notEqual(canvasEdgeStyle('Prereq', 'Act1', colourOf).key, canvasEdgeStyle('Prereq', 'Act2', colourOf).key);
        assert.notEqual(canvasEdgeStyle('Tactical', null, colourOf).key, canvasEdgeStyle('Prereq', null, colourOf).key);
    });
});
