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
 * A piece of artwork with the size it was authored at.
 *
 * The dimensions are the point: the card lays out AROUND its artwork - the header band's height,
 * the Against panel's aspect, the separator's thickness, the ability slot's size - and all of it is
 * reskinnable. Without them the card has to assume the base game's sizes, which silently squashes a
 * mod's own art back into them. Read from the PNG header, so they describe what was actually cut.
 *
 * `width`/`height` are 0 when the header could not be read; treat that as "unknown" and fall back to
 * a nominal size rather than dividing by it.
 */
export interface EncyclopediaImage {
    dataUri: string;
    width: number;
    height: number;
}

/**
 * One active ability from `Unit_Abilities_Data`, in declaration order.
 *
 * The list arrives complete. The popup only has room for two slots, but the rest are meaningful -
 * modders park abilities past the second slot deliberately, to keep an auto-activated one off the
 * UI or to drive tactical GUI grouping - so trimming is the card's decision, not the server's.
 */
export interface EncyclopediaAbility {
    /** e.g. `DEFEND`, `POWER_TO_WEAPONS`. Drawn in the slot when no icon resolves. */
    type: string;
    /** `GUI_Activated_Ability_Name`; absent for an ability with no command-bar activation. */
    abilityName?: string | null;
    /**
     * `Alternate_Icon_Name` - the alternate state's icon, and the only icon an ability carries in
     * data. The default one is hardcoded in the engine, hence the guess below.
     */
    alternateIconName?: string | null;
    /**
     * The ability's icon, absent when none was found.
     *
     * The server finds it by guessing `I_SA_<TYPE>` against the mega texture - nothing in the game's
     * data links an ability to its icon. The guess is validated by the lookup, so a miss means no
     * icon rather than the wrong one, and the slot falls back to the type text.
     *
     * Its natural size sizes the slot: the ability scale applies to the icon's own dimensions, and a
     * mod may ship these at other than the base game's 26.
     */
    icon?: EncyclopediaImage | null;
}

/** A `Good_Against` / `Vulnerable_To` target, named via its own `Text_ID`. */
export interface EncyclopediaReference {
    objectId: string;
    displayName?: string | null;
    /**
     * The referenced unit's own portrait, as a `data:image/png;base64,...` URI - the server resolves
     * the unit the list names, then that unit's `Icon_Name` through the ordinary icon resolver.
     * Absent when the target is unknown or icons are unavailable, in which case the slot falls back
     * to showing the unit's name.
     */
    iconDataUri?: string | null;
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
     * The factor the header draws the unit icon at, from `encyclopedia_icon`'s `Size` X. Stock is
     * 0.75 and the header icon measures 50 units, so 0.75 is the popup's REFERENCE scale rather
     * than a plain multiplier - read other scales relative to it. See {@link abilityIconScale}.
     */
    iconScale: number;
    /**
     * The factor the class row draws each ability icon at, from the same tag's Y - the shipped file
     * comments it as "Y is the scale of the ability icon". Stock 0.66 against a 26-unit ability
     * asset puts the slot at `26 * 0.66 / 0.75`, about 23 units.
     */
    abilityIconScale: number;
    backdropColor: EncyclopediaRgba;
    /**
     * `encyclopedia_back`'s `Blank_Texture_Name` - the atlas entry the backdrop is cut from. Data,
     * not a constant: unlike `E_TOPBAR`, `E_LINE`, `E_AGAINST_FRAME` and `E_UNIT_AGAINST`, which
     * appear in no shipped XML and really are engine-fixed, this one is named in the file.
     */
    backdropTextureName: string;
    /**
     * `encyclopedia_back`'s `Icon_Alternate_Texture_Name`, in list order - one faction frame per
     * faction slot. The base game ships exactly two.
     */
    factionFrameTextureNames: string[];
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
    /** The object's portrait, absent when nothing supplied one. */
    icon?: EncyclopediaIcon | null;
    /** The card's own chrome from the mega texture, absent when none of it resolved. */
    chrome?: EncyclopediaChrome | null;
    /**
     * The pool of individual ship names this object draws from, absent when it is not registered
     * for them - which is nearly every object. The SERVER does not pick one, so {@link unitClass}
     * always holds the object's real class line - the panel picks and keeps its choice.
     */
    shipNames?: EncyclopediaShipNames | null;
}

/**
 * An object's pool of individual ship names, wired up in GameConstants' `ShipNameTextFiles`.
 *
 * Objects listed there show a NAME where others show their class. The engine picks one and
 * remembers which are spent so a fleet never repeats itself; a preview has no campaign to remember
 * for, so it simply picks - which is why the whole pool travels and the panel can show what else
 * the object could have been called.
 */
