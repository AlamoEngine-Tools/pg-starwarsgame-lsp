// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The weapon you build, and what it does to the thing on stage.
//
// The previewed object is the TARGET. That inversion is the whole shape of this panel: the scene
// carries the defence - armor types, pools, and the two columns of the damage table that apply to
// it - and everything offensive is typed by the reader. There is no automatic preload, so opening a
// subject never rewrites what you set up.
//
// The rules below are ENGINE BEHAVIOUR as the user gave it, not something measured out of the
// files. Kept in one pure module so they can be read against that statement line by line.

import type { PreviewProjectile, PreviewTargetDefence } from '../../protocol/modelPreview';
import type { HullPool } from './unitPool';
import type { BlastHit } from './blast';
import { healthColour } from './reticles';

/**
 * Which optional mechanics the panel is showing.
 *
 * Energy is behind a feature toggle and **off by default**, at the user's instruction: the mechanic
 * is in the engine and works, but the game disables it and ships no UI for it, so a mod maker should
 * turn it on deliberately and read what it means first. Every energy element goes together - the
 * pool row, the bar and the projectile switch - or the panel would offer a weapon setting that
 * drains a pool it does not draw.
 */
export interface PoolOptions {
    energy: boolean;

    /**
     * Every hardpoint projecting the shield has been shot off, so there is no shield.
     *
     * The user's rule: *"if a ship has all hardpoints of type shield generator destroyed the shields
     * have to drop to zero, no matter the current state - similar to how the engines turn off."* A
     * SWITCH, not a drain - the bar reads empty whatever the pool holds, and the row says why.
     *
     * See `shieldGeneratorsDown`, which decides it.
     */
    shieldsDown?: boolean;
}

/** Off, which is what a caller that has not been told yet must assume. */
const POOLS_OFF: PoolOptions = { energy: false };

/** The weapon the reader has configured. */
export interface Attacker {
    damage: number;
    damageType: string;
    /** `Projectile_Does_Shield_Damage` and its two siblings, which decide what a hit touches. */
    shield: boolean;
    energy: boolean;
    hitpoint: boolean;

    /**
     * `Projectile_Blast_Area_Damage`. Zero for a bolt that does not go off, which is most of them.
     *
     * Blast used to arrive ONLY with a picked projectile, read straight off the file - so a reader
     * could type a damage number and a damage type and had no way at all to say "and it explodes".
     * It is part of the weapon you build now, like every other field here.
     */
    blastDamage: number;

    /** `Projectile_Blast_Area_Range`, in engine units. Zero reaches nothing but the target. */
    blastRange: number;

    /** `Projectile_Blast_Area_Dropoff` - whether the blast falls off with distance. */
    blastDropoff: boolean;

    /**
     * `Projectile_Blast_Area_Dropoff_Tiers` - how many bands it falls off in.
     *
     * Only 10 of foc's 63 blast projectiles declare a dropoff at all, with 3, 4 or 5 tiers, so a
     * FLAT blast inside a radius is the normal case and this is the exception.
     */
    blastDropoffTiers: number;
}

/** What is left of the target. Reset on open - the opening rules beat persistence. */
export interface Pools {
    /**
     * The subject these numbers were measured on. Empty before any scene has arrived.
     *
     * Carried so a reading can never be mistaken for a reading of a DIFFERENT unit. The pools are
     * refilled by an effect and the scene changes during a render, so there is always a commit where
     * the two disagree - and an empty hull read against the new unit's declared health is a unit
     * that is dead on arrival. See {@link poolsFor}.
     */
    subject: string;
    shield: number;
    hull: number;
    energy: number;
}

/**
 * The pools, but only when they describe the scene being drawn.
 *
 * Reported as "loading a unit without hardpoints immediately plays the death explosion on load",
 * and it was the load ORDER rather than any particular unit: whichever hardpoint-less subject
 * opened first in a fresh panel blew itself up. The reset effect runs at MOUNT as well, when there
 * is no scene, so it leaves every pool at 0; the first real scene then renders for one commit
 * against those zeroes, `unitDestroyed` sees an empty hull with a positive maximum, and the death
 * watch fires. The commit after that refills the pool and silently undoes it, which is why nothing
 * looked wrong afterwards and only the explosion gave it away.
 *
 * A unit WITH destructible hardpoints was never affected - its hull is summed from them and the
 * pools are not consulted at all.
 */
