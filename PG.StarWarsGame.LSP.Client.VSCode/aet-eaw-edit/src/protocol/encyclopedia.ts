// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The encyclopedia popup preview. Mirrors
// PG.StarWarsGame.LSP.Server/Encyclopedia/GetEncyclopediaEntryParams.cs,
// GetEncyclopediaEntryResult.cs and EncyclopediaLayout.cs.

// ── aet/getEncyclopediaEntry ─────────────────────────────────────────────────

export interface GetEncyclopediaEntryParams {
    objectId: string;
    /**
     * Preview the multiplayer popup. Only then can `MP_Encyclopedia_Text` take over the body, and
     * even then only when it is non-empty - so asking for it is not a promise of getting it. Check
     * `usedMultiplayerBody` on the result.
     */
    multiplayer?: boolean;
}

/**
 * One body line.
 *
 * `text` is null when the key is missing from the loaded databases. Those lines are kept rather
 * than dropped: render the key as a problem, do not silently shorten the card.
 */
export interface EncyclopediaLine {
    key: string;
    text?: string | null;
}

/**
 * One active ability from `Unit_Abilities_Data`, in declaration order.
 *
 * The list arrives complete. The popup only has room for two slots, but the rest are meaningful -
 * modders park abilities past the second slot deliberately, to keep an auto-activated one off the
 * UI or to drive tactical GUI grouping - so trimming is the card's decision, not the server's.
 */
export interface EncyclopediaAbility {
    /** e.g. `DEFEND`, `POWER_TO_WEAPONS`. Drawn in the slot until real icons are reachable. */
    type: string;
    /** `GUI_Activated_Ability_Name`; absent for an ability with no command-bar activation. */
    abilityName?: string | null;
    /**
     * `Alternate_Icon_Name` - the alternate state's icon, and the only icon an ability carries in
     * data. The default one is hardcoded in the engine, which is why slots show text for now.
     */
    alternateIconName?: string | null;
}

/** A `Good_Against` / `Vulnerable_To` target, named via its own `Text_ID`. */
export interface EncyclopediaReference {
    objectId: string;
    displayName?: string | null;
}

/** Four 0-255 channels, alpha last, exactly as the game writes them. */
export interface EncyclopediaRgba {
    r: number;
    g: number;
    b: number;
    a: number;
}

/**
 * Horizontal alignment. Mirrors the C# `EncyclopediaTextAlignment` constant class - strings, not
 * an enum ordinal.
 */
export const ENCYCLOPEDIA_TEXT_ALIGNMENT = {
    /** Neither justify tag set, which is how `encyclopedia_center_text` is encoded. */
    center: 'center',
    left: 'left',
    right: 'right',
} as const;

/** How one text row is drawn. `component` is the CommandBarComponent it came from. */
export interface EncyclopediaTextStyle {
    component: string;
    fontName: string;
    fontPointSize: number;
    scale: number;
    textColor: EncyclopediaRgba;
    /** See {@link ENCYCLOPEDIA_TEXT_ALIGNMENT}. */
    alignment: string;
}

/**
 * The popup's geometry and per-row styling.
 *
 * `width` is the load-bearing value: the engine wraps proportional text to it, so it - not any
 * monospace grid - produces the line breaks a modder tuned their "=====" bars against. Width and
 * point size scale together, so rendering at any multiple keeps identical wrap points.
 */
export interface EncyclopediaLayout {
    width: number;
    rowHeight: number;
    offsetX: number;
    offsetY: number;
    /**
     * The factor the header draws the unit icon at, from `encyclopedia_icon`'s `Size` X. The icon
     * asset is 50 units square, so the drawn size is `50 * iconScale` - stock 0.75 gives 37.5.
     */
    iconScale: number;
    backdropColor: EncyclopediaRgba;
    header: EncyclopediaTextStyle;
    body: EncyclopediaTextStyle;
    rightText: EncyclopediaTextStyle;
    centerText: EncyclopediaTextStyle;
    costText: EncyclopediaTextStyle;
}

export interface GetEncyclopediaEntryResult {
    found: boolean;
    objectId: string;
    typeName?: string | null;
    displayName?: string | null;
    unitClass?: string | null;
    body: EncyclopediaLine[];
    /** Whether `MP_Encyclopedia_Text` actually supplied `body`. */
    usedMultiplayerBody: boolean;
    /**
     * `Population_Value`, drawn as the blip at the card's top-left. Absent when the object has
     * none - and then the header shifts left into the space the blip would have occupied, so this
     * is a layout input as well as a value.
     */
    populationValue?: number | null;
    /** Active abilities in declaration order, complete; the card slots the first two. */
    abilities: EncyclopediaAbility[];
    goodAgainst: EncyclopediaReference[];
    vulnerableTo: EncyclopediaReference[];
    /** Ships with the text so the card can never draw one against a stale copy of the other. */
    layout: EncyclopediaLayout;
}
