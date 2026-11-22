// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The effect groups the game switches by name.
//
// Measured in the game: the start of an effect's name puts it in a group that is switched as one,
// compared over the prefix's length with case ignored - so `pe` takes `pem` effects too, and `pi`
// takes every `pi` effect, damage sparks included. An object is created with `pte`, `pptw` and `prs`
// hidden, and a type whose `Passive_Missile_Shield_Radius` is above 0 then shows its `prs`. Abilities
// and power modes switch their groups when they go on and again when they go off. Each is a write of
// the effect's state switch; the last write is what the effect has.

/** An effect group, by the name prefix the game reads. */
export const EFFECT_GROUP = {
    engines: "pe",
    turboEngines: "pte",
    ionStun: "pi",
    gravityWell: "pgw",
    sprint: "pas",
    powerToWeapons: "pptw",
    invulnerability: "pem",
    missileShield: "prs",
} as const;

export type EffectGroup = (typeof EFFECT_GROUP)[keyof typeof EFFECT_GROUP];

/** One write of a group's effects: shown or hidden. */
export interface GroupWrite {
    group: EffectGroup;
    shown: boolean;
}

/** What creating a unit writes. */
const ON_CREATION: readonly GroupWrite[] = [
    { group: EFFECT_GROUP.turboEngines, shown: false },
    { group: EFFECT_GROUP.powerToWeapons, shown: false },
    { group: EFFECT_GROUP.missileShield, shown: false },
];

/** What creating a missile-shield object writes after that. */
const MISSILE_SHIELD_OBJECT: readonly GroupWrite[] = [
    { group: EFFECT_GROUP.missileShield, shown: true },
];

/**
 * What creating an object with the ION_STUN_EFFECT behaviour writes. Measured in the game: the
 * behaviour takes the stun off when it starts, which hides the `pi` effects; a stun shows them and
 * its end hides them again. An object without the behaviour takes none of these writes.
 */
const ION_STUN_OBJECT: readonly GroupWrite[] = [
    { group: EFFECT_GROUP.ionStun, shown: false },
];

/**
 * The switch an ion stun is: not an ability of the unit's but a state done to it, by a projectile
 * in the game and by the reader here. It shows the `pi` effects while it lasts and hides them when
 * it ends - on a unit with the behaviour, which is the caller's to check.
 */
export const ION_STUN_SWITCH = "ION_STUN";

/** Turbo engines in place of the plain ones. */
const TURBO: readonly GroupWrite[] = [
    { group: EFFECT_GROUP.engines, shown: false },
    { group: EFFECT_GROUP.turboEngines, shown: true },
];

/**
 * What each ability and power mode writes when it goes on, by ability type. Going off writes the
 * opposite of each.
 *
 * Only the ones the preview can switch: a unit's own abilities. Ion stun is done TO a unit by a
 * projectile, so no ability here switches `pi` - creating a unit with the behaviour hides it, see
 * `ION_STUN_OBJECT`.
 */
const WHILE_ON: ReadonlyMap<string, readonly GroupWrite[]> = new Map([
    ["TURBO", TURBO],
    ["SPOILER_LOCK", TURBO],
    ["POWER_TO_WEAPONS", [{ group: EFFECT_GROUP.powerToWeapons, shown: true }]],
    ["LEECH_SHIELDS", [{ group: EFFECT_GROUP.powerToWeapons, shown: true }]],
    ["MISSILE_SHIELD", [{ group: EFFECT_GROUP.missileShield, shown: true }]],
    ["INVULNERABILITY", [{ group: EFFECT_GROUP.invulnerability, shown: true }]],
    ["SPRINT", [{ group: EFFECT_GROUP.sprint, shown: true }]],
    ["INTERDICT", [{ group: EFFECT_GROUP.gravityWell, shown: true }]],
    [ION_STUN_SWITCH, [{ group: EFFECT_GROUP.ionStun, shown: true }]],
]);

/** Whether an effect of this name is in the group: its start against the prefix, case ignored. */
export function inGroup(name: string, group: EffectGroup): boolean {
    return name.slice(0, group.length).toLowerCase() === group;
}

/** The groups one ability type switches while it is on; none for one that switches no group. */
export function groupsSwitchedBy(abilityType: string): readonly EffectGroup[] {
    return (WHILE_ON.get(abilityType.trim().toUpperCase()) ?? []).map(
        (write) => write.group,
    );
}

/** The last of `writes` that reaches an effect of this name, or undefined when none does. */
function lastWrite(
    name: string,
    writes: readonly GroupWrite[],
    written: boolean | undefined,
): boolean | undefined {
    let last = written;

    for (const write of writes) {
        if (inGroup(name, write.group)) {
            last = write.shown;
        }
    }

    return last;
}

/**
 * What creating an object writes to an effect of this name, or undefined when nothing.
 *
 * @param missileShieldObject Whether the object's type is a missile-shield object: its
 *     `Passive_Missile_Shield_Radius` is above 0.
 * @param ionStunObject Whether the object's type has the ION_STUN_EFFECT behaviour.
 */
export function createdWrite(
    name: string,
    missileShieldObject: boolean,
    ionStunObject = false,
): boolean | undefined {
    let created = lastWrite(name, ON_CREATION, undefined);

    if (missileShieldObject) {
        created = lastWrite(name, MISSILE_SHIELD_OBJECT, created);
    }

    if (ionStunObject) {
        created = lastWrite(name, ION_STUN_OBJECT, created);
    }

    return created;
}

/**
 * What one ability going on or off writes to an effect of this name, or `written` when it writes
 * nothing there.
 */
export function switchWrite(
    name: string,
    abilityType: string,
    on: boolean,
    written: boolean | undefined,
): boolean | undefined {
    const writes = (WHILE_ON.get(abilityType.trim().toUpperCase()) ?? []).map(
        (write) => (on ? write : { group: write.group, shown: !write.shown }),
    );

    return lastWrite(name, writes, written);
}