export function poolsFor(
    scene: { subject: string } | null | undefined,
    pools: Pools | null | undefined,
): Pools | null {
    if (scene === null || scene === undefined || pools === null || pools === undefined) {
        return null;
    }

    return pools.subject === scene.subject ? pools : null;
}

/**
 * Every pool at capacity, as a freshly opened - or freshly repaired - subject stands.
 *
 * The one place a `Pools` is built from a scene, so the subject is always stamped on it. Both
 * callers used to write the three fields out by hand, which is how one of them could have carried
 * the stamp and the other not.
 */
export function fullPools(
    scene: { subject: string; defence?: PreviewTargetDefence | null } | null | undefined,
): Pools {
    return {
        subject: scene?.subject ?? '',
        shield: scene?.defence?.shieldPoints ?? 0,
        hull: scene?.defence?.tacticalHealth ?? 0,
        energy: scene?.defence?.energyCapacity ?? 0,
    };
}

/**
 * Where the panel starts.
 *
 * A plain hitpoint weapon, because that is the one every target can feel: shield-only against an
 * unshielded object does nothing at all, and opening on a configuration that visibly does nothing
 * reads as broken.
 */
export const DEFAULT_ATTACKER: Attacker = {
    damage: 100,
    damageType: 'Damage_Default',
    shield: false,
    energy: false,
    hitpoint: true,
    // No blast: a plain bolt that damages what it hits and nothing else, which is what 110 of foc's
    // 173 projectiles are.
    blastDamage: 0,
    blastRange: 0,
    blastDropoff: false,
    blastDropoffTiers: 0,
};

/**
 * The weapon the reader built, shaped as a projectile so one code path resolves every shot.
 *
 * `fire` used to BRANCH: with a projectile picked it went through `blastVictims`, and without one it
 * hand-built a single hit with no blast. Two paths, and the reader's own blast fields could only
 * ever have reached one of them. This is the whole of the difference now - the picker fills the
 * fields, and the fields are what fires.
 */
export function attackerProjectile(attacker: Attacker): PreviewProjectile {
    return {
        id: 'attacker',
        render: 'Model',
        damage: attacker.damage,
        damageType: attacker.damageType,
        doesShieldDamage: attacker.shield,
        doesEnergyDamage: attacker.energy,
        doesHitpointDamage: attacker.hitpoint,
        blastAreaDamage: attacker.blastDamage,
        blastAreaRange: attacker.blastRange,
        blastAreaDropoff: attacker.blastDropoff,
        blastAreaDropoffTiers: attacker.blastDropoffTiers,
    };
}

/**
 * The multiplier for one damage type against one armor column.
 *
 * **A pair the table does not name is 1.0.** That is not an edge case: the shipped table names 2426
 * of 4293 possible pairs, so the gap is hit constantly, and reading a miss as zero would make most
 * weapons do nothing. A DECLARED zero is kept - a modder writing 0 means it, and a falsy check would
 * quietly promote it to 1.
 */
export function armorFactor(factors: Record<string, number>, damageType: string): number {
    const declared = factors[damageType];
    if (typeof declared === 'number') {
        return declared;
    }

    // The file is not consistent about casing, here or in the reticle rows.
    const wanted = damageType.toLowerCase();
    for (const [name, factor] of Object.entries(factors)) {
        if (name.toLowerCase() === wanted) {
            return factor;
        }
    }

    return 1;
}

/**
 * One hit, resolved against the target's current pools.
 *
 * The rules, as the user gave them. Shields are in play ONLY when the target declares `SHIELDED`;
 * then the projectile's three switches decide entirely:
 *
 * | switches | what happens |
 * | --- | --- |
 * | energy only | drains the energy pool only |
 * | shield only | depletes the shield, hull untouched |
 * | hitpoint only | **bypasses the shield** and hits the hull |
 * | more than one | shield first, then hull and energy |
 *
 * There is no global shield-then-hull ordering - that applies to the multi-switch case alone, and
 * reading it as a general rule is what makes a hitpoint-only bolt wrongly stop at a full shield.
 */
