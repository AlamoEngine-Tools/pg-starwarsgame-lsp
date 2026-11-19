// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Whether a part in the model tree is drawn, and WHICH switch decided.
//
// Every part of a game object - a mesh, an effect, an attached model - carries four independent
// switches, and it is drawn only when all four say shown. No switch can show a part another one
// hides:
//
//   ANIMATION - what the clip running says about the part's OWN bone, frame by frame. Silent when
//   nothing speaks for that bone.
//   STATE     - one bit with many writers, and the last write is what the part has. The model file
//   writes it first, from its hidden flag; the game's rules write over it later - see `UnitRules`.
//   ALT       - whether the part belongs to the current damage level.
//   LOD       - whether it belongs to the current detail level.
//
// No switch is inherited. A part is not hidden because a bone above its own bone is hidden. The one
// structural exception, an attached hardpoint model, is one part of the unit and is hidden as a whole
// outside this chain, on its part root.
//
// Around the four sit the PREVIEW's own controls, which are not statements about the model:
//
//   master toggle > a row the reader hid above this one > the reader's own word on this row
//
// A master that is off and a row the reader switched off above this one can only hide. The reader's
// word on the row itself beats every switch in both directions, because full control over what is on
// screen is the point of the model view - a shadow volume is exactly the row worth ticking on.
//
// Starting a clip CLEARS every row override - see `clearedForPlayback` - because watching an
// animation is checking whether the animation works, and that can only be answered against the model
// as authored. The master toggles stay: effects switched off because they obstruct the view stay off
// while the clip plays.
//
// `because` is not decoration. The old tree could say a row was hidden but never why, so a reader
// looking at a black scene and a tree full of ticks had nothing to go on. Every row names the switch
// that decided it.

import * as l10n from "@vscode/l10n";

import {
    type MaterialExtras,
    isVisibleAtAlt,
    isVisibleAtLod,
    resolveMaterial,
} from "./materials";

/** The reader's word on one row. Absent means they have not said anything about it. */
export type RowOverride = "shown" | "hidden";

/** A global switch a row answers to, such as the effects or hardpoint master. */
export interface MasterToggle {
    id: string;
    on: boolean;
}

/**
 * Who last wrote a part's state switch.
 *
 * `file` is the model file's own hidden flag, written when the model loads. `rules` is any of the
 * game's rules written over it in a unit preview - a hardpoint, an ability, the state a unit is
 * created in. A later rule only adds a writer; it never touches the AND below.
 */
export type StateWriter = "file" | "rules";

/** The state switch: one bit with many writers, and the last write is what the part has. */
export interface StateSwitch {
    shown: boolean;
    writer: StateWriter;
}

/** The model file's write of the state switch, which every part starts from. */
export function fileState(shown: boolean): StateSwitch {
    return { shown, writer: "file" };
}

/** A rule of the game writing the state switch, over whatever was written before. */
export function ruleState(shown: boolean): StateSwitch {
    return { shown, writer: "rules" };
}

/**
 * A mesh's own switches as its model file sets them: the state its hidden flag writes, the damage
 * and detail level it is tagged for, and whether it is a surface at all.
 *
 * One answer for every mesh the preview draws - the rows of the subject and the parts of a death
 * clone or a wreck alike - so the two cannot read the file differently.
 */
export function meshSwitches(
    extras: MaterialExtras,
    alt: number,
    lod: number,
): Pick<RowFacts, "state" | "alt" | "lod" | "nonVisual"> {
    return {
        state: fileState(extras.alamoHidden !== true),
        alt: isVisibleAtAlt(extras, alt),
        lod: isVisibleAtLod(extras, lod),
        nonVisual: resolveMaterial(extras).hidden,
    };
}

/**
 * An effect's state switch, from its proxy's own flag and what the effect rules now say.
 *
 * The rules decide the effect as a whole, so the write is put down to the FILE wherever they agree
 * with the proxy's flag: there is nothing the rules changed for the reader to be told about.
 */
export function effectState(
    startsVisible: boolean,
    drawsNow: boolean,
): StateSwitch {
    return drawsNow === startsVisible
        ? fileState(drawsNow)
        : ruleState(drawsNow);
}

/** Where the animation switch's answer came from. */
export type AnimationSource =
    /** The clip being played. */
    | "clip"
    /** The idle a unit stands in while nothing else plays - see `UnitRules.idle`. */
    | "idle"
    /** The model file's bone flag, while no clip runs - see `poseSays`. */
    | "pose";

/** The animation switch, when something speaks for the part's own bone. */
export interface AnimationSwitch {
    shown: boolean;
    source: AnimationSource;
}

/** Everything the chain needs to know about one part. */
export interface RowFacts {
    /** The state switch, as its last writer left it. */
    state: StateSwitch;

