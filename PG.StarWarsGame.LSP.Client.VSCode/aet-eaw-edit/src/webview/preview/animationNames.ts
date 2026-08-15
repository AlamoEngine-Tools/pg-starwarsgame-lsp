// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Reading an Alamo animation filename.
//
// An `.ala` names nothing about itself - not its model, not what it does. Everything a reader can
// know before loading it is in the filename, which the exporter builds as
// `<model>_<action>[_<qualifier>]_<take>.ala`:
//
//     Ev_stardestroyer_idle_00.ala      Ei_trooper_attackflinchl_02.ala
//     Ai_rancor_attack_00.ala           Nv_frigate_turnr_quarter_00.ala
//
// The model prefix is the same on every clip in a list, so it is noise; the take number is a variant
// of one action. What is left - the action - is the only part worth reading, and there are 132
// distinct ones across the 1363 shipped files. That is far too many to show flat, but they fall into
// a handful of families a modder already thinks in: idles, movement, attacks, deaths.
//
// The families are keyword rules over the action, in a FIXED order, because several actions carry
// more than one keyword and the first match has to be the right one. `attackidle` is the case that
// proves it: an idle stance held while armed, not an attack.

/** The families, in the order they are shown. */
const FAMILIES = [
    'idle', 'move', 'turn', 'attack', 'hit', 'death', 'travel', 'state', 'cinematic', 'other',
] as const;

export type AnimationFamily = typeof FAMILIES[number];

export const FAMILY_LABELS: Record<AnimationFamily, string> = {
    idle: 'Idle',
    move: 'Movement',
    turn: 'Turning',
    attack: 'Attack',
    hit: 'Taking a hit',
    death: 'Death',
    travel: 'Landing and takeoff',
    state: 'State changes',
    cinematic: 'Cinematic',
    other: 'Other',
};

/**
 * The keyword rules, tried in this order.
 *
 * Order is the whole design. `attackidle` matches both the attack rule and the idle rule, and only
 * one of those readings is right; `chokedeath` matches death and attack; `flylandidle` matches idle,
 * travel and movement. Putting the more specific reading first is what settles each of them, so a
 * rule may only ever be moved with a case in the test that says why.
 */
const RULES: [AnimationFamily, readonly string[]][] = [
    // Before movement: `flylandidle` is a hover, not a flight, and `attackidle` is not an attack.
    ['idle', ['idle', 'attention', 'hold', 'disabled', 'cooldown']],
    // Before attack and before death: a flinch is a reaction, whatever caused it.
    ['hit', ['flinch', 'dodge']],
    ['death', ['die', 'death', 'crushed', 'destruct']],
    // Before movement, so `force_run` and `running_charge` stay attacks where the name says so.
    ['attack', [
        'attack', 'blaster', 'bombtoss', 'choke', 'release', 'pound', 'demolition', 'charge',
    ]],
    ['turn', ['turnl', 'turnr', 'rotat']],
    // Before movement: a rope slide and a landing are arrivals, not locomotion.
    ['travel', ['land', 'takeoff', 'rope', 'drop', 'lift', 'jump']],
    ['move', ['move', 'run', 'walk', 'fly']],
    // No bare 'on' or 'off' here. 'on' is a substring of half the language - it swallowed
    // `transition` - and 'power' and 'shield' already carry every on/off action the corpus ships.
    ['state', ['build', 'deploy', 'power', 'shield', 'open', 'close', 'repair', 'hack', 'heal']],
    ['cinematic', ['cinematic', 'talk', 'celebrate', 'dance', 'alarm', 'warning', 'trans', 'hc_']],
];

/**
 * What a clip DOES, from its filename.
 *
 * The model prefix goes because it is the same on every clip in the list. It is matched
 * case-insensitively - the index holds `EV_STARDESTROYER_DIE_00.ALA` and `Ev_stardestroyer_die_00.ala`
 * alike - but the returned text keeps the file's own casing, since that is what the file is called.
 *
 * A clip whose name the model does not prefix keeps its whole stem. 50 of the shipped animations are
 * like that; showing the full name is a worse label than a trimmed one but a far better outcome than
 * hiding the clip because a naming rule did not fire.
 */
export function actionOf(model: string, file: string): string {
    const stem = file.replace(/\.[^.]*$/, '');
    const prefix = `${model.toLowerCase()}_`;

    return stem.toLowerCase().startsWith(prefix) ? stem.slice(prefix.length) : stem;
}

/** Which family an action belongs to. */
export function familyOf(action: string): AnimationFamily {
    const lower = action.toLowerCase();

    for (const [family, keywords] of RULES) {
        if (keywords.some(keyword => lower.includes(keyword))) {
            return family;
        }
    }

    return 'other';
}

/** One clip, ready to show. */
export interface AnimationItem {
    /** The filename, which is what actually gets loaded. */
    name: string;
    /** The action, spelled for a person. */
    label: string;
}

export interface AnimationGroup {
    family: AnimationFamily;
    label: string;
    items: AnimationItem[];
}

/**
 * The clips of one model, gathered into families.
 *
 * Families come out in `FAMILIES` order rather than in the order the model happens to use them, so
 * Death is always in the same place whatever you opened. Empty families are left out entirely - a
 * heading over nothing is a claim that the model should have had one.
 */
export function groupAnimations(model: string, files: readonly string[]): AnimationGroup[] {
    const byFamily = new Map<AnimationFamily, AnimationItem[]>();

    for (const name of files) {
        const action = actionOf(model, name);
        const family = familyOf(action);
        const items = byFamily.get(family) ?? [];

        items.push({ name, label: prettify(action) });
        byFamily.set(family, items);
    }

    return FAMILIES
        .filter(family => byFamily.has(family))
        .map(family => ({
            family,
            label: FAMILY_LABELS[family],
            items: byFamily.get(family) ?? [],
        }));
}

/** `turnr_quarter_00` reads as `Turnr quarter 00`. Enough to scan; not a translation. */
function prettify(action: string): string {
    const spaced = action.replace(/_/g, ' ').trim();

    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