export function resolveHit(
    attacker: Attacker, defence: PreviewTargetDefence | null | undefined, pools: Pools,
): Pools {
    if (defence === null || defence === undefined) {
        return { ...pools };
    }

    const hit = applyHit(attacker, defence, pools.shield, pools.energy, pools.hull);

    return { ...pools, shield: hit.shield, energy: hit.energy, hull: hit.hullLike };
}

/**
 * The same hit, aimed at ONE HARDPOINT rather than at the hull.
 *
 * A hardpoint declares no armor and no shield of its own - measured: 0 of them do, and 145 declare
 * only `Health` - so it is hull geometry sitting behind the ship's shield. It therefore takes the
 * HULL's armor factor, and the shield stops a bolt aimed at it exactly as it stops one aimed at the
 * hull.
 *
 * A hardpoint with no `Health` at all is indestructible rather than already dead: 210 of foc's
 * hardpoints declare none, and reading that as zero would blow every one of them off on the first
 * shot.
 */
export function fireAtHardpoint(
    attacker: Attacker,
    defence: PreviewTargetDefence | null | undefined,
    pools: Pools,
    hardpointHealth: number | null,
): { pools: Pools; hardpointHealth: number | null; destroyed: boolean } {
    if (defence === null || defence === undefined || hardpointHealth === null) {
        return { pools: { ...pools }, hardpointHealth, destroyed: false };
    }

    const hit = applyHit(attacker, defence, pools.shield, pools.energy, hardpointHealth);

    return {
        // The hull pool is untouched: the shot went into the hardpoint.
        pools: { ...pools, shield: hit.shield, energy: hit.energy },
        hardpointHealth: hit.hullLike,
        // At EXACTLY its health, not a point later. `damage.ts` takes it from here - the model
        // hides, the decal shows, the death explosion plays once and the breakoff prop drops.
        destroyed: hit.hullLike <= 0,
    };
}

/**
 * The rules, in one place, over an abstract "hull-like" pool.
 *
 * Shared by the hull and the hardpoint paths deliberately: they differ only in WHICH pool absorbs the
 * hull half, and copying the shield logic into both is how the two would come to disagree about the
 * bypass.
 */
function applyHit(
    attacker: Attacker,
    defence: PreviewTargetDefence,
    shield: number,
    energy: number,
    hullLike: number,
): { shield: number; energy: number; hullLike: number } {
    const after = { shield, energy, hullLike };

    // Energy is independent of the shield-or-hull question: it drains whenever the switch is set,
    // in every one of the four rows above.
    if (attacker.energy) {
        after.energy = drop(after.energy, attacker.damage);
    }

    if (attacker.shield && defence.isShielded) {
        const shieldFactor = armorFactor(defence.shieldFactors, attacker.damageType);
        const scaled = attacker.damage * shieldFactor;
        const absorbed = Math.min(after.shield, scaled);
        after.shield = drop(after.shield, scaled);

        // The surplus carries on, but only when a hull switch is set too - a shield-only weapon
        // stops at the shield however much it had left over. Taken back out of the shield's scale
        // and re-scaled by the HULL's, because the overflow is now hitting a different armor.
        if (attacker.hitpoint) {
            const spare = (scaled - absorbed) / Math.max(shieldFactor, Number.EPSILON);

            after.hullLike = drop(after.hullLike,
                spare * armorFactor(defence.hullFactors, attacker.damageType));
        }

        return after;
    }

    // Everything else that touches the hull. A hitpoint-only bolt lands here whether or not the
    // shield is up, which IS the bypass; a shield-only bolt against an unshielded target lands
    // nowhere, because there is no shield to deplete and it does no hull damage by definition.
    if (attacker.hitpoint) {
        after.hullLike = drop(after.hullLike,
            attacker.damage * armorFactor(defence.hullFactors, attacker.damageType));
    }

    return after;
}