    /**
     * The animation switch: what the clip running says about the part's own bone on this frame.
     *
     * Absent when nothing is playing, or the clip says nothing about that bone, which is NOT the
     * same as hidden - treating silence as a hide is how an animation came to blink meshes out.
     */
    animation?: AnimationSwitch;

    /** The damage-level switch: whether the part belongs to the current ALT. */
    alt: boolean;

    /** The detail-level switch: whether the part belongs to the current LOD. */
    lod: boolean;

    /**
     * Geometry that is no surface at all - a shadow volume, a collision or heat shader. Never drawn
     * as a mesh whatever its switches say; only the reader can still put it on screen to look at.
     */
    nonVisual?: boolean;

    /** The reader's override, if they have set one. */
    override?: RowOverride;

    /** The masters this row subscribes to, widest first. */
    masters: readonly MasterToggle[];

    /**
     * The name of the nearest row above this one that the READER switched off, when there is one.
     *
     * Only the reader's: a bone row in the tree is a subtree switch, and unticking an arm takes the
     * hand off screen. The model never hides a part through a bone above it.
     */
    ancestorHidden?: string;
}

/**
 * Which link of the chain settled a row.
 *
 * The typed half of `because`. `master` and `ancestor` speak for something ABOVE the row, which the
 * tree's eye cannot change; `you` is the reader; the rest name the switch: `archetype` for geometry
 * that is no surface, `file` and `rules` for the state switch by its last writer, `animation`,
 * `idle` and `pose` for the animation switch by its source, `alt` and `lod` for the two levels.
 */
export type VisibilityLink =
    | "master"
    | "ancestor"
    | "you"
    | "archetype"
    | "file"
    | "rules"
    | "animation"
    | "idle"
    | "pose"
    | "alt"
    | "lod";

/** Whether the row is drawn, and the rule that settled it. */
export interface Resolution {
    visible: boolean;

    /** Which link decided. */
    decidedBy: VisibilityLink;

    /**
     * The same answer for a person: `master:<id>`, `ancestor:<name>`, or the link itself.
     *
     * Written for a tooltip, never parsed - `decidedBy` is what code asks.
     */
    because: string;

