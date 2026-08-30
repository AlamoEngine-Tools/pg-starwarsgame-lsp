// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Whether a row in the model tree is drawn, and WHICH rule decided.
//
// One ordered chain, evaluated the same way for every row, every time anything changes. Each link
// either has an opinion or declines to have one, and the first opinion wins. Two kinds of link:
//
//   VETOES - a master toggle and a hidden ancestor. They can only ever hide. A master switch that
//   is ON does not force anything on; it simply declines to veto.
//
//   DECIDERS - the reader's own override, then a playing clip's visibility track, then the level
//   gates, then the file's own flag. Each can hide OR show.
//
// The order is the design:
//
//   master toggle > hidden ancestor > the reader > the animation > the level > the file
//
// The reader sits above the animation because full control over what is on screen is the point of
// the model view: switching a mesh off during a clip has to actually switch it off, or the tick
// looks broken. The animation sits above the level and the file because a clip is the model as its
// author animated it, and where nobody has overridden it, it is the truth.
//
// The reader sitting above the animation does NOT mean the two fight. Starting a clip CLEARS every
// row override - see `clearedForPlayback` - because watching an animation is checking whether the
// animation works, and that can only be answered against the model as authored. A clip resets the
// pose each time it runs; it resets the visibilities with it. The one thing that still applies is
// the master toggles, which are not statements about the model at all: effects switched off because
// they obstruct the view stay off while the clip plays.
//
// `because` is not decoration. The old tree could say a row was hidden but never why, so a reader
// looking at a black scene and a tree full of ticks had nothing to go on. Every row can now name
// the rule that decided it.

/** The reader's word on one row. Absent means they have not said anything about it. */
export type RowOverride = 'shown' | 'hidden';

/** A global switch a row answers to, such as the effects or hardpoint master. */
export interface MasterToggle {
    id: string;
    on: boolean;
}

/** Everything the chain needs to know about one row. */
export interface RowFacts {
    /** The file's own visible flag. */
    inFile: boolean;

    /**
     * What the model's RESTING clip says about this row, when it ships one.
     *
     * The engine never stands still - a unit is always playing something - so a model's true
     * resting state is its idle clip, not its file. The AT-AT is the case that settled it: its idle
     * keys both barrel muzzles off for all 81 frames, and the file leaves those flash meshes
     * unhidden precisely BECAUSE the animation owns that state. Read from the file alone, two
     * muzzle flashes sit on screen that nobody ever sees in game.
     *
     * Absent when the model ships no idle clip, or when that clip says nothing about this row -
     * then the file is the baseline, which is the user's rule: idle if we have it, the file
     * otherwise.
     */
    resting?: boolean;

    /**
     * Switched off by something other than that flag: a collision or shadow archetype, or an ALT,
     * LOD or damage level that is not the current one.
     */
    gated: boolean;

    /**
     * What a playing clip's visibility track says on this frame, when one names this row.
     *
     * Absent when nothing is playing or the clip says nothing about it, which is NOT the same as
     * false - treating it as false is how an animation came to blink meshes out.
     */
    animated?: boolean;

    /** The reader's override, if they have set one. */
    override?: RowOverride;

    /** The masters this row subscribes to, widest first. */
    masters: readonly MasterToggle[];

    /** The name of the nearest ancestor that is itself hidden, when there is one. */
    ancestorHidden?: string;

    /**
     * The same, but for the ancestor as the MODEL leaves it - absent when the ancestor is only
     * hidden because the reader hid it.
     *
     * Separate because the two answers genuinely differ, and conflating them made a whole subtree
     * claim the model did not draw it the moment one bone above was unticked. What the model says
     * about a child is what it would say if nobody had touched the parent.
     */
    ancestorHiddenAuthored?: string;
}

/**
 * Which link of the chain settled a row.
 *
 * The typed half of `because`. It exists because the tree's eye has to know whether the decision was
 * this row's own - the reader, the clip, the level, the file - or came from ABOVE it, since only the
 * first kind is something the eye can change. Reading that out of `because` would mean parsing a
 * string written for a person, which is the shape `boneIds.ts` records this project paying for three
 * times.
 */
export type VisibilityLink =
    'master' | 'ancestor' | 'you' | 'animation' | 'level' | 'idle' | 'file';