/** Copies a projectile's values in. A COPY, not a binding - see the panel. */
export function attackerFromProjectile(
    projectile: PreviewProjectile, current: Attacker,
): Attacker {
    return {
        // What the projectile does not declare keeps the reader's own value: a bolt with no damage
        // number should not blank the field to a zero nobody chose.
        damage: projectile.damage ?? current.damage,
        damageType: projectile.damageType ?? current.damageType,
        shield: projectile.doesShieldDamage,
        energy: projectile.doesEnergyDamage,
        hitpoint: projectile.doesHitpointDamage,

        // The blast is the OPPOSITE, and deliberately: a bolt that declares none is a bolt that
        // does not go off, so the fields are cleared rather than keeping the last weapon's radius.
        // Leaving it would simulate something nobody described.
        blastDamage: projectile.blastAreaDamage ?? 0,
        blastRange: projectile.blastAreaRange ?? 0,
        blastDropoff: projectile.blastAreaDropoff === true,
        blastDropoffTiers: projectile.blastAreaDropoffTiers ?? 0,
    };
}

/** A pool never goes below zero - a negative one would read as a bug on the bar drawn from it. */
function drop(pool: number, amount: number): number {
    return Math.max(0, pool - amount);
}

/**
 * The three switches, labelled by what they DO.
 *
 * `Projectile_Does_Hitpoint_Damage` is the tag; "hull" is what a reader is looking for, and the
 * bypass is the one rule here that surprises people, so it is on the tooltip rather than left to be
 * discovered by experiment.
 */
export const DAMAGE_SWITCHES: readonly {
    id: 'shield' | 'energy' | 'hitpoint'; label: string; title: string;
}[] = [
    {
        id: 'shield', label: 'Shield',
        title: 'Projectile_Does_Shield_Damage - depletes the shield. On its own it never reaches '
            + 'the hull, however much is left over.',
    },
    {
        id: 'energy', label: 'Energy',
        title: 'Projectile_Does_Energy_Damage - drains the energy pool. Independent of the other '
            + 'two: It applies whether or not the shield is up.',
    },
    {
        id: 'hitpoint', label: 'Hull',
        title: 'Projectile_Does_Hitpoint_Damage - hits the hull. On its own it BYPASSES the shield '
            + 'entirely rather than being stopped by it.',
    },
];

/**
 * The switches on offer, which is all of them unless energy is off.
 *
 * Withheld rather than disabled: a disabled control says "this exists and not here", and the point
 * of the toggle is that energy damage should not be part of the panel at all until it is asked for.
 * {@link DAMAGE_SWITCHES} stays the full table - the wire still carries
 * `Projectile_Does_Energy_Damage`, and a projectile that sets it still resolves correctly.
 */
export function damageSwitches(options: PoolOptions = POOLS_OFF): readonly {
    id: 'shield' | 'energy' | 'hitpoint'; label: string; title: string;
}[] {
    return options.energy ? DAMAGE_SWITCHES : DAMAGE_SWITCHES.filter(s => s.id !== 'energy');
}

/**
 * One line of the target readout, and one bar under the ability command bar.
 *
 * The words and the bar are ONE model. They are the same three pools said two ways, and a second
 * copy would drift the first time either changed.
 */
export interface PoolRow {
    id: 'shield' | 'hull' | 'energy';
    label: string;
    detail: string;
    title: string;
    /** How full, 0 to 1 and clamped. A pool cannot be more than full or less than empty. */
    fraction: number;
    /**
     * The bar's colour.
     *
     * The user set these: shields BLUE, the hull on the hardpoints' own ramp - so a bar gone orange
     * in here and a targeting mark gone orange out there are saying the same thing - and energy
     * something neither of them is. Violet, because it is the one hue the preview does not already
     * spend: the hull ramp owns green through red, and blue is the shield.
     */
    colour: string;
}

/** Shields. Blue, and from the theme so it tracks a light editor. */
const SHIELD_COLOUR = 'var(--vscode-charts-blue, #3794ff)';

