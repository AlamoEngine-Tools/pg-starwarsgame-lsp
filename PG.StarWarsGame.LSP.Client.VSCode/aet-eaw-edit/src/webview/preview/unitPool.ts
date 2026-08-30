// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a unit has left, and what it takes to finish it.
//
// The rules, as the user gave them - they own the engine, this file owns the arithmetic:
//
//   - A unit WITH hardpoints cannot be targeted at all. Only its hardpoints can.
//   - It dies when ALL of them are dead, including ones that are untargetable but destructible.
//   - A unit WITHOUT hardpoints takes damage on its own shield / energy / health pool.
//
// The one thing NOBODY knows is how the engine ties the two health pools together.
// `Hull_Vs_Hard_Points_Health_Constraint` (Gameconstants.xml, shipped at 0.2) is the constant, and
// its own comment says only "how closely to tie hull vs. hard point healths together in relation to
// one another". The community never worked out what it computes. Most mods simply author the unit's
// health as the SUM of its hardpoints, and that seems to work - so that is the convention drawn
// here. It is a convention, not a derivation, and it is not dressed up as one.

import type { PreviewHardpoint, PreviewTargetDefence } from '../../protocol/modelPreview';

/** Health left per hardpoint, keyed by hardpoint id. Null where the hardpoint declares none. */
export type HardpointHealth = Readonly<Record<string, number | null>>;

/** The unit's hull bar. */
export interface HullPool {
    current: number;
    max: number;
    /**
     * Whether this is the SUM of the hardpoints rather than the unit's own `Tactical_Health`.
     *
     * Worth saying out loud in the panel: the two numbers disagree in the shipped data - the Star
     * Destroyer resolves to 7500 of `Tactical_Health` against 2850 summed over its ten hardpoints - so
     * a reader seeing 2850 where the XML says 7500 needs to know which one they are looking at.
     */
    fromHardpoints: boolean;
}

/**
 * The hull pool, from whichever source actually governs this unit.
 *
 * A hardpoint with no declared `Health` contributes nothing to either end of the bar. 210 of foc's
 * hardpoints declare none, and they are INDESTRUCTIBLE rather than already dead - counting a null
 * as a zero would open every such ship at less than full strength.
 */
export function hullPool(
    defence: PreviewTargetDefence | null | undefined,
    hardpoints: readonly PreviewHardpoint[],
    hardpointHealth: HardpointHealth,
    destroyed: ReadonlySet<string> = new Set(),
    hullNow?: number | null,
): HullPool {
    const destructible = hardpoints.filter(h => h.isDestroyable);

    if (destructible.length === 0) {
        // What the unit has LEFT, not what it was built with. This answered `tacticalHealth` for
        // both ends of the bar, so a reader could empty the pool with the attacker panel and watch
        // a full hull bar throughout: the number that depletes and the number that is drawn were
        // two different numbers, and only one of them was on screen.
        //
        // Undefined means nothing has fired yet - a preview that has just opened - so the declared
        // health stands. Clamped at both ends for the same reason the hardpoint sum below is: an
        // overkill shot must not draw a negative bar, and a repair that overshoots must not report
        // a ship in better condition than it was built in.
        const max = defence?.tacticalHealth ?? 0;

        return {
            current: hullNow === undefined || hullNow === null
                ? max
                : Math.min(Math.max(hullNow, 0), max),
            max,
            fromHardpoints: false,
        };
    }

    let current = 0;
    let max = 0;

    for (const hardpoint of destructible) {
        if (hardpoint.health === null || hardpoint.health === undefined) {
            continue;
        }

        max += hardpoint.health;

        // A hardpoint marked destroyed contributes nothing, whatever its health entry says. The two are
        // one fact wearing two faces - a hardpoint ticked off in the Hardpoints list is destroyed
        // without any damage ever being subtracted - and reading only the health left the bar full
        // while the ship lost hardpoint after hardpoint.
        if (destroyed.has(hardpoint.id)) {
            continue;
        }

        // Clamped to the hardpoint's own declared health at BOTH ends, so a repair that overshoots or a
        // stale entry cannot report a ship in better condition than it was built in.
        current += Math.min(Math.max(hardpointHealth[hardpoint.id] ?? 0, 0), hardpoint.health);
    }

    return { current, max, fromHardpoints: true };
}

