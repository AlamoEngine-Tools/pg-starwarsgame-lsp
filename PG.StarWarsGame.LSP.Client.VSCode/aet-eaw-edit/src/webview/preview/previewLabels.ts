// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model preview's fixed words and pictures: what a control says about itself, what a row kind is
// called and drawn as. Out of modelPreview.tsx so the panels that show them read the same table.

import * as l10n from "@vscode/l10n";

import type { PreviewRgba } from "../../protocol/modelPreview";
import type { IconName } from "../shared/Icon";
import { TREE_KIND_ICONS, TREE_KIND_LABELS } from "../shared/TreeRow";
import type { GroupState } from "./particleScene";
import type { TreeKind } from "./previewTree";
import type { EyeState } from "./rowEye";
import type { ShadowTintReach, StandInBlock } from "./shadowVolumePass";
import type { PresetView } from "./viewportTypes";

/** What each kind is called in the filter row - the shared tree row's names for them. */
export const KIND_LABELS = TREE_KIND_LABELS;

/**
 * What a group's switch will DO, which differs by the state it is in.
 *
 * Only the action. What the group is currently doing is on screen already - the switch is drawn in
 * that state - so saying it again bought nothing and pushed the half that matters to the end.
 */
export const GROUP_STATE_TITLES: Record<GroupState, string> = {
    all: l10n.t("Switch particle systems off"),
    none: l10n.t("Switch particle systems on"),
    some: l10n.t("Switch the rest on"),
};

/** What a row IS, as a picture - the shared tree row's, so the filter chips match the rows. */
export const KIND_ICONS = TREE_KIND_ICONS;

/** The eye a row's state draws. Blender's three - see `preview/rowEye.ts`. */
export const EYE_ICONS: Record<EyeState, IconName> = {
    shown: "visible",
    inherited: "inherited",
    hidden: "hidden",
};

/**
 * The kinds, once, in the order the filter chips show them.
 *
 * Deliberately NOT a cast literal at each use. Both places that need this list used to write their
 * own `as TreeKind[]`, which is an assertion rather than a check - so renaming a kind left one of
 * them naming a kind that no longer existed, typechecked clean, and threw on the first render.
 */
export const TREE_KINDS: readonly TreeKind[] = ["bone", "mesh", "particle"];

/**
 * What the shadow colour control says about itself, per place the tint can land.
 *
 * A disabled control has to say WHY - and the reason it used to give was wrong as often as it was
 * right: "with no floor there is nothing to catch a shadow" is only true when the stencil pass is
 * not casting either, and the same colour reaches the hull through that pass with no ground at all.
 */
export const SHADOW_TINT_TITLE: Record<ShadowTintReach, string> = {
    ground: l10n.t("What the ground tints a cast shadow"),
    model: l10n.t(
        "What this hull tints its own shadowing - switch the ground on for the cast shadow too",
    ),
    both: l10n.t("What the ground and this hull tint a shadow"),
    none: l10n.t(
        "Nothing can show it: Switch the ground on, or the Game renderer on a model that authors a shadow volume",
    ),
    uncast: l10n.t(
        "Nothing casts a shadow: The game draws none without a shadow mesh",
    ),
};

/** Why the stand-in shadow cannot be switched on, by the reason `standInBlock` gives. */
export const STAND_IN_BLOCKED: Record<Exclude<StandInBlock, null>, string> = {
    default: l10n.t("The Default renderer always casts a shadow"),
    shadowMesh: l10n.t("The shadow mesh casts the game's own shadow"),
    particle: l10n.t("A particle file has no render geometry to cast from"),
};

/** Said once when a model loads with no mesh reaching the screen. */
export const NOTHING_DRAWN = l10n.t(
    "This model has no visible mesh at the current damage and detail level. Every sub-mesh is either tagged for another level, hidden in the file, or on a hidden bone.",
);

/** What a faction with no colour of its own is drawn as, and the custom well's starting point. */
export const NEUTRAL_COLOUR: PreviewRgba = { r: 184, g: 184, b: 184, a: 255 };

/** The camera's own views, in the order the stage offers them. */
export const PRESETS: { view: PresetView; label: string; title: string }[] = [
    {
        view: "threeQuarter",
        label: "3/4",
        title: l10n.t("Look from three-quarters on"),
    },
    {
        view: "front",
        label: l10n.t("Front"),
        title: l10n.t("Look from the front"),
    },
    {
        view: "side",
        label: l10n.t("Side"),
        title: l10n.t("Look from the side"),
    },
    { view: "top", label: l10n.t("Top"), title: l10n.t("Look from above") },
];