/** Energy. Deliberately neither of the other two - see {@link PoolRow.colour}. */
const ENERGY_COLOUR = 'var(--vscode-charts-purple, #b180d7)';

/** How full, clamped at both ends. A capacity of zero reads as empty rather than dividing by it. */
function fractionOf(left: number, capacity: number): number {
    return capacity > 0 ? Math.min(1, Math.max(0, left / capacity)) : 0;
}

/**
 * What the target has left, pool by pool.
 *
 * Three named pools rather than one health bar: WHICH one a weapon drains is the entire question
 * this panel exists to answer, and collapsing them would hide it. A pool the target does not declare
 * is left out rather than shown at "0 of 0", and a shield with no `SHIELDED` behaviour says so
 * instead of drawing a full bar no weapon can touch.
 */
export function poolRows(
    defence: PreviewTargetDefence | null | undefined,
    pools: Pools | null | undefined,
    hull?: HullPool | null,
    options: PoolOptions = POOLS_OFF,
): PoolRow[] {
    if (defence === null || defence === undefined || pools === null || pools === undefined) {
        return [];
    }

    const rows: PoolRow[] = [];

    if ((defence.shieldPoints ?? 0) > 0) {
        rows.push({
            id: 'shield',
            label: 'Shield',
            detail: options.shieldsDown === true
                ? 'down - every shield generator has been destroyed'
                : defence.isShielded
                    ? `${round(pools.shield)} of ${round(defence.shieldPoints ?? 0)}`
                        + `${defence.shieldArmorType === null
                            || defence.shieldArmorType === undefined
                            ? '' : ` - ${defence.shieldArmorType}`}`
                    : 'not in play - this object declares Shield_Points but not SHIELDED',
            title: 'Shield_Points, defended by Shield_Armor_Type',
            // Empty for either reason: a shield the object never raises protects nothing, and one
            // whose generators are gone has stopped protecting. The bar says what is between a
            // shot and the hull, and in both cases that is nothing.
            fraction: defence.isShielded && options.shieldsDown !== true
                ? fractionOf(pools.shield, defence.shieldPoints ?? 0)
                : 0,
            colour: SHIELD_COLOUR,
        });
    }

    // The HARDPOINTS are the hull on a unit that has them - it cannot be targeted itself, and most
    // mods author its health as their sum. Falls back to what the file declares, which is also what
    // a unit without hardpoints uses.
    const hullMax = hull?.fromHardpoints === true ? hull.max : defence.tacticalHealth ?? 0;

    // `hull.current` is only an answer when the hull IS the hardpoints. For a unit with none,
    // `hullPool` can only report the declared `Tactical_Health` - it has nothing to sum - and
    // reading that as the current value pinned the bar to full: the damage landed in `pools.hull`
    // every shot and the row never moved. That is what "units without hardpoints don't take any
    // damage" was; they took it, the panel just never said so.
    const hullNow = hull?.fromHardpoints === true ? hull.current : pools.hull;

    if (hullMax > 0) {
        const armor = defence.armorType === null || defence.armorType === undefined
            ? '' : ` - ${defence.armorType}`;

        rows.push({
            id: 'hull',
            label: 'Hull',
            detail: hull?.fromHardpoints === true
                ? `${round(hullNow)} of ${round(hullMax)} - summed from hardpoints${armor}`
                : `${round(hullNow)} of ${round(hullMax)}${armor}`,
            title: hull?.fromHardpoints === true
                ? 'The summed Health of every destructible hardpoint. A unit with hardpoints '
                    + 'cannot be targeted itself and dies when the last of them dies, and most mods '
                    + 'author its health as this sum. The engine ties the two together with '
                    + 'Hull_Vs_Hard_Points_Health_Constraint; what that computes is not known, so '
                    + `the convention is drawn rather than a derivation. Tactical_Health says ${
                        round(defence.tacticalHealth ?? 0)}.`
                : 'Tactical_Health, defended by Armor_Type',
            fraction: fractionOf(hullNow, hullMax),
            // The hardpoints' own ramp, so a bar gone orange in here and a targeting mark gone
            // orange out there are saying the same thing.
            colour: healthColour(fractionOf(hullNow, hullMax)),
        });
    }

    // Off unless asked for, and then the row, the bar and the projectile switch all appear
    // together - see PoolOptions.
    if (options.energy && (defence.energyCapacity ?? 0) > 0) {
        rows.push({
            id: 'energy',
            label: 'Energy',
            detail: `${round(pools.energy)} of ${round(defence.energyCapacity ?? 0)}`,
            title: 'Energy_Capacity. Never scaled by an armor factor - energy damage is flat.',
            fraction: fractionOf(pools.energy, defence.energyCapacity ?? 0),
            colour: ENERGY_COLOUR,
        });
    }

    return rows;
}

