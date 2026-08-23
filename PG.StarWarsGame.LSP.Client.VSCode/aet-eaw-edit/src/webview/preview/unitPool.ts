// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a unit has left, and what it takes to finish it.
//
// The rules, as the user gave them - they own the engine, this file owns the arithmetic:
//
//   - A unit WITH hardpoints cannot be targeted at all. Only its mounts can.
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

/** Health left per mount, keyed by hardpoint id. Null where the mount declares none. */
export type MountHealth = Readonly<Record<string, number | null>>;

/** The unit's hull bar. */
export interface HullPool {
    current: number;
    max: number;
    /**
     * Whether this is the SUM of the mounts rather than the unit's own `Tactical_Health`.
     *
     * Worth saying out loud in the panel: the two numbers disagree in the shipped data - the Star
     * Destroyer resolves to 7500 of `Tactical_Health` against 2850 summed over its ten mounts - so
     * a reader seeing 2850 where the XML says 7500 needs to know which one they are looking at.
     */
    fromHardpoints: boolean;
}

/**
 * The hull pool, from whichever source actually governs this unit.
 *
 * A mount with no declared `Health` contributes nothing to either end of the bar. 210 of foc's
 * hardpoints declare none, and they are INDESTRUCTIBLE rather than already dead - counting a null
 * as a zero would open every such ship at less than full strength.
 */
export function hullPool(
    defence: PreviewTargetDefence | null | undefined,
    hardpoints: readonly PreviewHardpoint[],
    mountHealth: MountHealth,
    destroyed: ReadonlySet<string> = new Set(),
): HullPool {
    const destructible = hardpoints.filter(h => h.isDestroyable);

    if (destructible.length === 0) {
        return {
            current: defence?.tacticalHealth ?? 0,
            max: defence?.tacticalHealth ?? 0,
            fromHardpoints: false,
        };
    }

    let current = 0;
    let max = 0;

    for (const mount of destructible) {
        if (mount.health === null || mount.health === undefined) {
            continue;
        }

        max += mount.health;

        // A mount marked destroyed contributes nothing, whatever its health entry says. The two are
        // one fact wearing two faces - a mount ticked off in the Hardpoints list is destroyed
        // without any damage ever being subtracted - and reading only the health left the bar full
        // while the ship lost mount after mount.
        if (destroyed.has(mount.id)) {
            continue;
        }

        // Clamped to the mount's own declared health at BOTH ends, so a repair that overshoots or a
        // stale entry cannot report a ship in better condition than it was built in.
        current += Math.min(Math.max(mountHealth[mount.id] ?? 0, 0), mount.health);
    }

    return { current, max, fromHardpoints: true };
}

/**
 * Whether the unit is finished: every destructible mount destroyed.
 *
 * Untargetable mounts count, and that is the interesting half. The game itself warns about
 * `Is_Targetable` NO with `Is_Destroyable` YES, but at least two mods use the combination
 * deliberately as a gameplay element - a ship you cannot finish off by shooting only what the
 * reticles offer. Excluding those would let the preview declare a unit dead that the engine would
 * keep alive.
 *
 * False for a unit with no destructible mounts at all: it does not die this way, it dies by its own
 * health pool, and answering true would kill every fighter the moment it opened.
 */
export function unitDestroyed(
    hardpoints: readonly PreviewHardpoint[], destroyed: ReadonlySet<string>,
): boolean {
    const destructible = hardpoints.filter(h => h.isDestroyable);

    return destructible.length > 0 && destructible.every(h => destroyed.has(h.id));
}

/**
 * Whether the unit itself can be shot at.
 *
 * False as soon as it has a destructible mount - the engine offers the mounts and nothing else, so
 * a "Hull" choice in the attacker panel would be aiming at something no weapon can reach.
 */
export function unitTargetable(hardpoints: readonly PreviewHardpoint[]): boolean {
    return !hardpoints.some(h => h.isDestroyable);
}
