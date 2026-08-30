// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What each shot actually did, in the order it happened.
//
// The user asked for it in the shape an old-school RPG reports a hit: "X did Y amount of damage to Z
// (Armour_Type)". That last bracket is the point of it - the armour is what SCALED the number, and a
// panel that shows a pool going down without saying which factor applied leaves the one question a
// modder is actually asking unanswered.
//
// The numbers here are MEASURED, not recomputed: the panel logs the difference a shot made to a pool
// or a hardpoint's health. Recomputing them would be a second implementation of the damage rules
// able to disagree with the one that did the work.

/** How a pool is named in a line. The hull is the default and goes unsaid. */
export type DamagePool = 'hull' | 'shield' | 'energy';

/** One thing one shot did to one target. */
export interface DamageLogEntry {
    /** What hit it - the damage type, or the projectile where one was picked. */
    source: string;
    /** How much actually landed, after the armour factor. */
    amount: number;
    /** The hardpoint's id, or the unit itself. */
    target: string;
    /** The armour column that scaled it, or null where the target declares none. */
    armor: string | null;
    pool: DamagePool;
    /** This shot finished it off. */
    destroyed: boolean;
}

/** How many lines the log keeps. */
export const LOG_LIMIT = 200;

/**
 * What a hardpoint destroyed from its own card was killed BY.
 *
 * The switch on a card is not a weapon - it simply decides the hardpoint is gone - so there is no
 * damage type to name and no armour factor that applied. An easter egg the user asked for by name,
 * and one constant to change if it wears thin.
 */
export const BY_HAND = 'The Force';

/** One line, as the user asked for it. */
export function damageLine(entry: DamageLogEntry): string {
    // A shot that got through nothing still gets a line. Silence there reads as a broken button:
    // the reader pressed Fire and the panel said nothing at all.
    //
    // INFINITE is said in words. `Infinity` is what the number formats to and reads as a bug, and
    // the symbol is not ASCII - the panel's strings are.
    const did = Number.isFinite(entry.amount)
        ? entry.amount > 0
            ? `did ${round(entry.amount)} damage`
            : 'did no damage'
        : 'did infinite damage';

    // The hull is the ordinary case and goes unsaid; the shield stopping a bolt is a different
    // event and worth naming.
    const where = entry.pool === 'hull' ? '' : ` to the ${entry.pool}`;
    const armor = entry.armor === null || entry.armor === '' ? '' : ` (${entry.armor})`;
    const finished = entry.destroyed ? ' - destroyed' : '';

    return `${entry.source} ${did}${where} to ${entry.target}${armor}${finished}`;
}

/**
 * The log with one shot's victims added, newest shot first.
 *
 * Within a shot the victims keep the order the blast reached them - nearest out - because that is
 * the order the damage was applied in and reading it backwards would misdescribe a falloff.
 */
export function appendShot(
    log: readonly DamageLogEntry[], shot: readonly DamageLogEntry[],
): DamageLogEntry[] {
    return [...shot, ...log].slice(0, LOG_LIMIT);
}

/** The same rounding the pool readout uses: an armor factor of 0.35 leaves a tail nobody wants. */
function round(value: number): string {
    return Number.parseFloat(value.toFixed(1)).toString();
}