    /**
     * What the row would do if the reader had said nothing about it, here or above it.
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
    // it. A row the reader switched off above this one says nothing about what the model draws.
    const authored = decide({ ...facts, ancestorHidden: undefined }, undefined);

    return { ...decided, authored: authored.visible };
}

/** The chain itself, for one reading of what the reader has said. */
function decide(
    facts: RowFacts,
    override: RowOverride | undefined,
): Omit<Resolution, "authored"> {
    // Vetoes first, widest first. A master switch beats an ancestor because it is the broader
    // statement: "no effects at all" outranks "not this limb".
    for (const master of facts.masters) {
        if (!master.on) {
            return {
                visible: false,
                decidedBy: "master",
                because: `master:${master.id}`,
            };
        }
    }

    if (facts.ancestorHidden !== undefined) {
        return {
            visible: false,
            decidedBy: "ancestor",
            because: `ancestor:${facts.ancestorHidden}`,
        };
    }

    if (override !== undefined) {
        return {
            visible: override === "shown",
            decidedBy: "you",
            because: "you",
        };
    }

    const hiding = hidingSwitch(facts);
    if (hiding !== undefined) {
        return { visible: false, decidedBy: hiding, because: hiding };
    }

    // Every switch says shown. The one worth naming is whatever spoke for the part besides the
    // file: a rule that wrote its state, else the clip that keyed its bone.
    const shownBy: VisibilityLink =
        facts.state.writer === "rules"
            ? "rules"
            : facts.animation === undefined
              ? "file"
              : animationLink(facts.animation);

    return { visible: true, decidedBy: shownBy, because: shownBy };
}

/**
 * The first switch that hides the part, or undefined when all four show it.
 *
 * The four are an AND, so any one of them is enough. The order only decides which one the tooltip
 * names when several agree.
 */
function hidingSwitch(facts: RowFacts): VisibilityLink | undefined {
    if (facts.nonVisual === true) {
        return "archetype";
    }

    if (!facts.state.shown) {
        return facts.state.writer;
    }

    if (facts.animation !== undefined && !facts.animation.shown) {
        return animationLink(facts.animation);
    }

    if (!facts.alt) {
        return "alt";
    }

    return facts.lod ? undefined : "lod";
}

function animationLink(animation: AnimationSwitch): VisibilityLink {
    return animation.source === "clip" ? "animation" : animation.source;
}

/**
 * Whether the READER has switched the rows under this one off: by their word on this row, or on a
 * row above it.
 *
 * The tree's own meaning and nothing of the game's: unticking an arm takes the hand off screen, but
 * no switch of the model is inherited. This is what a bone's scene node follows, so the model's own
 * answer never prunes a subtree.
 */
export function subtreeHiddenByReader(facts: RowFacts): boolean {
    return facts.ancestorHidden !== undefined || facts.override === "hidden";
}

/**
 * Whether a shadow volume casts: its own switches as the model leaves them.
 *
 * A volume is never drawn as a surface, so the archetype that keeps it off screen says nothing
 * about whether it casts; the reader's tick on its row puts it on screen to look at, and says nothing
 * either. What casts is the part the game would draw: a volume whose bone a clip hides, or whose
 * level or state is off, casts nothing.
 */
export function castsShadow(facts: RowFacts): boolean {
    return resolveRow({
        ...facts,
        nonVisual: false,
        override: undefined,
        ancestorHidden: undefined,
    }).visible;
}

/**
 * The clip a model rests in, out of the ones it ships.
 *
 * By name, because that is the only thing that marks one: Alamo has no flag for "this is the pose a
 * unit stands in". Sorted before picking, so a model with several idles opens the same way twice.
 */
export function restingClip(clips: readonly string[]): string | undefined {
    // A PLAIN idle first, and only then a qualified one.
    //
    // Matching anything that merely contains the word took `attackidle_00` for Yoda's resting pose,
    // because it sorts before `idle_00` - and an attack idle is the opposite of a rest. His
    // `idle_00` hides the `saber` bone and shows `stick`; `attackidle_00` does exactly the reverse.
    // So he opened with the blade out at his hip and no cane, on every frame.
    //
    // Not simply a word-boundary test, which is what `deathClip` needs. Measured over the 2408
    // shipped clips: 694 name an idle, and every qualified one is a PREFIX - attackidle 108,
    // crouchidle 23, flyidle 4, flylandidle 4, with nothing ever following the word. A flier whose
    // only rest is `flyidle` still has to rest on something, so the qualified ones stay eligible -
    // they just lose to a plain idle whenever the model ships one.
    const sorted = [...clips].sort();

    return (
        sorted.find((clip) => /(^|[^a-z])idle/i.test(clip)) ??
        sorted.find((clip) => clip.toLowerCase().includes("idle"))
    );
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

    return (
        sorted.find((clip) => /(^|[^a-z])die([^a-z]|$)/i.test(clip)) ??
        sorted[0]
    );
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
    rowId: string,
    descendantIds: readonly string[],
    effective: boolean,
    authored: boolean,
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
    rowId: string,
    descendantIds: readonly string[],
    visible: boolean,
    authored: boolean,
): OverrideChange[] {
    // Stored only where it DISAGREES with the model - `authored` is what this row would do if the
    // reader had never touched it. Asking for what the model already does is not a statement, it is
    // the absence of one, and writing it down anyway is what made a tick permanent: an override
    // outranks every switch for good, so one tick on an ability's effect row broke that ability's
    // switch until the whole model was reset.
    //
    // This is the way BACK, and it is what gives the tree's eye its third state - see `rowEye`. It
    // takes nothing away: a shadow volume is gated off, so asking for it genuinely disagrees and is
    // stored, which is the row worth ticking in the first place.
    const override: RowOverride | null =
        visible === authored ? null : visible ? "shown" : "hidden";

    return [
        { row: rowId, override },
        // The subtree is unchanged: hiding an arm hides the hand explicitly, and showing it clears
        // them so each child goes back to whatever the chain says. Only the CLICKED row is the
        // reader making a statement about one thing.
        ...descendantIds.map((row) => ({
            row,
            override: visible ? null : ("hidden" as const),
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
 * ticks. The chain names the switch that decided; this puts it in words.
 */
export function becauseText(because: string): string {
    if (because.startsWith("master:")) {
        return l10n.t(
            "Hidden by the {0} switch",
            because.slice("master:".length),
        );
    }

    if (because.startsWith("ancestor:")) {
        // A bone the tree has no name for still hides what is under it: said without the name.
        const ancestor = because.slice("ancestor:".length);
        return ancestor === ""
            ? l10n.t("Hidden with a bone above it")
            : l10n.t("Hidden with {0}, above it", ancestor);
    }

    // One clause each. These are read standing over a row in a tree that can be a hundred deep,
    // so anything that needs a second sentence is not going to be read at all - "Set by hand.
    // Reset puts it back" spent half its length advertising a button that is already on screen.
    // `idle` is NOT the rest pose. The rest pose is the model with nothing driving the skeleton;
    // this is the unit's own idle clip running and its visibility track deciding the row, which is
    // why it reads as a sibling of `animation` rather than as a pose.
    switch (because) {
        case "you":
            return l10n.t("Set by hand");
        case "idle":
            return l10n.t("Set by the idle animation");
        case "animation":
            return l10n.t("Set by the animation playing");
        case "pose":
            return l10n.t("Bone hidden in the model file");
        case "alt":
            return l10n.t("Not in this damage level");
        case "lod":
            return l10n.t("Not in this detail level");
        case "rules":
            return l10n.t("Set by the game rules");
        case "archetype":
            return l10n.t("Not drawn as a surface");
        default:
            return l10n.t("As the model draws it");
    }
}
