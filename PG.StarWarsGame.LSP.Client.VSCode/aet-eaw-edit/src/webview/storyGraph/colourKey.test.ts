// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { branchKey } from './colourKey';
import { BRANCH_PALETTE, branchColours } from './palette';

const event = (branch: string | null | undefined) => ({ kind: 'Event', branch });

describe('branchKey', () => {
    /** Issue #128: a zoomed-out node is drawn in its branch colour, and nothing said which branch. */
    it('lists each branch in the graph once, with the token the graph draws it in', () => {
        const colourOf = branchColours(['Act1', 'Act2']);
        const key = branchKey([event('Act2'), event('Act1'), event('Act2')], colourOf);

        assert.deepEqual(key.map(entry => entry.branch), ['Act1', 'Act2']);
        assert.deepEqual(key.map(entry => entry.token), [BRANCH_PALETTE[0], BRANCH_PALETTE[1]]);
    });

    /** The order colours are handed out in, so the rows run through the palette in sequence. */
    it('orders branches the way the campaign list does', () => {
        const colourOf = branchColours(['beta', 'Alpha', 'Gamma']);
        const key = branchKey([event('beta'), event('Alpha'), event('Gamma')], colourOf);

        assert.deepEqual(key.map(entry => entry.branch), ['Alpha', 'Gamma', 'beta']);
    });

    /** A junction inherits its owner's branch for the glow; it names no branch of its own. */
    it('takes branches from events only, and skips an event with none', () => {
        const key = branchKey([
            { kind: 'AndJunction', branch: 'Ghost' }, event(null), event(undefined), event(''), event('Act1'),
        ], branchColours(['Act1']));

        assert.deepEqual(key.map(entry => entry.branch), ['Act1']);
    });

    it('is empty for a graph with no branches', () => {
        assert.deepEqual(branchKey([event(null)], branchColours([])), []);
    });

    /** Past six branches the slots cycle. The key has to say so - two identical swatches with
     * different names would read as a rendering fault. */
    it('names the other branches in view that share a colour', () => {
        const campaign = ['A', 'B', 'C', 'D', 'E', 'F', 'G'];
        const key = branchKey(campaign.map(event), branchColours(campaign));
        const byName = new Map(key.map(entry => [entry.branch, entry]));

        assert.deepEqual(byName.get('A')?.sharedWith, ['G']);
        assert.deepEqual(byName.get('G')?.sharedWith, ['A']);
        assert.deepEqual(byName.get('B')?.sharedWith, []);
    });

    /** A filter hides branches; it must not make the survivors look as though they share nothing. */
    it('keeps a branch`s campaign colour when the others are filtered out', () => {
        const campaign = ['A', 'B', 'C', 'D', 'E', 'F', 'G'];
        const [only] = branchKey([event('G')], branchColours(campaign));

        assert.equal(only.token, BRANCH_PALETTE[0]);
        assert.deepEqual(only.sharedWith, []);
    });
});
