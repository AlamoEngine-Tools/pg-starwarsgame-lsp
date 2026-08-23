// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Who a shot actually damages.
//
// The rule, as the user gave it: a projectile with NO blast area damages exactly one target, and
// if it applies more damage than the target has left the target disappears - the surplus goes
// nowhere. One with a blast area damages its target AND everything inside the radius.
//
// Measured over foc: 63 projectiles declare `Projectile_Blast_Area_Damage` and 62 a `_Range`; only
// 10 declare `_Dropoff` with `_Dropoff_Tiers` of 3, 4 or 5, and `_Max_Victims` appears on 2. So a
// flat blast inside a radius is the normal case and tiered falloff is the exception.

import type { PreviewProjectile } from '../../protocol/modelPreview';

/** A mount and how far it is from where the shot landed, in engine units. */
export interface BlastCandidate {
    id: string;
    distance: number;
}

/** One mount caught by a shot, and what it takes. */
export interface BlastHit {
    id: string;
    /** `Projectile_Damage`. Only ever non-zero for the mount actually aimed at. */
    directDamage: number;
    /** `Projectile_Blast_Area_Damage`, after any tier falloff. */
    blastDamage: number;
    /** Which falloff band it fell in, or null where the blast is flat. */
    tier: number | null;
}

/**
 * Everything one shot damages, nearest first.
 *
 * The target takes its direct damage AND the blast. Both, and the shipped data forces it:
 * `Proj_Veers_AT_AT_Max_Power_Laser_Red` declares `Projectile_Damage` 0.0 alongside a blast of 40,
 * as do `Proj_Ground_Proton_Torpedo` and `Proj_T4B_Missile` - reading the direct number alone would
 * make those weapons do nothing at all to the thing they hit.
 *
 * The target is always included, even if it somehow sits outside its own blast radius. A direct hit
 * is a direct hit; the range governs who ELSE is caught.
 */
export function blastVictims(
    projectile: PreviewProjectile,
    targetId: string,
    candidates: readonly BlastCandidate[],
    destroyed: ReadonlySet<string> = new Set(),
): BlastHit[] {
    const direct = projectile.damage ?? 0;
    const range = projectile.blastAreaRange ?? 0;
    const blast = projectile.blastAreaDamage ?? 0;

    if (range <= 0 || blast <= 0) {
        return [{ id: targetId, directDamage: direct, blastDamage: 0, tier: null }];
    }

    const tiers = projectile.blastAreaDropoff ? projectile.blastAreaDropoffTiers ?? null : null;

    const caught = candidates
        .filter(c => c.id === targetId || (c.distance <= range && !destroyed.has(c.id)))
        .sort((a, b) => a.distance - b.distance)
        .map(c => ({
            id: c.id,
            directDamage: c.id === targetId ? direct : 0,
            blastDamage: blast * blastShare(
                c.distance, range, projectile.blastAreaDropoff, projectile.blastAreaDropoffTiers),
            tier: tiers === null ? null : bandOf(c.distance, range, tiers),
        }));

    const cap = projectile.blastAreaMaxVictims ?? null;

    return cap === null || cap <= 0 ? caught : caught.slice(0, cap);
}

/**
 * What fraction of the blast damage reaches something this far out.
 *
 * 1 everywhere inside the radius unless the projectile declares dropoff - which 53 of the 63 do
 * not.
 *
 * **The per-tier multiplier is an ASSUMPTION.** `Projectile_Blast_Area_Dropoff_Tiers` says how many
 * bands the falloff is quantised into and nothing states what each band is worth, so this reads it
 * as an even linear step: with 4 tiers the bands are worth 1, 3/4, 1/2 and 1/4 of the damage. That
 * is a reading, not a measurement, and it is the one number in the blast model that nobody has
 * confirmed. It affects 10 projectiles in the whole of foc.
 */
export function blastShare(
    distance: number, range: number, dropoff: boolean, tiers: number | null | undefined,
): number {
    if (!dropoff || tiers === null || tiers === undefined || tiers <= 0 || range <= 0) {
        return 1;
    }

    return (tiers - bandOf(distance, range, tiers)) / tiers;
}

/** Which band a distance falls in: 0 at the centre, `tiers - 1` at the rim. */
function bandOf(distance: number, range: number, tiers: number): number {
    const band = Math.floor((Math.max(distance, 0) / range) * tiers);

    return Math.min(Math.max(band, 0), tiers - 1);
}

/** A point in the scene, or null for a bone that never loaded. */
export interface Point3 {
    x: number;
    y: number;
    z: number;
}

/**
 * Every mount, measured from the one that was hit.
 *
 * A mount with no position is left out entirely rather than defaulted to the origin - an unloaded
 * bone placed at zero would sit at the epicentre of every blast and take full damage from all of
 * them. If the TARGET has no position there is nothing to measure from, so nothing is caught.
 */
export function candidatesFrom(
    positions: Readonly<Record<string, Point3 | null>>, targetId: string,
): BlastCandidate[] {
    const origin = positions[targetId] ?? null;

    if (origin === null) {
        return [];
    }

    return Object.entries(positions)
        .filter((entry): entry is [string, Point3] => entry[1] !== null)
        .map(([id, at]) => ({
            id,
            distance: Math.hypot(at.x - origin.x, at.y - origin.y, at.z - origin.z),
        }));
}
