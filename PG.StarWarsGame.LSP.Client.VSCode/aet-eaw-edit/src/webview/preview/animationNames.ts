// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Reading an Alamo animation filename.
//
// An `.ala` names nothing about itself - not its model, not what it does. Everything a reader can
// know before loading it is in the filename, which the exporter builds as
//
//     <model>_<action>_<take>.ala
//
// and 3736 of the 3771 shipped files follow it exactly. Three facts come out of that name and each
// is a different question:
//
//   the ACTION - what the unit is doing. 168 distinct ones over both trees, far too many to show
//   flat, so they gather into a handful of FAMILIES a modder already thinks in.
//
//   the STANCE - how it is being held. `crouchidle` is not a kind of crouching, it is the IDLE
//   played while the unit is spread out; `crouchmove` is the walk in that stance. Reading the
//   prefix as part of the action put those in the wrong families and lost the one fact that
//   explains them.
//
//   the TAKE - which of several recordings of that action this is. 385 of the 1718 model+action
//   pairs ship more than one, up to nine idles on the infantry, and the engine picks among them.
//   They are one row, not nine.
//
// Whether an action LOOPS falls out of the same reading: the idles and the locomotion do, and
// nothing else does unless the file says so in its own name.

/** The families, in the order they are shown. */
const FAMILIES = [
    'idle', 'move', 'attack', 'hit', 'death', 'travel', 'state', 'cinematic', 'other',
] as const;

export type AnimationFamily = typeof FAMILIES[number];

export const FAMILY_LABELS: Record<AnimationFamily, string> = {
    idle: 'Idle',
    move: 'Movement',
    attack: 'Attack',
    hit: 'Taking a hit',
    death: 'Death',
    travel: 'Landing and takeoff',
    state: 'State changes',
    cinematic: 'Cinematic',
    other: 'Other',
};

/** How an action is being held. */
export type AnimationStance = 'normal' | 'crouched' | 'deployed';

/**
 * The stance prefixes, and what to call them.
 *
 * `crouch` is the SPREAD_OUT ability - the reader's own explanation - so the label says the thing a
 * modder would search their XML for rather than the word the exporter happened to use.
 */
const STANCES: readonly { prefix: string; stance: AnimationStance; label: string }[] = [
    { prefix: 'crouch', stance: 'crouched', label: 'spread out' },
    { prefix: 'deployed_', stance: 'deployed', label: 'deployed' },
];

/**
 * The keyword rules, tried in this order.
 *
 * Order is the whole design. `attackidle` matches both the attack rule and the idle rule, and only
 * one of those readings is right; `chokedeath` matches death and attack; `flylandidle` matches
 * idle, travel and movement. Putting the more specific reading first is what settles each of them,
 * so a rule may only ever be moved with a case in the test that says why.
 */
const RULES: [AnimationFamily, readonly string[]][] = [
    // Before movement: `flylandidle` is a hover, not a flight, and `attackidle` is not an attack.
    ['idle', ['idle', 'attention', 'hold', 'disabled', 'cooldown']],
    // Before attack and before death: a flinch is a reaction, whatever caused it.
    ['hit', ['flinch', 'dodge']],
    ['death', ['die', 'death', 'crushed', 'destruct']],
    // Before movement, so `running_charge` stays an attack where the name says so. It does NOT
    // catch `force_run`, which carries no attack word and files under movement - the loop rule
    // excludes that one as an ability instead. See `isAbility`.
    ['attack', [
        'attack', 'blaster', 'bombtoss', 'choke', 'release', 'pound', 'demolition', 'charge',
    ]],
    // Before movement: a rope slide and a landing are arrivals, not locomotion.
    ['travel', ['land', 'takeoff', 'rope', 'drop', 'lift', 'jump']],
    // TURNING lives here. It was a family of its own, which put a unit's locomotion in two places -
    // the reader's words: "turnl and turnr are technically movement animations".
    ['move', ['move', 'run', 'walk', 'fly', 'turnl', 'turnr', 'turn_', 'rotat', 'spin']],
    // No bare 'on' or 'off' here. 'on' is a substring of half the language - it swallowed
    // `transition` - and 'power' and 'shield' already carry every on/off action the corpus ships.
    ['state', ['build', 'deploy', 'power', 'shield', 'open', 'close', 'repair', 'hack', 'heal']],
    ['cinematic', ['cinematic', 'talk', 'celebrate', 'dance', 'alarm', 'warning', 'trans', 'hc_']],
];