/** A pool as a person reads it: an armor factor of 0.35 leaves long tails nobody wants. */
function round(value: number): string {
    return Number.parseFloat(value.toFixed(1)).toString();
}

/** How many names the picker shows at once. Beyond this the search is the way through. */
const MAX_CHOICES = 40;

/**
 * The projectiles to offer, filtered by what the reader has typed.
 *
 * EVERY projectile in the tree, not the handful the subject fires. The panel builds a weapon to
 * shoot AT the subject, so its own armament is the wrong list - which is what it used to offer, and
 * what made the picker look arbitrary.
 *
 * Matched anywhere in the name rather than as a prefix: every one is called `Proj_something`, so a
 * prefix search would be useless. Capped, because 212 names in one dropdown is a scroll rather than
 * a picker - the cap is what makes the search read as the way through instead of an optional extra.
 */
export function projectileChoices(catalog: readonly string[], search: string): string[] {
    const wanted = search.trim().toLowerCase();

    const matching = wanted === ''
        ? catalog
        : catalog.filter(name => name.toLowerCase().includes(wanted));

    return matching.slice(0, MAX_CHOICES);
}

/** What one shot did to the whole target. */
export interface BlastResult {
    pools: Pools;
    hardpointHealth: Record<string, number | null>;
    /** Hardpoints this shot finished off, for the destruction path to pick up. */
    destroyed: ReadonlySet<string>;
}

/**
 * One shot, resolved against every hardpoint it caught.
 *
 * Sequential, nearest first, through the SHARED pools: it is one shot, but each victim is worked
 * out against the shield as it stands after the last, so a blast catching three hardpoints through a
 * thin shield gets through on the later ones. That ordering is a reading rather than a measurement,
 * and it is the only sane one available - resolving them all against the shield's opening value
 * would let a single blast be absorbed several times over.
 *
 * The surplus is LOST, per the user's rule: a projectile applies its damage to a target, and if
 * that is more than the target has left the target disappears. Nothing carries over to the next
 * hardpoint - that is the difference between a blast and a chain reaction.
 *
 * A hardpoint with no declared `Health` is untouched. 210 of foc's hardpoints declare none and are
 * indestructible rather than fragile.
 */
export function fireBlast(
    attacker: Attacker,
    defence: PreviewTargetDefence | null | undefined,
    pools: Pools,
    hits: readonly BlastHit[],
    hardpointHealth: Readonly<Record<string, number | null>>,
): BlastResult {
    const health: Record<string, number | null> = { ...hardpointHealth };
    const destroyed = new Set<string>();

    if (defence === null || defence === undefined) {
        return { pools: { ...pools }, hardpointHealth: health, destroyed };
    }

    let after: Pools = { ...pools };

    for (const hit of hits) {
        const current = health[hit.id] ?? null;
        if (current === null) {
            continue;
        }

        // The direct number and the blast are one hit on this hardpoint, not two: they are separate
        // tags because they reach different victims, not because they land separately.
        const damage = hit.directDamage + hit.blastDamage;
        const resolved = applyHit(
            { ...attacker, damage }, defence, after.shield, after.energy, current);

        after = { ...after, shield: resolved.shield, energy: resolved.energy };
        health[hit.id] = resolved.hullLike;

        if (resolved.hullLike <= 0) {
            destroyed.add(hit.id);
        }
    }

    return { pools: after, hardpointHealth: health, destroyed };
}
