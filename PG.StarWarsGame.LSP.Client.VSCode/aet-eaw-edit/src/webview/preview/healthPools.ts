// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The two health pools, and the leash between them.
//
// Read out of the 2018 debug build rather than inferred - see
// https://claude.ai/code/artifact/3832ce2b-7425-4142-925b-fb5e54e8ed70. The hull
// (`Tactical_Health`) and the combined hardpoint health are SEPARATE pools whose absolute totals
// need not agree; the shipped corpus ranges from 0.06x to 3.40x. What ties them is a pair of caps,
// each expressed in PERCENTAGES, which is why the totals cancel:
//
//   GameObjectClass::Service            hull  <= hardpoint% + constraint   (tag-gated)
//   GameObjectClass::Service_Hard_Points hardpoints <= hull% + constraint  (always)
//
// Both are guarded by the unit HAVING destroyable hardpoints. That guard is not incidental: without
// it an empty hardpoint pool reads as 0% and would drag every fighter's hull down to the constraint.

/**
 * What both shipped corpora set `Hull_Vs_Hard_Points_Health_Constraint` to.
 *
 * A fallback for a scene that predates the field, never a substitute for reading it: a mod that
 * raises it to 1 switches both corrections off, and drawing 0.2 for them would show a leash their
 * game does not have.
 */
export const DEFAULT_HULL_CONSTRAINT = 0.2;

/** A health pool: what is left, and what it was built with. */
export interface Pool {
    current: number;
    max: number;
}

/** Both pools after a service tick. */
export interface Leashed {
    hull: Pool;
    hardpoints: Pool;
}

const clamp = (value: number, low: number, high: number): number =>
    Math.min(Math.max(value, low), high);

/**
 * A pool as a fraction of its maximum.
 *
 * A pool with no maximum is 0, not a division by zero - and it is also the shape of a unit that
 * does not use the channel at all, which is why every caller checks `max > 0` before acting on it.
 */
export function percentOf(pool: Pool): number {
    return pool.max > 0 ? clamp(pool.current / pool.max, 0, 1) : 0;
}

/**
 * Both corrections, in the order the engine runs them.
 *
 * The hull is pulled first, inside `Service`, and the hardpoint step then reads the UPDATED hull -
 * which is what makes the two meet rather than merely approach when the constraint is 0.
 *
 * @param constraint `Hull_Vs_Hard_Points_Health_Constraint`. At 1 every cap clamps to 100% and
 *     neither correction runs; at 0 each pool is pulled to the other and they converge on the
 *     lower. It is a global, so it is read from the workspace rather than assumed.
 * @param diesWithHardpoints `Should_Be_Destroyed_When_All_Hardpoints_Destroyed`. Gates the hull
 *     half only: hardpoints follow the hull whatever it says.
 */
export function leashed(
    hull: Pool, hardpoints: Pool, constraint: number, diesWithHardpoints: boolean,
): Leashed {
    // No destroyable hardpoints means neither correction runs at all.
    if (hardpoints.max <= 0) {
        return { hull, hardpoints };
    }

    let hullNow = hull;

    if (diesWithHardpoints) {
        const cap = Math.min(1, percentOf(hardpoints) + constraint);
        if (percentOf(hullNow) > cap) {
            // A flat assignment through Set_Health_Percent, not damage applied to the pool.
            hullNow = { ...hullNow, current: cap * hullNow.max };
        }
    }

    const cap = Math.min(1, percentOf(hullNow) + constraint);
    if (percentOf(hardpoints) <= cap) {
        return { hull: hullNow, hardpoints };
    }

    // Damage, distributed across the destroyable hardpoints in proportion to their CURRENT health -
    // so every one loses the same fraction of what it had, and the total lands exactly on the cap
    // in a single tick rather than converging over several.
    return { hull: hullNow, hardpoints: { ...hardpoints, current: cap * hardpoints.max } };
}

/**
 * What the health bar shows: the worse of the two pools.
 *
 * `Get_Display_Health_Percent` starts from the hull and takes the minimum only when the unit has
 * destroyable hardpoints AND dies with them. With the tag off the bar never consults the hardpoints,
 * which is why shooting the palace's generators moves nothing visible.
 */
export function displayPercent(
    hull: Pool, hardpoints: Pool, diesWithHardpoints: boolean,
): number {
    const hullPercent = percentOf(hull);

    if (hardpoints.max <= 0 || !diesWithHardpoints) {
        return hullPercent;
    }

    return Math.min(hullPercent, percentOf(hardpoints));
}