export interface EncyclopediaShipNames {
    /** The path as written in the tag. */
    sourcePath: string;
    /**
     * Whether the file was readable. Separates "wired up but the file is missing" - an authoring
     * mistake worth showing - from "wired up and empty", which a bare count cannot express.
     */
    fileFound: boolean;
    /** Every name in the pool, in file order. */
    names: string[];
}

/**
 * The popup's chrome, cut from the mega texture - the pieces the engine builds the card from rather
 * than any unit's artwork.
 *
 * Sizes quoted below are the BASE GAME's. Do not hardcode them: each piece carries its own, and a
 * mod reskinning the atlas may ship any of them at a different size. Absent fields are normal - the
 * card keeps its calibrated CSS rendition as the fallback, so chrome can only sharpen it.
 */
export interface EncyclopediaChrome {
    /** `E_BACKGROUND`, 48x48 in the base game, stretched across the backdrop. */
    background?: EncyclopediaImage | null;
    /**
     * `E_TOPBAR`, the full-width header band - 262x20 in the base game, 262 being the card's own
     * width - WITH the population blip's black disc baked into its left end at x 1..19, y 1..18.
     * The disc is artwork: draw the band and it is already there.
     */
    topBar?: EncyclopediaImage | null;
    /**
     * `E_TOPBAR2`, the same band with NO disc, for objects without a `Population_Value`. The two
     * variants ship at identical sizes, which is why only their pixels tell them apart.
     */
    topBarNoBlip?: EncyclopediaImage | null;
    /**
     * `E_LINE`, the separator under the class row - 232x27 in the base game, and almost entirely
     * transparent padding around a one-pixel rule. Draw it at its natural height; squashing it to a
     * thin strip interpolates the rule away entirely.
     */
    line?: EncyclopediaImage | null;
    /** `E_AGAINST_FRAME`, one Strong/Weak Against panel; 129x43 in the base game. */
    againstFrame?: EncyclopediaImage | null;
    /** `E_UNIT_AGAINST`, a single slot inside that panel; 43x43 in the base game. */
    unitAgainst?: EncyclopediaImage | null;
    /**
     * The per-faction frames, one entry per slot `encyclopedia_back` declares. Always the full list,
     * art or no art, so the preview can offer every slot the mod defines.
     */
    factionFrames: EncyclopediaFactionFrame[];
}

/**
 * One faction's frame for the card.
 *
 * The engine picks the slot from the viewing player's faction. A preview has no player, so the
 * server ships every slot and the panel switches between them locally - which also keeps the switch
 * instant instead of costing a round trip per click.
 *
 * The base game's two frames are the SAME colour, `rgb(103,163,255)`: the rebel entry is a flat 4x4
 * wash at alpha 130, the empire entry a 64x64 rect opaque in the middle with a feathered edge. So
 * this is not a red/blue faction tint, and neither frame may be stretched across the card - doing
 * that washes the dark navy backdrop out to light blue.
 */
export interface EncyclopediaFactionFrame {
    /** Index into the list, which is the faction slot the engine selects by. */
    slot: number;
    /** The name as written in the tag, so a slot stays identifiable when its art is missing. */
    textureName: string;
    /**
     * A display name, absent when only a guess was available. Only the first two slots have names
     * the game's own data confirms - the shipped textures say rebel and empire - and a mod's third
     * slot belongs to whichever faction that mod added, so it travels unnamed.
     */
    slotName?: string | null;
    /** The artwork, absent when the atlas carries no such entry. */
    image?: EncyclopediaImage | null;
}

/**
 * Where a resolved icon's pixels came from. The first three mirror the server's `IconSource`;
 * `Fallback` is the opposite - no layer supplied the icon and the server sent its built-in
 * placeholder so the card still has a portrait slot.
 */
export type EncyclopediaIconSource =
    | 'WorkspaceMegaTexture'
    | 'LooseSource'
    | 'Baseline'
    | 'Fallback';

/**
 * A resolved icon, pre-encoded by the server so the webview needs no decoding step - and no blob
 * URLs, which the panel's content security policy would block anyway.
 */
export interface EncyclopediaIcon {
    /** The name as written in `Icon_Name`. */
    name: string;
    /** A complete `data:image/png;base64,...` URI. */
    dataUri: string;
    source: EncyclopediaIconSource;
    /**
     * The icon resolved only from a raw source image while the workspace's mega texture lacks it:
     * drawn, but never repacked, so the game would not show it yet.
     */
    isMegaTextureStale: boolean;
    /**
     * Natural pixel size, or 0 when unknown. Worth having even though the header draws the portrait
     * into a fixed slot: several shipped portraits are NOT square (50x49, 47x47, 44x45), so knowing
     * the real dimensions is what lets the card draw them without distorting them.
     */
    width: number;
    height: number;
}