/** Whether the row is drawn, and the rule that settled it. */
export interface Resolution {
    visible: boolean;

    /** Which link decided. */
    decidedBy: VisibilityLink;

    /**
     * The same answer for a person: `master:<id>`, `ancestor:<name>`, `you`, `animation`, `level`,
     * `idle` or `file`.
     *
     * Written for a tooltip, never parsed - `decidedBy` is what code asks.
     */
    because: string;

    /**
     * What the row would do if the reader had said nothing about it.
     *
     * The tree renders a row in italics when this is false, so a ticked row that only stands
     * because someone ticked it looks different from one the model draws by itself - which is the
     * whole question when a shadow volume or a collision hull is on screen.
     *
     * Not the inverse of the override: hiding a mesh the model draws leaves it authored VISIBLE.
     * Only the model's own silence about a row makes it italic.
     */
    authored: boolean;
}

/** Runs the chain for one row. */
export function resolveRow(facts: RowFacts): Resolution {
    const decided = decide(facts, facts.override);

    // The same chain, run again with the reader taken back out - of this row AND of the rows above
    // it. Deriving it instead ("gated or not in the file") would miss the animation, which speaks
    // for the model too.
    const authored = decide(
        { ...facts, ancestorHidden: facts.ancestorHiddenAuthored }, undefined);

    return { ...decided, authored: authored.visible };
}

/** The chain itself, for one reading of what the reader has said. */
function decide(
    facts: Omit<RowFacts, 'ancestorHiddenAuthored'>, override: RowOverride | undefined,
): Omit<Resolution, 'authored'> {
    // Vetoes first, widest first. A master switch beats an ancestor because it is the broader
    // statement: "no effects at all" outranks "not this limb".
    for (const master of facts.masters) {
        if (!master.on) {
            return { visible: false, decidedBy: 'master', because: `master:${master.id}` };
        }
    }

    if (facts.ancestorHidden !== undefined) {
        return { visible: false, decidedBy: 'ancestor', because: `ancestor:${facts.ancestorHidden}` };
    }

    if (override !== undefined) {
        return { visible: override === 'shown', decidedBy: 'you', because: 'you' };
    }

    if (facts.animated !== undefined) {
        return { visible: facts.animated, decidedBy: 'animation', because: 'animation' };
    }

    if (facts.gated) {
        return { visible: false, decidedBy: 'level', because: 'level' };
    }

    // Below the level gate on purpose: a mesh tagged for another damage or detail state is not part
    // of this one whatever the idle clip has to say about it.
    if (facts.resting !== undefined) {
        return { visible: facts.resting, decidedBy: 'idle', because: 'idle' };
    }

    return { visible: facts.inFile, decidedBy: 'file', because: 'file' };
}

/**
 * The clip a model rests in, out of the ones it ships.
 *
 * By name, because that is the only thing that marks one: Alamo has no flag for "this is the pose a
 * unit stands in". Sorted before picking, so a model with several idles opens the same way twice.
 */
export function restingClip(clips: readonly string[]): string | undefined {
    return [...clips].sort().find(clip => clip.toLowerCase().includes('idle'));
}

/**
 * The clip a model dies on, out of the ones it ships.
 *
 * By name, like {@link restingClip}, and for the same reason - nothing in the file marks one. The
 * word has to stand on its own between separators rather than merely appear, because the exporter
 * writes the action as its own segment of `<model>_<action>_<take>.ala` and `diehard` is not a
 * death.
 *
 * Falls back to the first clip the model ships. The host hands the server every clip the SCENE
 * found, so the list is not guaranteed to name a death at all, and geometry that plays nothing at
 * the moment it is spawned to die is worse than geometry playing the one thing it has. Sorted
 * before picking, so a model with several deaths dies the same way twice.
 */
export function deathClip(clips: readonly string[]): string | undefined {
    const sorted = [...clips].sort();

    return sorted.find(clip => /(^|[^a-z])die([^a-z]|$)/i.test(clip)) ?? sorted[0];
}

/** One row's override changing, where null means "give it back to the model". */
export interface OverrideChange {
    row: string;
    override: RowOverride | null;
}

