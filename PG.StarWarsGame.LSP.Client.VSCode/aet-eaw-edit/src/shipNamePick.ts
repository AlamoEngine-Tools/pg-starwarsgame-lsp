// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Picks which of an object's ship names the card shows.
 *
 * The server deliberately does not do this. It reports the pool; WHICH name to show is a
 * presentation choice with a lifetime, and the panel owns that lifetime - a pick made per request
 * would reshuffle the card on every refresh (retargeting, the MP toggle, an edit), which reads as
 * flicker rather than as the engine's behaviour.
 *
 * The engine picks at random and remembers which names it has spent so a fleet never repeats one.
 * A preview has no campaign to remember for, so this only ever picks.
 */
export function pickShipName(names: readonly string[], random: () => number = Math.random): string | null {
    if (names.length === 0) { return null; }
    // Guard the index rather than trusting the generator: a random() at exactly 1 would run off
    // the end, and callers may pass a seeded generator in tests.
    const index = Math.min(names.length - 1, Math.max(0, Math.floor(random() * names.length)));
    return names[index];
}
