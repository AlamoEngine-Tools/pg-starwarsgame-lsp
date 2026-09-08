// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { FIRST_TAKE_WEIGHT, pickTake, takeWeights } from './takeRoulette';
import { type AnimationTake } from './animationNames';

const take = (n: number): AnimationTake => ({ name: `x_idle_0${n}.ala`, take: n, label: `0${n}` });

describe('takeWeights', () => {
    it('gives the first take a heavier share than the rest', () => {
        // The reader: the game "switches between the animations randomly for each repeat, favouring
        // the _00 animation slightly". How MUCH is not something this project has measured - the
        // weight is chosen, and it is one constant to change if the engine is ever read.
        const weights = takeWeights([take(0), take(1), take(2)]);

        assert.equal(weights[0], FIRST_TAKE_WEIGHT);
        assert.deepEqual(weights.slice(1), [1, 1]);
    });

    it('favours take zero wherever it sits in the list', () => {
        // 7 of the 1718 shipped model+action pairs do not start at take 0, so position is not the
        // same question as number.
        assert.deepEqual(takeWeights([take(1), take(0)]), [1, FIRST_TAKE_WEIGHT]);
    });

    it('favours nothing when the action ships no take zero', () => {
        assert.deepEqual(takeWeights([take(1), take(2)]), [1, 1]);
    });
});

describe('pickTake', () => {
    const three = [take(0), take(1), take(2)];

    it('picks the first take across its heavier share of the roll', () => {
        // Weights 2, 1, 1 over a total of 4: take 0 owns the first half.
        assert.equal(pickTake(three, 0).take, 0);
        assert.equal(pickTake(three, 0.49).take, 0);
    });

    it('picks the others across theirs', () => {
        assert.equal(pickTake(three, 0.5).take, 1);
        assert.equal(pickTake(three, 0.74).take, 1);
        assert.equal(pickTake(three, 0.75).take, 2);
        assert.equal(pickTake(three, 0.99).take, 2);
    });

    it('never falls off the end of a roll that reaches one', () => {
        // `Math.random` cannot return 1, but a caller passing a clamped value should not be handed
        // undefined - a clip that fails to play is a worse answer than the last take.
        assert.equal(pickTake(three, 1).take, 2);
    });

    it('returns the only take there is', () => {
        assert.equal(pickTake([take(0)], 0.9).take, 0);
    });

    it('is the take itself, so the caller has the filename to load', () => {
        assert.equal(pickTake(three, 0).name, 'x_idle_00.ala');
    });

    it('favours take zero over a long run', () => {
        // The property the weight exists for, asserted as a property rather than as a magic number:
        // over an even sweep of the roll, take zero comes up more often than either other take and
        // the other two come up equally.
        const rolls = Array.from({ length: 1000 }, (_, at) => at / 1000);
        const tally = [0, 0, 0];

        for (const roll of rolls) {
            tally[pickTake(three, roll).take!] += 1;
        }

        assert.ok(tally[0] > tally[1], `${tally.join(', ')}`);
        assert.equal(tally[1], tally[2]);
    });
});