/**
 * What clicking one row's tick does to it and to everything beneath it.
 *
 * The two directions are deliberately NOT symmetrical:
 *
 * - Hiding pushes an explicit `hidden` all the way down, which is what a tree is expected to do -
 *   switching off an arm switches off the hand.
 * - Showing forces only the CLICKED row on and CLEARS its descendants. Forcing them on as well
 *   would drag every collision hull and shadow volume under that bone into view, since those are
 *   gated off rather than absent. Cleared, each child goes back to whatever the chain says.
 *
 * The clicked row is forced rather than cleared for the opposite reason: a shadow volume is gated
 * off and is exactly the row worth ticking, so clearing it would leave it invisible.
 */
export function toggleRow(
    rowId: string, descendantIds: readonly string[], effective: boolean, authored: boolean,
): OverrideChange[] {
    // From what the reader can SEE, never from a stored flag. The old tree computed the next state
    // from a value it was not the one writing, which is why the tick stuck.
    return setRow(rowId, descendantIds, !effective, authored);
}

/**
 * What it takes to make one row visible, or not, along with everything beneath it.
 *
 * The counterpart of `toggleRow` and the primitive underneath it: this one is told what the reader
 * WANTS, where `toggleRow` is told what they can currently see and works it out.
 *
 * Both exist because the callers genuinely differ. A checkbox's change event already carries the
 * state being asked for, and a button labelled "Hide" plainly says what it will do; a bare tick on
 * a row that has no stored state has only what is on screen to go on. Handing a wanted state to the
 * function that expects a seen one inverts it silently - which is exactly what happened: the tick
 * kept setting each row to the state it was already in and so appeared dead, and Hide showed things
 * while Show hid them.
 */
export function setRow(
    rowId: string, descendantIds: readonly string[], visible: boolean, authored: boolean,
): OverrideChange[] {
    // Stored only where it DISAGREES with the model - `authored` is what this row would do if the
    // reader had never touched it. Asking for what the model already does is not a statement, it is
    // the absence of one, and writing it down anyway is what made a tick permanent: an override
    // outranks the animation, the level and the file for good, so one tick on an ability's effect
    // row broke that ability's switch until the whole model was reset.
    //
    // This is the way BACK, and it is what gives the tree's eye its third state - see `rowEye`. It
    // takes nothing away: a shadow volume is gated off, so asking for it genuinely disagrees and is
    // stored, which is the row worth ticking in the first place.
    const override: RowOverride | null =
        visible === authored ? null : visible ? 'shown' : 'hidden';

    return [
        { row: rowId, override },
        // The subtree is unchanged: hiding an arm hides the hand explicitly, and showing it clears
        // them so each child goes back to whatever the chain says. Only the CLICKED row is the
        // reader making a statement about one thing.
        ...descendantIds.map(row => ({
            row,
            override: visible ? null : 'hidden' as const,
        })),
    ];
}

/**
 * The row overrides that survive a clip starting: none.
 *
 * Watching an animation means checking whether the animation works, and that question can only be
 * answered against the model as its author left it. Every clip already resets the pose each time it
 * runs, and the visibilities are part of that pose - a mesh switched off by hand three minutes ago
 * would otherwise quietly answer the question wrong.
 *
 * The master toggles are untouched on purpose. They are not per-row statements about the model:
 * effects hidden because they obstruct the view are still hidden, which is the one thing anyone
 * legitimately wants to keep switched off while watching a clip.
 */
export function clearedForPlayback(): Map<string, RowOverride> {
    return new Map();
}

/**
 * `because` as a sentence, for the row's own tooltip.
 *
 * The reason a row looks the way it does was computed and then thrown away - the tree could say a
 * row was hidden but never why, which is what left a reader staring at a black scene and a list of
 * ticks. The chain names the link that decided; this puts it in words.
 */
export function becauseText(because: string): string {
    if (because.startsWith('master:')) {
        return `Hidden by the ${because.slice('master:'.length)} switch`;
    }

    if (because.startsWith('ancestor:')) {
        return `Hidden with ${because.slice('ancestor:'.length)}, above it`;
    }

    // One clause each. These are read standing over a row in a tree that can be a hundred deep,
    // so anything that needs a second sentence is not going to be read at all - "Set by hand.
    // Reset puts it back" spent half its length advertising a button that is already on screen.
    // `idle` is NOT the rest pose. The rest pose is the model with nothing driving the skeleton;
    // this is the model's own idle clip running and its visibility track deciding the row, which is
    // why it reads as a sibling of `animation` rather than as a pose.
    switch (because) {
        case 'you': return 'Set by hand';
        case 'idle': return 'Set by the idle animation';
        case 'animation': return 'Set by the animation playing';
        case 'level': return 'Not in this damage or detail level';
        default: return 'As the model draws it';
    }
}

