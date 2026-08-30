// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The preview settings that belong to the MOD rather than to the person.
//
// One test decides what lives here: does the setting name something out of the project's own tree?
// All four do. A damage type, a saved weapon built out of one, a faction, and the colour picked to
// match that faction are names a mod declares - and a mod can declare factions and damage types the
// next one has never heard of. Every one of these used to be a field of `ViewerSettings`, which is
// backed by `globalState`, so all four followed the MACHINE: `Damage_Whatever` typed against one mod
// came back in the next, which declares no such type.
//
// The rule is the repo's own: a setting lives at the tier of the thing it describes, and you would
// expect the answer to change when a different mod is opened. So this rides in `workspaceState`
// beside the dialog geometry, and the ROOM - the grid, the floor, the lights, the camera presets -
// stays global, because none of it names anything out of the tree.
//
// The damage INFLICTED is not here, at the other end of the same rule: destroyed hardpoints and
// drained pools reset on open, because the opening rules beat persistence. Nor is the SUBJECT's own
// state - its damage stage and detail level - which is per subject and resets with it.

import { DEFAULT_ATTACKER, type Attacker } from './attacker';
import { asRecord, boolean_, nullableString, number_, string_ } from './storedValue';

/** A saved attacker, with a name to recall it by. */
export interface AttackerPreset extends Attacker {
    id: string;
    name: string;
}

/** What one project remembers about the preview. */
export interface ProjectSettings {
    /** The weapon currently built in the panel. */
    attacker: Attacker;
    /** The weapons the reader has saved, in the order they saved them. */
    presets: AttackerPreset[];
    /**
     * The faction to tint with, by NAME.
     *
     * By name because the index differs per subject: "I am reviewing the Rebel roster" should
     * survive opening the next unit, and quietly fall back when that unit has no such faction. Which
     * is also why it is per PROJECT - the roster is the mod's, and a mod's own faction means nothing
     * in the next one.
     */
    faction: string | null;
    /** A colour chosen by hand instead of a faction's, as `#rrggbb`. */
    customColour: string | null;
}

/** An untouched project: the default weapon, nothing saved, and the model's own colours. */
export const DEFAULT_PROJECT_SETTINGS: ProjectSettings = {
    attacker: DEFAULT_ATTACKER,
    presets: [],
    faction: null,
    customColour: null,
};

/**
 * What was stored, defaulted field by field.
 *
 * Never trusts what it finds, for the reason given on {@link asRecord}'s module: this blob outlives
 * the build that wrote it.
 */
export function projectSettingsFrom(stored: unknown): ProjectSettings {
    const raw = asRecord(stored);

    return {
        attacker: attackerFrom(raw.attacker),
        presets: attackerPresetsFrom(raw.presets),
        faction: nullableString(raw.faction),
        customColour: nullableString(raw.customColour),
    };
}

/**
 * One stored attacker, field by field.
 *
 * Anything unreadable falls back to the default for THAT field rather than throwing: a string where
 * a number was expected must cost the reader their weapon, not everything else here.
 */
function attackerFrom(stored: unknown): Attacker {
    const raw = asRecord(stored);

    return {
        damage: number_(raw.damage, DEFAULT_ATTACKER.damage, { min: 0, max: 1_000_000 }),
        damageType: string_(raw.damageType, DEFAULT_ATTACKER.damageType),
        shield: boolean_(raw.shield, DEFAULT_ATTACKER.shield),
        energy: boolean_(raw.energy, DEFAULT_ATTACKER.energy),
        hitpoint: boolean_(raw.hitpoint, DEFAULT_ATTACKER.hitpoint),

        // An older stored attacker has none of these, and falls back to no blast - which is what it
        // behaved as when it was written.
        blastDamage: number_(raw.blastDamage, DEFAULT_ATTACKER.blastDamage,
            { min: 0, max: 1_000_000 }),
        blastRange: number_(raw.blastRange, DEFAULT_ATTACKER.blastRange,
            { min: 0, max: 1_000_000 }),
        blastDropoff: boolean_(raw.blastDropoff, DEFAULT_ATTACKER.blastDropoff),
        blastDropoffTiers: number_(raw.blastDropoffTiers, DEFAULT_ATTACKER.blastDropoffTiers,
            { min: 0, max: 16 }),
    };
}

/** The saved presets, dropping any that could not name themselves. */
function attackerPresetsFrom(stored: unknown): AttackerPreset[] {
    if (!Array.isArray(stored)) {
        return [];
    }

    const presets: AttackerPreset[] = [];

    for (const entry of stored) {
        const raw = asRecord(entry);
        const { id, name } = raw;

        // A preset with no name is a blank row in the list, and one with no id cannot be recalled
        // or deleted. Neither is worth keeping.
        if (typeof id !== 'string' || id === '' || typeof name !== 'string' || name === '') {
            continue;
        }

        presets.push({ id, name, ...attackerFrom(entry) });
    }

    return presets;
}
