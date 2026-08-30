// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Picking which TAKE of an action to play.
//
// 385 of the 1718 shipped model+action pairs carry more than one recording - nine idles on the
// infantry - and the engine does not play them in order. The reader's account: it "picks random
// animations from the animation index ... and switches between the animations randomly for each
// repeat, favouring the _00 animation slightly".
//
// So the action's play button is a roulette: press it and a take is drawn, and each time the clip
// comes round again another is drawn. Listing the takes flat, as sibling rows, hid that entirely -
// it read as nine idles rather than as one idle with nine recordings.
//
// The roll comes in from the caller rather than being taken here, so the whole thing is a pure
// function of a number and the panel is the only part that needs `Math.random`.

import { type AnimationTake } from './animationNames';

/**
 * How much more often take zero comes up than any other.
 *
 * CHOSEN, not measured. The engine's actual weighting is not something this project has read out of
 * the binary; what is known is the reader's word that `_00` is favoured "slightly". Two is the
 * simplest thing that is clearly a favour and clearly not a rule - on a three-take idle it is
 * 50/25/25 - and it is one constant to change if the engine is ever read.
 */
export const FIRST_TAKE_WEIGHT = 2;

/**
 * The weight of each take, in the order given.
 *
 * By take NUMBER, not by position: 7 of the shipped pairs do not start at zero, and on those the
 * first row in the list is not the favoured recording.
 */
export function takeWeights(takes: readonly AnimationTake[]): number[] {
    return takes.map(entry => (entry.take === 0 ? FIRST_TAKE_WEIGHT : 1));
}

/**
 * Draws one take.
 *
 * @param roll A number in [0, 1) - `Math.random()` at the call site. Taken as an argument so this
 *     stays a pure function; a roll of exactly 1 still yields the last take rather than nothing,
 *     because a clip that fails to play is a worse answer than a slightly wrong one.
 */
export function pickTake(takes: readonly AnimationTake[], roll: number): AnimationTake {
    const weights = takeWeights(takes);
    const total = weights.reduce((sum, weight) => sum + weight, 0);

    let at = roll * total;

    for (let index = 0; index < takes.length; index++) {
        at -= weights[index];

        if (at < 0) {
            return takes[index];
        }
    }

    return takes[takes.length - 1];
}