/**
 * The same row, asked only about the things that apply to everything BENEATH it.
 *
 * A merged bone/mesh row writes two different things: whether that mesh is drawn, and whether the
 * bone is visible - which prunes its entire subtree. Deciding both from one answer is wrong in one
 * direction: what the FILE says about a mesh, and what the current level says about it, are
 * statements about that mesh alone. Everything else - the reader's own tick, a master switch, a
 * hidden ancestor, a clip hiding the bone - is a statement about the bone and takes the subtree
 * with it, which is what a tree means.
 *
 * The AT-AT is the case that proved it. Its particle proxies are bones named after the effect they
 * carry, each with a marker mesh of the same name that the file marks hidden. Merged into one row,
 * the mesh's flag pruned the bone's subtree, and the effect attached to that bone was vetoed by an
 * ancestor it could not see - so every fire, smoke and explosion proxy on the model was dark and no
 * amount of ticking brought one back.
 */
/**
 * The same row, asked about the EFFECT it carries rather than the geometry.
 *
 * One difference, and it is the engine's: **Alamo does not inherit visibility.**
 * `RenderObject::Render` draws a submesh when `GetBoneVisibility(mesh.bone)` says so - that bone's
 * own track and nothing above it - and a particle proxy is spawned and killed on
 * `GetVisibleEvent` / `GetInvisibleEvent` of `proxy.bone->index` alone. An ancestor never speaks
 * for it.
 *
 * The shipped data depends on that. A proxy bone is a CHILD of the piece its blast covers, and the
 * Nebulon-B's death clip keys `p_explosion_big00#12` ON for exactly the one frame `Busted_00#11`
 * is keyed OFF. Inherit, and that frame cannot be reached at all.
 *
 * The reader stays, because the tree is not the engine: a bone row is a subtree switch, and
 * unticking a chunk has to take its effects with it. That is the ONE ancestor that still counts,
 * and `ancestorHiddenAuthored` is what tells the two apart - it is set only when the MODEL is the
 * reason the ancestor is hidden.
 */
export function effectFacts(facts: RowFacts): RowFacts {
    const readerHidIt = facts.ancestorHidden !== undefined
        && facts.ancestorHiddenAuthored === undefined;

    return {
        ...facts,
        ancestorHidden: readerHidIt ? facts.ancestorHidden : undefined,
        ancestorHiddenAuthored: undefined,
    };
}

/**
 * The same row, asked about a mesh the XML names as a hardpoint's `Damage_Decal`.
 *
 * Its state is the damage RULE's, not the file's. Told by the user, and the shipped art agrees:
 * over the material extras, **0 of 8** `_Blast` meshes on `EV_StarDestroyer.ALO` and **0 of 26** on
 * `EV_ExecutorStarDestroyer.ALO` are marked hidden. Every scorch mark in the corpus says "draw me",
 * so an undamaged hull would wear all of them - the engine cannot be reading the file flag here,
 * and neither may this.
 *
 * The same question, and the same answer, as a particle proxy's `gateVisible`. `Damage_Particles`
 * is the identical case and `Engine_Particles` is its inverse - on while the hardpoint LIVES - and
 * both already come through `effectFacts`, so this is the last of the three still asking the file.
 *
 * `gated` is deliberately untouched. That link is the LEVEL - a decal tagged for another ALT or LOD
 * is not part of this configuration at all - and it is where the damage rule used to be folded in,
 * which made a row's tooltip blame the level for a damage decision.
 */
export function damageMeshFacts(facts: RowFacts, shown: boolean): RowFacts {
    return { ...facts, inFile: shown };
}

export function subtreeFacts(facts: RowFacts): RowFacts {
    // `resting` stays: it comes from a clip's track, which names a BONE, so it is a statement about
    // the whole subtree in the same way a playing clip's is.
    return { ...facts, inFile: true, gated: false };
}
