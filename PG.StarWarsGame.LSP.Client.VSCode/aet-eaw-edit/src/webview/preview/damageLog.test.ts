// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { BY_HAND, LOG_LIMIT, appendShot, damageLine, type DamageLogEntry } from './damageLog';

const entry = (over: Partial<DamageLogEntry> = {}): DamageLogEntry => ({
    source: 'Damage_Turbolaser',
    amount: 35,
    target: 'HP_Weapon_FL',
    armor: 'Armor_Star_Destroyer',
    pool: 'hull',
    destroyed: false,
    ...over,
});

describe('damageLine', () => {
    /**
     * The user's own shape for it: *"a log that says X did Y amount of damage to Z
     * (Armour_Type)"*, the way an old-school RPG reports a hit.
     */
    it('says who did how much to what, and what armour scaled it', () => {
        assert.equal(
            damageLine(entry()),
            'Damage_Turbolaser did 35 damage to HP_Weapon_FL (Armor_Star_Destroyer)');
    });

    /** A target that declares no armour is not "(null)" - the brackets go with it. */
    it('leaves the brackets off where the target declares no armour', () => {
        assert.equal(
            damageLine(entry({ armor: null })),
            'Damage_Turbolaser did 35 damage to HP_Weapon_FL');
    });

    /**
     * An armor factor of 0.35 leaves a long tail nobody wants to read; the pools round the same way.
     */
    it('rounds the way the pool readout does', () => {
        assert.match(damageLine(entry({ amount: 12.250000001 })), /did 12\.3 damage/);
    });

    /** The shield stopping a bolt is a different event from the hull taking it. */
    it('says which pool absorbed it when it was not the hull', () => {
        assert.match(damageLine(entry({ pool: 'shield' })), /to the shield/i);
    });

    /** A shot that finishes something says so - that is the line a reader is looking for. */
    it('marks the shot that destroyed the target', () => {
        assert.match(damageLine(entry({ destroyed: true })), /destroyed/i);
    });

    /**
     * Destroying a hardpoint by hand, from its own card.
     *
     * The user asked for it as an easter egg: the switch on a card is not a weapon, it just decides
     * the hardpoint is gone - so the log says so with infinite damage. It went unlogged entirely
     * before, which made the log a record of SHOTS rather than of what happened to the unit.
     */
    it('logs a hand-destroyed hardpoint as infinite damage', () => {
        const line = damageLine(entry({
            source: BY_HAND, amount: Number.POSITIVE_INFINITY, destroyed: true,
        }));

        assert.match(line, /did infinite damage/);
        assert.match(line, /destroyed/);
        assert.doesNotMatch(line, /Infinity/);
    });

    /** Nothing got through: worth a line, because "no line at all" reads as a broken button. */
    it('says so when a shot does nothing rather than logging a silent zero', () => {
        assert.match(damageLine(entry({ amount: 0 })), /no damage/i);
    });
});

describe('appendShot', () => {
    it('puts the newest first, so the last shot is the one you read', () => {
        const log = appendShot([entry({ target: 'A' })], [entry({ target: 'B' })]);

        assert.deepEqual(log.map(e => e.target), ['B', 'A']);
    });

    it('keeps every victim of one shot, in the order the blast reached them', () => {
        const log = appendShot([], [entry({ target: 'A' }), entry({ target: 'B' })]);

        assert.deepEqual(log.map(e => e.target), ['A', 'B']);
    });

    /** A reader firing repeatedly must not grow an unbounded array behind the panel. */
    it('keeps the log bounded', () => {
        let log: DamageLogEntry[] = [];

        for (let i = 0; i < LOG_LIMIT + 40; i++) {
            log = appendShot(log, [entry({ target: `HP_${i}` })]);
        }

        assert.equal(log.length, LOG_LIMIT);
        assert.equal(log[0].target, `HP_${LOG_LIMIT + 39}`);
    });

    it('does not mutate the log it is given', () => {
        const before: DamageLogEntry[] = [entry()];
        appendShot(before, [entry({ target: 'B' })]);

        assert.equal(before.length, 1);
    });
});
