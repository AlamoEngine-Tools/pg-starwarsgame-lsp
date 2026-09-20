// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which colour the story graph gives a lifecycle, a branch and a lane.
//
// This used to be four lists. The lifecycle border rules named the four chart colours, the legend
// swatches beside them named the same four again inline, a hex mirror named them a third time for
// the LOD canvas, and the branch palette existed twice over - once as variables for CSS and once as
// hex for the canvas, under a comment asking the next reader to keep both in the same order by
// hand. Nothing checked that any of them agreed, and the canvas copies could not follow the theme
// at all.
//
// One list each now, naming colour TOKENS rather than values. A panel wraps a token in var(); the
// canvas resolves it to a concrete colour through resolveColour, because a 2D context cannot take a
// var(). Both start from the same name.

/** The lifecycles an event node can be in, in the order the colour key lists them. */
export type Lifecycle = 'Waiting' | 'Armed' | 'Fired' | 'Disabled';

/**
 * The colour a node's border and its colour key swatch take - and its overview rect, unless the node
 * belongs to a branch, which then wins there (see BRANCH_PALETTE).
 */
export const LIFECYCLE_TOKENS: Readonly<Record<Lifecycle, string>> = {
    Waiting: '--colour-data-blue',
    Armed: '--colour-data-green',
    Fired: '--colour-data-purple',
    Disabled: '--colour-data-red',
};

/**
 * A junction has no lifecycle of its own, so it takes a NEUTRAL colour rather than a hue that
 * means a state.
 *
 * It used to take purple - the colour a fired event takes - which said the one thing about a
 * junction that cannot be true: it claimed a lifecycle. Zoomed out, where the shape was a plain
 * rectangle like everything else, that was the only signal there was, and it was wrong. Structure
 * is neutral here for the same reason a plain Prereq edge is (see EDGE_KINDS): the default
 * relation is not a state, and colouring it says otherwise.
 */
export const JUNCTION_TOKEN = '--colour-data-neutral';

/** An event whose lifecycle the server did not name. Quiet, because it says nothing. */
export const UNKNOWN_LIFECYCLE_TOKEN = '--colour-faint';

/**
 * What an edge's colour and dash pattern mean.
 *
 * Node colour says lifecycle; edge colour says RELATION, and the two axes were never distinguished
 * anywhere the reader could see: the old legend was generated from LIFECYCLE_TOKENS alone. Orange is
 * a control edge, yellow a tactical attachment. (The orange and yellow in issue #128's screenshot
 * turned out to be BRANCH colours on zoomed-out nodes - see BRANCH_PALETTE - not these.)
 *
 * One entry per kind, read by the stroke rules AND by the colour key, so a swatch cannot come to
 * disagree with the edge it describes. `dash` is an SVG stroke-dasharray; empty means solid.
 * `Prereq` is the plain case and takes the chart foreground rather than a hue, because "A must
 * happen first" is the default relation and colouring it would say something it does not mean.
 * `LuaLink` is reserved and never produced yet, so it is deliberately absent and falls through to
 * the default stroke.
 */
export interface EdgeKindStyle {
    /** Matches StoryEdgeKind on the server; `TacticalEntry` shares the tactical presentation. */
    readonly kind: string;
    readonly token: string;
    readonly dash: string;
    readonly label: string;
}

export const EDGE_KINDS: readonly EdgeKindStyle[] = [
    {kind: 'Prereq', token: '--colour-data-neutral', dash: '', label: 'Requires'},
    {kind: 'Control', token: '--colour-data-orange', dash: '', label: 'Activates or suspends'},
    {kind: 'Tactical', token: '--colour-data-yellow', dash: '8 4', label: 'Tactical battle'},
    {kind: 'Flag', token: '--colour-data-blue', dash: '2 4', label: 'Story flag'},
    // The engine's own link from a reward to the listeners it reaches - a speech to its speech-done,
    // a battle to its outcome and summary listeners. Dash-dot so it reads as neither the dashed
    // tactical edge nor the dotted flag edge, and in the neutral grey so no branch hue claims it.
    {kind: 'Implicit', token: '--colour-data-neutral', dash: '8 3 2 3', label: 'Engine link'},
];

/**
 * The hues a branch is given, slot by slot (see branchColours).
 *
 * Order is load-bearing. The campaign's first branch takes the first slot, so reordering this list
 * repaints every graph anyone has looked at.
 *
 * These are the lifecycle hues again, and zoomed out a branched node takes its branch hue INSTEAD of
 * its lifecycle, so a green rect there may be a branch rather than Armed. Separating them by glow or
 * outline cost the overview its frame rate, so the colour key (colourKey.ts) names each branch's hue
 * and says which one wins instead.
 */
export const BRANCH_PALETTE: readonly string[] = [
    '--colour-data-blue',
    '--colour-data-green',
    '--colour-data-orange',
    '--colour-data-purple',
    '--colour-data-red',
    '--colour-data-yellow',
];

/**
 * The hues a swimlane is hashed onto.
 *
 * Seven slots against the six chart hues the theme publishes. The count is kept at seven rather than
 * collapsed to six so that the modulus does not move: every lane keeps the colour it has today
 * except the sixth and seventh, which were a teal and a pink with no theme behind them. The seventh
 * takes the chart foreground, which reads as the lane that was not given a hue.
 */
export const LANE_PALETTE: readonly string[] = [
    '--colour-data-blue',
    '--colour-data-green',
    '--colour-data-orange',
    '--colour-data-purple',
    '--colour-data-yellow',
    '--colour-data-red',
    '--colour-data-neutral',
];

/** The token a branch is drawn in. */
export type BranchColour = (branch: string) => string;

/**
 * Hands out branch colours in campaign order: the first branch takes the first slot, the seventh
 * cycles back to the first.
 *
 * The list is the whole CAMPAIGN's branches - the server's unfiltered branch facet - never the ones
 * in view, so filtering cannot move a branch to another colour. Only adding or removing a branch in
 * the campaign itself does. It replaced a hash of the name (issue #128), which put 5 branches on 4
 * colours in Empire Act I and 14 on 5 in the Underworld campaign.
 *
 * Ordinal order, the order the server sorts the facet in and the branch dropdown shows. A branch the
 * list does not name yet - typed into an edit, ahead of the server's next facet list - falls back to
 * its name hash, so it still gets a colour and the same one every time.
 */
export function branchColours(campaignBranches: readonly string[]): BranchColour {
    const order = [...new Set(campaignBranches.filter(branch => branch !== ''))].sort();
    const slot = new Map(order.map((branch, index) => [branch, index]));

    return branch => {
        const index = slot.get(branch);
        return index === undefined ? branchToken(branch) : BRANCH_PALETTE[index % BRANCH_PALETTE.length];
    };
}

/**
 * The branch name hash - now only the fallback for a branch outside the campaign list.
 *
 * Signed 32-bit wraparound then Math.abs, which is not the same distribution as the lane hash below
 * and is deliberately left alone.
 */
export function branchToken(branch: string): string {
    let hash = 0;
    for (let i = 0; i < branch.length; i++) {
        hash = (hash * 31 + branch.charCodeAt(i)) | 0;
    }
    return BRANCH_PALETTE[Math.abs(hash) % BRANCH_PALETTE.length];
}

/** The lane hash, unchanged: unsigned, so it does not need the Math.abs the branch hash does. */
export function laneToken(key: string): string {
    let hash = 0;
    for (let i = 0; i < key.length; i++) {
        hash = (hash * 31 + key.charCodeAt(i)) >>> 0;
    }
    return LANE_PALETTE[hash % LANE_PALETTE.length];
}