/**
 * Whether the unit is finished: every destructible hardpoint destroyed.
 *
 * Untargetable hardpoints count, and that is the interesting half. The game itself warns about
 * `Is_Targetable` NO with `Is_Destroyable` YES, but at least two mods use the combination
 * deliberately as a gameplay element - a ship you cannot finish off by shooting only what the
 * reticles offer. Excluding those would let the preview declare a unit dead that the engine would
 * keep alive.
 *
 * A unit with no destructible hardpoints does not die that way - it dies by its own health pool,
 * which is what <paramref name="hull" /> carries. That half went unwritten for a long time and the
 * comment here claimed it anyway, so the 188 objects that carry their weapons as WEAPON behaviour
 * rather than as hardpoints - every fighter, every infantry squad, most ground vehicles - simply
 * could not be killed. They are the COMMON unit, against 68 with hardpoints.
 *
 * A pool with no maximum is a unit that does not use this channel at all, not a unit at zero. That
 * is the guard `destructible.length > 0` used to be doing, moved rather than dropped: without it,
 * anything declaring no `Tactical_Health` would be dead the moment the preview opened.
 */
export function unitDestroyed(
    hardpoints: readonly PreviewHardpoint[],
    destroyed: ReadonlySet<string>,
    hull?: HullPool | null,
): boolean {
    const destructible = hardpoints.filter(h => h.isDestroyable);

    // The hardpoints are the authority wherever there are any: the engine offers them and nothing
    // else, and the pool is their SUM, so it cannot say anything they do not already say.
    if (destructible.length > 0) {
        return destructible.every(h => destroyed.has(h.id));
    }

    return hull !== undefined && hull !== null && hull.max > 0 && hull.current <= 0;
}

/**
 * The hardpoint type that projects the shield.
 *
 * 23 of them ship across both trees and every one is `Is_Destroyable` YES. The files are not
 * consistent about case, here as everywhere else.
 */
const SHIELD_GENERATOR = 'hard_point_shield_generator';

/**
 * Whether the shield is down because every generator projecting it has been shot off.
 *
 * The user's rule: *"if a ship has all hardpoints of type shield generator destroyed the shields
 * have to drop to zero, no matter the current state - similar to how the engines turn off."* So this
 * is not a drain, it is a SWITCH: whatever the pool held, there is no shield while this holds.
 *
 * Two things it deliberately does NOT do:
 *
 * - **A unit with no generators keeps its shield.** Every fighter is in that position - SHIELDED,
 *   and no hardpoints at all - so reading "none destroyed" as "all destroyed" would strip the shield
 *   off most of the game.
 * - **An indestructible generator counts as standing.** A hull carrying one that cannot be shot off
 *   never loses its shield, which is the file's own answer rather than a gap in this one.
 */
export function shieldGeneratorsDown(
    hardpoints: readonly PreviewHardpoint[],
    destroyed: ReadonlySet<string>,
): boolean {
    const generators = hardpoints.filter(
        h => (h.type ?? '').toLowerCase() === SHIELD_GENERATOR);

    // One that cannot be shot off never stops projecting, so it is not enough for the destroyable
    // ones to be gone. Every shipped generator IS destroyable; this is about a mod that marks one
    // otherwise, and the answer is that its shield simply cannot be brought down this way.
    if (generators.length === 0 || generators.some(h => !h.isDestroyable)) {
        return false;
    }

    return generators.every(h => destroyed.has(h.id));
}

/**
 * Whether the unit itself can be shot at.
 *
 * False as soon as it has a destructible hardpoint - the engine offers the hardpoints and nothing else, so
 * a "Hull" choice in the attacker panel would be aiming at something no weapon can reach.
 */
export function unitTargetable(hardpoints: readonly PreviewHardpoint[]): boolean {
    return !hardpoints.some(h => h.isDestroyable);
}
