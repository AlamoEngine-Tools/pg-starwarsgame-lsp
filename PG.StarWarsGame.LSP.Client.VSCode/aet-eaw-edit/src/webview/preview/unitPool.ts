// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a unit has left, and what it takes to finish it.
//
// The rules, decompiled from the 2018 build rather than conventional -
// https://claude.ai/code/artifact/3832ce2b-7425-4142-925b-fb5e54e8ed70:
//
//   - The hull (`Tactical_Health`) and the combined hardpoint health are SEPARATE pools. Their
//     absolute totals need not agree, and across the shipped corpus they range from 0.06x to 3.40x.
//   - What ties them is a pair of percentage caps - see `healthPools.ts`.
//   - A unit dies when its HULL reaches zero. Destroying every destroyable hardpoint kills it only
//     when `Should_Be_Destroyed_When_All_Hardpoints_Destroyed` says so, and even then by setting the
//     hull to zero rather than by any separate death.
//
// This file used to sum the hardpoints and call that the hull, on the reasoning that most mods
// author it that way. The sum is real - it is the hardpoint pool - but it was never the hull.

import type { PreviewHardpoint, PreviewTargetDefence } from '../../protocol/modelPreview';

/** Health left per hardpoint, keyed by hardpoint id. Null where the hardpoint declares none. */
export type HardpointHealth = Readonly<Record<string, number | null>>;

/**
 * A health pool: what is left, and what it was built with.
 *
 * Structurally identical to `healthPools.Pool`, which is what the leash operates on - the two are
 * the same idea and are deliberately interchangeable.
 */
export interface HullPool {
    current: number;
    max: number;
}

/**
 * The combined hardpoint pool: every DESTROYABLE hardpoint's health, summed.
 *
 * This is the sum the engine divides by in `Get_Combined_Hard_Point_Health_Percent`, so it is a
 * denominator rather than a hull. A hardpoint with no declared `Health` contributes nothing to
 * either end. 210 of foc's hardpoints declare none, and they are INDESTRUCTIBLE rather than already
 * dead - counting a null as a zero would open every such ship at less than full strength.
 *
 * Empty (`max` 0) for a unit with no destroyable hardpoints, which is the shape every caller checks
 * before applying a correction: the engine guards both halves of the leash on exactly that.
 */
export function hardpointPool(
    hardpoints: readonly PreviewHardpoint[],
    hardpointHealth: HardpointHealth,
    destroyed: ReadonlySet<string> = new Set(),
): HullPool {
    let current = 0;
    let max = 0;

    for (const hardpoint of hardpoints) {
        if (!hardpoint.isDestroyable
            || hardpoint.health === null || hardpoint.health === undefined) {
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

    return { current, max };
}

/**
 * The unit's OWN health pool - `Tactical_Health` - whatever its hardpoints are doing.
 *
 * Always this, now. It used to be the hardpoint sum wherever there were hardpoints, which meant the
 * panel had no way to show the pool that actually dies, and no way to show the two disagreeing.
 *
 * @param hullNow What the pool has LEFT. Undefined means nothing has fired yet - a preview that has
 *     just opened - so the declared health stands. Clamped at both ends: an overkill shot must not
 *     draw a negative bar, and a repair that overshoots must not report a ship in better condition
 *     than it was built in.
 */
export function hullPool(
    defence: PreviewTargetDefence | null | undefined,
    hullNow?: number | null,
): HullPool {
    const max = defence?.tacticalHealth ?? 0;

    return {
        current: hullNow === undefined || hullNow === null
            ? max
            : Math.min(Math.max(hullNow, 0), max),
        max,
    };
}

/**
 * Whether the unit is finished.
 *
 * The hull is what dies: `Take_Direct_Damage` is the only route to `Kill`. Destroying every
 * destroyable hardpoint is a second route ONLY when
 * `Should_Be_Destroyed_When_All_Hardpoints_Destroyed` says so, and even then the engine spends it by
 * setting the hull to zero rather than by killing the object directly.
 *
 * Untargetable hardpoints count toward that, and that is the interesting half. The game itself warns
 * about `Is_Targetable` NO with `Is_Destroyable` YES - a toothless assert that falls straight
 * through - and at least two mods use the combination deliberately, so an untargetable hardpoint
 * still has to die before the unit does.
 *
 * With the tag off, hardpoints stop being a route to death entirely: `U_Ground_Palace` keeps its
 * generators as destructible scenery and dies only when its own health is gone.
 *
 * A pool with no maximum is a unit that does not use this channel at all, not a unit at zero -
 * without that guard, anything declaring no `Tactical_Health` would be dead the moment it opened.
 */
export function unitDestroyed(
    hardpoints: readonly PreviewHardpoint[],
    destroyed: ReadonlySet<string>,
    hull?: HullPool | null,
    diesWithHardpoints = true,
): boolean {
    const destructible = hardpoints.filter(h => h.isDestroyable);

    if (diesWithHardpoints && destructible.length > 0
        && destructible.every(h => destroyed.has(h.id))) {
        return true;
    }

    return hull !== undefined && hull !== null && hull.max > 0 && hull.current <= 0;
}

/**
 * The hull after the all-destroyed branch has had its say.
 *
 * The engine does not "kill" an object whose last destroyable hardpoint dies - it sets `Health` to
 * zero by flat assignment and lets the hull path finish it. Modelling that rather than a separate
 * death flag matters because everything downstream reads the hull: the health bar is the lower of
 * the two pools, and the damage STAGE is the hull's percentage alone, so a unit finished through its
 * hardpoints has to show an empty bar and its last damage stage.
 *
 * Gated by `Should_Be_Destroyed_When_All_Hardpoints_Destroyed`, like every other route from the
 * hardpoints back to the hull.
 */
export function hullAfterHardpointDeath(
    hull: HullPool,
    hardpoints: readonly PreviewHardpoint[],
    destroyed: ReadonlySet<string>,
    diesWithHardpoints = true,
): HullPool {
    if (!diesWithHardpoints) {
        return hull;
    }

    const destructible = hardpoints.filter(h => h.isDestroyable);

    return destructible.length > 0 && destructible.every(h => destroyed.has(h.id))
        ? { ...hull, current: 0 }
        : hull;
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