/**
 * Actions that are a way IN or OUT of another one, and so never loop.
 *
 * The corpus marks its own, which is why this is a list of shapes rather than a guess: 80 files end
 * `_begin` or `_end`, 78 end `_half` or `_quarter` (a turn of a fixed amount, not a turn held), 58
 * are `movestart` or `move_endone..four`, and 128 are some spelling of `transition` - the exporter
 * shipped `transiotion` too, and that is not a typo anyone can fix now.
 */
function isTransition(action: string): boolean {
    return /_(begin|end|half|quarter)$/.test(action)
        || /^move(start|_end)/.test(action)
        || action.includes('trans');
}

/** Actions the author has named a loop outright, whatever family they land in. */
function saysLoop(action: string): boolean {
    return action.endsWith('_loop');
}

/**
 * Actions belonging to a special ABILITY, which the reader's rule excludes by name.
 *
 * Only the Force ones need saying. Every other ability action - `block_blaster`, `bombtoss`,
 * `self_destruct`, `hc_draw` - already lands in a family that does not loop, so it is the keyword
 * rules that exclude them and not this. `force_run` is the case that proves the exception: it
 * carries `run`, files under movement on that, and would loop for a reason that has nothing to do
 * with what it is. `ri_padowan_force_run` is the same clip under a second model, hence `includes`
 * rather than a prefix test.
 */
function isAbility(action: string): boolean {
    return action.includes('force_');
}

/**
 * The families whose actions run on a loop.
 *
 * The reader's rule, in their words: an idle, or something that "belongs to a group that is meant
 * to loop (run, walk, ...), but never transitions and special ability related animations".
 */
const LOOPING: readonly AnimationFamily[] = ['idle', 'move'];

/** Everything one filename says. */
export interface ClipName {
    /** The filename, which is what actually gets loaded. */
    name: string;
    /** The action, with its stance prefix and take number taken off. */
    action: string;
    /** Which recording of that action this is, or null where the file carries no number. */
    take: number | null;
    stance: AnimationStance;
    family: AnimationFamily;
    /** Whether the clip is meant to run on a loop, and so whether Repeat opens on. */
    loops: boolean;
}

/** Reads one clip's filename. */
export function readClip(model: string, file: string): ClipName {
    const stem = file.replace(/\.[^.]*$/, '');
    const prefix = `${model.toLowerCase()}_`;

    // A clip whose name the model does not prefix keeps its whole stem. 50 of the shipped
    // animations are like that - showing the full name is a worse label than a trimmed one but a
    // far better outcome than hiding the clip because a naming rule did not fire.
    const tail = stem.toLowerCase().startsWith(prefix)
        ? stem.slice(prefix.length)
        : stem;

    const numbered = /^(.*?)_(\d+)$/.exec(tail);
    const withStance = (numbered?.[1] ?? tail).toLowerCase();
    const take = numbered === null ? null : Number.parseInt(numbered[2], 10);

    const matched = STANCES.find(entry => withStance.startsWith(entry.prefix));
    const action = matched === undefined
        ? withStance
        : withStance.slice(matched.prefix.length);

    const family = familyOf(action);

    return {
        name: file,
        action,
        take,
        stance: matched?.stance ?? 'normal',
        family,
        loops: loopsBy(action, family),
    };
}

/**
 * Whether an action runs on a loop, in the order the questions have to be asked.
 *
 * A transition is never a loop however it is named - `force_reveal_begin` is a way IN to something,
 * and the file saying `_loop` about a sibling does not make it one. Then the author's own word, then
 * the ability exclusion, then the family.
 */
