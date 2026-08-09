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
 * One GUI-activated ability, drawn as a slot in the class row. At most two are sent - the popup
 * has room for two, and a unit with more shows the first two in document order.
 */
export interface EncyclopediaAbility {
    /** The `Unit_Ability`'s `Type`, e.g. `LUCKY_SHOT`. */
    type: string;
    /** `GUI_Activated_Ability_Name`; its presence is what makes the ability GUI-activated. */
    abilityName?: string | null;
    /**
     * `Alternate_Icon_Name` - the alternate-state icon, and the only icon the schema defines for
     * an ability. Nothing in the shipped data gives a primary one, so the ordinary icon appears to
     * come from the engine. Null for almost every ability.
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
    /** GUI-activated abilities, at most two, in document order. */
    abilities: EncyclopediaAbility[];
    goodAgainst: EncyclopediaReference[];
    vulnerableTo: EncyclopediaReference[];
    /** Ships with the text so the card can never draw one against a stale copy of the other. */
    layout: EncyclopediaLayout;
}