function loopsBy(action: string, family: AnimationFamily): boolean {
    if (isTransition(action)) {
        return false;
    }

    if (saysLoop(action)) {
        return true;
    }

    return !isAbility(action) && LOOPING.includes(family);
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

/** One recording of an action. */
export interface AnimationTake {
    /** The filename, which is what gets loaded. */
    name: string;
    /** Its number, or null where the file carries none. */
    take: number | null;
    /** How the take is named on its chip - `00`, or the whole stem where there is no number. */
    label: string;
}

/** One thing the unit does, and the takes of it the model ships. */
export interface AnimationAction {
    /** Stable across reloads, and unique within a family: the stance and the action. */
    id: string;
    label: string;
    action: string;
    stance: AnimationStance;
    loops: boolean;
    /** In take order, lowest first. Never empty. */
    takes: AnimationTake[];
}

export interface AnimationGroup {
    family: AnimationFamily;
    label: string;
    actions: AnimationAction[];
}

/**
 * The clips of one model, gathered into families and then into actions.
 *
 * Families come out in `FAMILIES` order rather than in the order the model happens to use them, so
 * Death is always in the same place whatever was opened. Empty families are left out entirely - a
 * heading over nothing is a claim that the model should have had one.
 *
 * Within a family the actions keep the order their first take appears in, which is the order the
 * index handed them over.
 */
/** Just enough of a scene to say which model its clips are named after. */
interface ClipNamingScene {
    animationSource?: string | null;
    parts?: readonly { origin?: string | null; modelRef?: string | null }[];
}

/**
 * The model the clips are NAMED after, which is not always the one on stage.
 *
 * `Land_Model_Anim_Override_Name` and its space counterpart hand a unit another model's animation
 * set, and the clips keep the SOURCE model's name - `ri_infantry_idle_00.ala` on a subject called
 * `NI_SandPeople_C`. Keying the library off the hull strips no prefix at all, so every action reads
 * the source model's name back at you and the whole set collapses into one family.
 *
 * This never showed while the identical-skeleton rule was dropping those clips before they reached
 * the client. 672 of them load now, across 12 of the 29 shipped override pairs, so the naming has
 * to follow them.
 */
export function clipNamingModel(scene: ClipNamingScene | null | undefined): string {
    const parts = scene?.parts ?? [];
    const hull = parts.find(part => part.origin === 'Hull') ?? parts[0];
    const named = scene?.animationSource ?? hull?.modelRef ?? '';

    return named.replace(/\.[^.]*$/, '');
}

export function groupAnimations(model: string, files: readonly string[]): AnimationGroup[] {
    const byFamily = new Map<AnimationFamily, Map<string, AnimationAction>>();

    for (const file of files) {
        const read = readClip(model, file);
        const actions = byFamily.get(read.family) ?? new Map<string, AnimationAction>();
        const id = `${read.stance}:${read.action}`;
        const existing = actions.get(id);

        const take: AnimationTake = {
            name: file,
            take: read.take,
            label: read.take === null ? prettify(read.action) : pad(read.take),
        };

        if (existing === undefined) {
            actions.set(id, {
                id,
                label: labelFor(read),
                action: read.action,
                stance: read.stance,
                loops: read.loops,
                takes: [take],
            });
        } else {
            existing.takes.push(take);
        }

        byFamily.set(read.family, actions);
    }

    return FAMILIES
        .filter(family => byFamily.has(family))
        .map(family => ({
            family,
            label: FAMILY_LABELS[family],
            actions: [...(byFamily.get(family) ?? new Map()).values()]
                .map(action => ({ ...action, takes: [...action.takes].sort(byTake) })),
        }));
}

/** Lowest take first, and a take with no number sorts to the front - there is only ever one. */
function byTake(a: AnimationTake, b: AnimationTake): number {
    return (a.take ?? -1) - (b.take ?? -1);
}

/** `3` reads as `03`, so a column of take chips lines up. */
function pad(take: number): string {
    return take.toString().padStart(2, '0');
}

/**
 * Names a handful of actions the exporter spelled for itself rather than for a reader.
 *
 * Only where the raw name is genuinely unreadable and the action is common: turning is 274 files,
 * and `Turnl` is not a word. Everything else is prettified and left alone - a table of 168 entries
 * would be a translation, and it would rot.
 */
const ACTION_LABELS: Record<string, string> = {
    turnl: 'Turn left',
    turnr: 'Turn right',
    turn_left: 'Turn left',
    turn_right: 'Turn right',
};

/** What an action row is called, with its stance said out loud when it has one. */
function labelFor(read: ClipName): string {
    const base = ACTION_LABELS[read.action] ?? prettify(read.action);
    const stance = STANCES.find(entry => entry.stance === read.stance);

    return stance === undefined ? base : `${base} (${stance.label})`;
}

/** `turnr_quarter` reads as `Turnr quarter`. Enough to scan; not a translation. */
function prettify(action: string): string {
    const spaced = action.replace(/_/g, ' ').trim();

    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/**
 * What the playhead readout says: the action, and which take of it.
 *
 * The filename repeats the model's name on every clip, which is the one part of it the reader
 * already knows. A rouletting action says so, because the clip under the playhead is about to
 * change on its own and a readout that did not mention it would look like a bug.
 */
export function playheadLabel(
    action: AnimationAction | null, clip: string, rouletting: boolean,
): string {
    if (action === null) {
        return clip.replace(/\.[^.]*$/, '');
    }

    const take = action.takes.find(entry => entry.name === clip);
    const shown = action.takes.length > 1 && take !== undefined
        ? `${action.label} ${take.label}`
        : action.label;

    return rouletting ? `${shown} - random` : shown;
}
