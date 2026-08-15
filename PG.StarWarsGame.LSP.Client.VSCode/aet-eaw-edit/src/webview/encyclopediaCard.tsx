// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The encyclopedia popup, drawn to match the game.
//
// The structure is the game's, not a stack of lines: a full-width grey header band with the square
// portrait drawn OVER its left end, a "Class:" row with ability icons at the far right, a
// separator underlining that row, then the body running the FULL width - under the portrait, not
// beside it - and finally the paired Strong/Weak Against panels.
//
// Two different sources of numbers meet here, and they are not equally trustworthy:
//   - Geometry and colours the server read out of `encyclopedia_*` in Commandbarcomponents.xml
//     (width, row height, offsets, icon scale, fonts, text colours). Data - a mod changing them
//     changes this.
//   - The natural size of each piece of ARTWORK, which arrives with the artwork itself. Also data:
//     a mod reskinning the atlas at a different size moves the band, the panels and the slots with
//     it. Never hardcode an art dimension - read it off the image and keep the base game's value
//     only as the fallback for an atlas that lacks the piece.
//   - Everything neither of those carries: the header block height, the insets and gaps. Those live
//     in CHROME below and were measured by sampling pixels out of a screenshot of the running game.
//     They are calibration, not data, and they are the part to re-check when something looks off -
//     preferably by sampling again rather than by eye, which got several of them wrong.

import {
    EncyclopediaImage, EncyclopediaLayout, EncyclopediaReference, EncyclopediaRgba,
    EncyclopediaTextStyle, GetEncyclopediaEntryResult,
} from '../protocol/encyclopedia';
import { cssFontStack } from './encyclopediaFonts';
import { encyclopediaIconTitle } from './encyclopediaIconTitle';

/**
 * Point sizes are not unit sizes: 7 units of text in a 262-unit popup is roughly half the size the
 * game draws. This is the multiplier between the two.
 *
 * Calibrated against the game's own wrapping. Luke's stock bio is a SINGLE localisation key
 * (`<Encyclopedia_Text>TEXT_TOOLTIP_LUKE_SKYWALKER_JEDI</Encyclopedia_Text>`), so the four lines
 * the game shows are computed - Win32 DrawText with DT_WORDBREAK - and reproducing them is the
 * whole job:
 *
 *     Once a farm boy from Tatooine, Luke
 *     Skywalker trained under Jedi Master Yoda
 *     to become the first of a new generation
 *     of Jedi Knights.
 *
 * The content box is 250 units: 262 wide, less the 1-unit border and the 5-unit inset on each
 * side. Mind that border - dropping it gives 252 and a font size ~0.8% too large, which is enough
 * to lose "Yoda" off the second line. With Tahoma metrics (see cssFontStack) any size in
 * (13.37, 13.57] px reproduces all four breaks plus both breaks of the capture sentence, and
 * 7 * 1.924 = 13.47 sits mid-window. Re-fit against this bio if the wrapping ever drifts.
 */
const POINT_TO_UNIT = 1.924;


/**
 * The unit icon, drawn 50 units square.
 *
 * Measured off an annotated screenshot: the icon spans 53.6 x 47.1 units, averaging ~50 - which is
 * also the atlas size of every portrait.
 *
 * `layout.iconScale` (encyclopedia_icon's `Size` X, stock 0.75) is deliberately NOT multiplied in:
 * 50 * 0.75 = 37.5 is a third short of the measurement. Stock 0.75 is the popup's REFERENCE scale,
 * not a plain multiplier - the engine draws an asset at `assetSize * scale / 0.75`, which leaves
 * the header icon at its full 50 and is the reading that also sizes the ability slots correctly.
 * A mod that changes the scale still moves the icon, since the ratio is what is applied.
 */
const ICON_SIZE = 50;
const REFERENCE_ICON_SCALE = 0.75;

/**
 * The ability slot, from `encyclopedia_icon`'s `Size` Y against a 26-unit asset. Stock 0.66 gives
 * ~23 units - a little larger than the ~19 first measured off a screenshot, and the game's own
 * icons are visibly upscaled in that slot rather than shrunk.
 */
function abilitySlotSize(layout: EncyclopediaLayout, art?: EncyclopediaImage | null): number {
    const scale = layout.abilityIconScale > 0 ? layout.abilityIconScale : 0.66;
    // The icon's OWN size, not the base game's 26: a mod may draw its ability icons at any size and
    // the engine scales what it finds, so assuming 26 shrinks or inflates a reskinned set.
    const asset = art?.width && art.width > 0 ? art.width : CHROME.abilityAssetSize;
    return (asset * scale) / REFERENCE_ICON_SCALE;
}

/** The natural size of `art` scaled the way the engine scales it, or `fallback` when unknown. */
function drawnSize(
    art: EncyclopediaImage | null | undefined, scale: number, fallback: number,
): { width: number; height: number } {
    if (!art || art.width <= 0 || art.height <= 0) {
        return { width: fallback, height: fallback };
    }

    const factor = (scale > 0 ? scale : REFERENCE_ICON_SCALE) / REFERENCE_ICON_SCALE;
    return { width: art.width * factor, height: art.height * factor };
}


const CHROME = {
    /**
     * Band top to the divider. The icon is TALLER than this and deliberately overflows it - its
     * bottom sits about 9 units below the divider, which is why a scan across the divider's row
     * finds the icon rather than the line on the left.
     */
    headerBlockHeight: 38,
    /** Full width, and the icon is drawn ON TOP of its left end - not beside it. */
    // 20, not the 21 screenshots suggested: `E_TOPBAR` in the mega texture is exactly 262x20, and
    // 262 is the card's own width - so the band is a full-width strip and the atlas settles its
    // height. Two variants ship, `E_TOPBAR` and `E_TOPBAR2`, at identical size.
    headerHeight: 20,
    /** The icon's own inset. Larger when a blip is present, since the two overlap. */
    iconLeftWithBlip: 13,
    iconLeftNoBlip: 8,
    iconTop: 1,
    /** Gap between the icon's right edge and the name/class left edge (measured 69.3 - 63). */
    textGap: 6,
    /**
     * The population blip, measured at 18.4 units off a screenshot - and confirmed by the atlas,
     * which bakes the disc into `E_TOPBAR` at x 1..19, y 1..18. These are the atlas numbers, used
     * to place the NUMERAL over the disc the band art already draws. Only the CSS fallback below
     * paints a disc of its own.
     */
    blipLeft: 1,
    blipTop: 1,
    blipSize: 18,
    /**
     * Where the numeral's RIGHT edge sits, from `encyclopedia_icon`'s `Text_Offset 12 -12`. The
     * component is a TextButton - it draws the number itself, right-justified - and 12 against a
     * disc centred on 10 is what makes the digit read slightly left of centre in the game.
     */
    blipTextRight: 12,
    /**
     * The nominal ability icon, 26 units - every `I_SA_*` in the atlas is 26x26. The drawn slot is
     * this times `layout.abilityIconScale` over the reference scale; see {@link abilitySlotSize}.
     */
    abilityAssetSize: 26,
    /**
     * The popup has room for exactly two. Measured as two boxes spanning x 440..522 on a card of
     * 60..530, each ~20 units square with a ~5-unit gap.
     */
    abilitySlots: 2,
    abilityGap: 2,

    /**
     * The Strong/Weak Against panels are STATICALLY sized - both measure 229x95px whatever they
     * contain - and they span the card edge to edge rather than sitting inside the text inset.
     * They are also all-or-nothing: either tag being present brings up BOTH panels, so the pair
     * always shows its six slots together rather than one panel appearing alone.
     *
     * Their geometry was DERIVED from screenshots - each panel holds exactly three square icon
     * slots, so the slot is a third of the panel's interior width and the interior is one slot
     * tall - and the mega texture has since CONFIRMED it: `E_AGAINST_FRAME` is 129x43 against the
     * 127.5x42 we measured, and `E_UNIT_AGAINST` is a 43x43 square, matching the derived slot.
     * The atlas is the better source, so those are the numbers to trust if the two ever disagree.
     */
    againstFrame: { width: 129, height: 43 },
    againstSlotSize: 43,
    againstSlots: 3,
    againstGap: 5,
    againstLabelInset: 3,
    separatorGap: 3,
    /**
     * Space below the divider before the body. Larger than the gap above it because the icon hangs
     * past the divider - the body has to start clear of the icon's bottom edge, which the game does
     * too: its first body line begins at ~50.4 units against an icon bottom of ~51.6.
     */
    bodyTopGap: 7,
    sectionGap: 4,
    panelPadding: 3,
    panelMinHeight: 34,

    // Sampled from the game screenshot rather than guessed. The backdrop is a slight vertical
    // gradient, lighter at the top.
    backgroundTop: 'rgb(21, 32, 73)',
    backgroundBottom: 'rgb(16, 25, 56)',
    border: 'rgb(91, 122, 181)',
    headerBand: 'rgb(83, 83, 83)',
    /**
     * Faint, not bright. Sampling a column clear of the portrait and the ability icons puts this
     * line at rgb(47,55,88) against a rgb(17,27,59) backdrop - barely there. An earlier reading of
     * rgb(161,184,253) came from x=500, which is inside an ability-icon box rather than on the
     * line, and drew a vivid light-blue rule the game does not have.
     */
    separator: 'rgb(47, 55, 88)',
    /**
     * `E_LINE` is 232x27 but almost entirely EMPTY: scanning its rows, only row 22 carries any
     * pixels at all (mean alpha 187, luminance 86) and the other 26 are fully transparent. It is a
     * one-pixel rule with 22 rows of padding above it and 4 below, not the soft glow its height
     * suggests.
     *
     * Two consequences, both learned the hard way. Squashing it to a thin strip erases it, since
     * interpolation drops the single row. And CENTRING it puts that row 8.5 units below the
     * divider, straight across the first line of body text - so the art is offset by its core row
     * instead, which lands the rule exactly where the measured 1px separator sat.
     */
    lineArtHeight: 27,
    lineArtCoreRow: 22,
    /**
     * How far the art's BOTTOM edge sits below the rule - 27 - 22 in the base game.
     *
     * The art is anchored by its bottom rather than its top so that its natural height can be
     * honoured: a reskinned separator of a different height then keeps its lower edge where the
     * engine puts it instead of having its rule slide by the whole difference. Where the rule sits
     * inside a MOD's art is genuinely unknowable without scanning its pixels, so this is the one
     * assumption left in the divider - noted rather than hidden.
     */
    lineArtRowsBelowRule: 27 - 22,
    abilitySlot: 'rgba(110, 126, 158, 0.70)',
    /**
     * The blip is a BLACK disc with a GOLD numeral, not the other way round. A cut straight
     * through it reads rgb(0,0,0) either side of a rgb(255,215,56) stroke. An earlier reading of
     * rgb(206,173,45) as the fill was an antialiased edge pixel of the digit itself - the yellow
     * region measured 7px wide and 14px tall, which is a "1", not a 30px disc.
     *
     * The fill is FALLBACK ONLY. `E_TOPBAR` bakes the disc into the band art, so a card drawing
     * chrome gets it from the atlas and painting this one as well doubled it up.
     */
    blipFill: 'rgb(0, 0, 0)',
    blipLabel: 'rgb(255, 215, 56)',

    // Three tones of one hue, and the "light dark light" is the SLOTS, not a gradient across one
    // fill: slots 1 and 3 take the lighter tone, slot 2 a darker hue of it. The border takes the
    // lightest variant, so it matches the label exactly.
    strongSlot: 'rgb(0, 91, 0)',
    strongSlotDark: 'rgb(0, 63, 0)',
    strongBorder: 'rgb(0, 192, 0)',
    strongLabel: 'rgb(0, 192, 0)',
    weakSlot: 'rgb(91, 0, 0)',
    weakSlotDark: 'rgb(63, 0, 0)',
    weakBorder: 'rgb(192, 0, 0)',
    weakLabel: 'rgb(192, 0, 0)',
} as const;

/**
 * Text colour, alpha deliberately dropped.
 *
 * `encyclopedia_text` says `Text_Color 192 192 192 200`, but sampling the game's own pixels puts
 * the glyph cores at exactly rgb(192,192,192), and the header's at exactly rgb(255,255,255) - the
 * engine does not apply that alpha to text. Honouring it composited the body down to about
 * rgb(155,155,155) against the backdrop, which is why the preview read washed-out next to the
 * real popup.
 */
function textColor(c: EncyclopediaRgba): string {
    return `rgb(${c.r}, ${c.g}, ${c.b})`;
}

/** Fills and tints, where the alpha does matter. */
function rgba(c: EncyclopediaRgba): string {
    return `rgba(${c.r}, ${c.g}, ${c.b}, ${(c.a / 255).toFixed(3)})`;
}

/**
 * Splits a game font name into a CSS family and weight/style.
 *
 * The game writes "Arial Bold" as one name; CSS has no such family, so asking for it verbatim
 * matches nothing and silently falls back to a default face - which changes glyph widths and
 * therefore the wrap points this card exists to reproduce.
 */
export function fontOf(fontName: string): React.CSSProperties {
    let family = fontName.trim();
    let fontWeight: React.CSSProperties['fontWeight'] = 'normal';
    let fontStyle: React.CSSProperties['fontStyle'] = 'normal';

    for (;;) {
        const lower = family.toLowerCase();
        if (lower.endsWith(' bold')) {
            // The family suffix is stripped but the weight is NOT applied: measuring the game's
            // glyphs gives the header and the body the same stem width (median 3px at a 17px glyph
            // height) even though encyclopedia_header_text asks for "Arial Bold" and
            // encyclopedia_text for plain "Arial". The engine evidently does not resolve the
            // suffix to a bold face, so rendering one made the header heavier than the game's.
            family = family.slice(0, -5).trim();
        } else if (lower.endsWith(' italic')) {
            fontStyle = 'italic';
            family = family.slice(0, -7).trim();
        } else {
            break;
        }
    }

    return { fontFamily: cssFontStack(family), fontWeight, fontStyle };
}

function textStyle(
    style: EncyclopediaTextStyle, layout: EncyclopediaLayout, u: (n: number) => string,
): React.CSSProperties {
    return {
        ...fontOf(style.fontName),
        fontSize: u(style.fontPointSize * style.scale * POINT_TO_UNIT),
        // The Y of encyclopedia_back's `Size` is the row height - one text line - so it is the
        // line box, not decoration. Leaving it to the browser's default gave lines ~36% further
        // apart than the game's and was the single biggest visual difference in the card.
        lineHeight: u(layout.rowHeight),
        color: textColor(style.textColor),
        // Authored leading spaces and blank lines are content; pre-wrap keeps them and still wraps.
        whiteSpace: 'pre-wrap',
        overflowWrap: 'break-word',
    };
}

/**
 * A Strong/Weak Against panel: a coloured label over a fixed-size box of unit icons.
 *
 * The box is statically sized - the game draws both at the same 229x95px whatever they hold - so
 * it does not grow with its contents, and a long list simply overflows the way the game's would.
 * The caller decides whether to render one at all: a unit with no Good_Against / Vulnerable_To
 * shows no panel.
 */
function AgainstPanel(
    {
        label, refs, layout, u, width, labelColor, slotFill, slotFillDark, border,
        frameImage, slotImage,
    }: {
        label: string;
        refs: readonly EncyclopediaReference[];
        layout: EncyclopediaLayout;
        u: (n: number) => string;
        width: number;
        labelColor: string;
        slotFill: string;
        slotFillDark: string;
        border: string;
        /** `E_AGAINST_FRAME` (129x43) when the atlas has it; the CSS border stands in otherwise. */
        frameImage?: EncyclopediaImage | null;
        /** `E_UNIT_AGAINST` (43x43) when the atlas has it; the CSS slot tones stand in otherwise. */
        slotImage?: EncyclopediaImage | null;
    },
): React.JSX.Element {
    // Three slots fill the interior exactly, so the slot width is the interior width over three.
    const slot = (width - 2) / CHROME.againstSlots;
    // The panel's height follows the FRAME ART's aspect rather than assuming the slot is square.
    // In the base game E_AGAINST_FRAME is 129x43 and the slot 43x43, so this reduces to the square
    // reading it replaces - but a reskinned frame of any other proportion now keeps its shape
    // instead of being stretched into the base game's.
    const panelHeight = frameImage?.width && frameImage.width > 0 && frameImage.height > 0
        ? width * (frameImage.height / frameImage.width)
        : slot + 2;
    // Fixed width, not flex: a panel shown on its own keeps its half-card size rather than
    // stretching to fill the row.
    return (
        <div style={{ width: u(width), flex: '0 0 auto' }}>
            <div
                style={{
                    ...fontOf(layout.header.fontName),
                    fontSize: u(layout.header.fontPointSize * POINT_TO_UNIT),
                    lineHeight: u(layout.rowHeight),
                    color: labelColor,
                    // The box sits ~0.6 units off the card edge but the label ~2.8, so the text
                    // clears the border instead of being cut by it.
                    paddingLeft: u(CHROME.againstLabelInset),
                }}
            >
                {label}
            </div>
            <div
                style={{
                    // The frame art is faction-NEUTRAL grey - the engine tints it per panel - so it
                    // is multiplied against the panel colour. Multiply is what a tint is: grey x
                    // colour keeps the art's own light/dark shading and colours it, where a flat
                    // fill or a mask would throw that shading away. The art carries its own edge,
                    // so the CSS border would double it.
                    ...(frameImage
                        ? {
                            backgroundImage: `url("${frameImage.dataUri}")`,
                            backgroundSize: '100% 100%',
                            backgroundRepeat: 'no-repeat',
                            backgroundColor: border,
                            backgroundBlendMode: 'multiply',
                            // The frame art includes its own 1-unit edge, and the slot row starting
                            // at 0,0 sat on top of it - clipping the upper-left border. Inset the
                            // contents by that edge so the frame reads as a frame on every side.
                            padding: u(1),
                        }
                        : { border: `${u(1)} solid ${border}` }),
                    height: u(panelHeight),
                    boxSizing: 'border-box',
                    display: 'flex',
                    overflow: 'hidden',
                }}
            >
                {/* Always three slots, filled left to right. Slots 1 and 3 take the lighter tone
                    and slot 2 a darker hue of it - that banding is the panel's own chrome, not
                    something the icons bring, so an empty slot still shows its tone.
                    Each occupied slot draws the referenced unit's own portrait: the server resolves
                    the unit the list names, then ITS icon, through exactly the same resolver the
                    card's own portrait uses - missing-icon placeholder included. A unit whose icon
                    cannot be found therefore looks the same here as anywhere else. The name only
                    appears when the unit names no icon at all, or is unknown. */}
                {Array.from({ length: CHROME.againstSlots }, (_, i) => {
                    const ref = refs[i];
                    return (
                        <div
                            key={ref?.objectId ?? `empty-${i}`}
                            title={ref === undefined
                                ? undefined
                                : ref.displayName
                                    ? `${ref.displayName} (${ref.objectId})`
                                    : ref.objectId}
                            style={{
                                width: u(slot),
                                // Fills the frame's interior, whatever aspect the frame art has.
                                height: '100%',
                                flex: '0 0 auto',
                                // The measured tones, NOT E_UNIT_AGAINST - and this one is settled by
                                // what the art turned out to be. The slot texture is UNIFORM, so
                                // tinting it cannot reproduce the light/dark/light banding the game
                                // shows across the three slots; screenshots of both attempts proved
                                // it (multiplying by the per-slot tone kept the banding but came out
                                // near-black, multiplying by the lightest variant got the brightness
                                // right and lost the banding). The banding therefore comes from the
                                // engine drawing alternate slots differently, not from the texture.
                                // These sampled fills - green rgb(0,89,0), red rgb(91,0,0) - already
                                // reproduce it exactly. `slotImage` stays on the wire.
                                background: i === 1 ? slotFillDark : slotFill,
                                boxSizing: 'border-box',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                padding: u(2),
                                ...fontOf('Arial'),
                                fontSize: u(4.5),
                                lineHeight: u(5),
                                color: 'rgba(255, 255, 255, 0.92)',
                                textAlign: 'center',
                                overflow: 'hidden',
                                wordBreak: 'break-word',
                            }}
                        >
                            {ref?.iconDataUri
                                ? (
                                    <img
                                        src={ref.iconDataUri}
                                        alt={ref.displayName ?? ref.objectId}
                                        style={{
                                            width: '100%',
                                            height: '100%',
                                            display: 'block',
                                            objectFit: 'fill',
                                            imageRendering: 'pixelated',
                                        }}
                                    />
                                )
                                // Reached only when the unit names NO icon, or is unknown - a unit
                                // whose icon merely failed to resolve already arrives as the
                                // placeholder. The name is the last resort, not the design.
                                : ref === undefined ? '' : ref.displayName ?? ref.objectId}
                        </div>
                    );
                })}
            </div>
        </div>
    );
}

/**
 * The rule under the class row, and again above the Against panels.
 *
 * Contributes NO height: it is zero-high in flow with the art overlaid absolutely, so the margins
 * either side are what position the body. The art is drawn at its own natural height - see
 * {@link CHROME.lineArtRowsBelowRule} for why that height cannot simply be squashed, and how the
 * anchor is chosen.
 */
function Divider(
    { art, u, marginTop, marginBottom }: {
        art?: EncyclopediaImage | null;
        u: (n: number) => string;
        marginTop: number;
        marginBottom?: number;
    },
): React.JSX.Element {
    const height = art?.height && art.height > 0 ? art.height : CHROME.lineArtHeight;

    return (
        <div
            style={{
                position: 'relative',
                height: 0,
                // The 1px rule is the fallback for an atlas without the art.
                ...(art ? {} : { borderTop: `${u(1)} solid ${CHROME.separator}` }),
                marginTop: u(marginTop),
                ...(marginBottom === undefined ? {} : { marginBottom: u(marginBottom) }),
            }}
        >
            {art && (
                <div
                    style={{
                        position: 'absolute',
                        left: 0,
                        right: 0,
                        top: u(-(height - CHROME.lineArtRowsBelowRule)),
                        height: u(height),
                        background: `url("${art.dataUri}") no-repeat center / 100% 100%`,
                        pointerEvents: 'none',
                    }}
                />
            )}
        </div>
    );
}

/**
 * The card itself.
 *
 * `zoom` multiplies every dimension and every font size by the same factor, so wrap points are
 * identical at any zoom - only legibility changes.
 */
export function EncyclopediaCard(
    { entry, zoom, factionSlot = 0 }: {
        entry: GetEncyclopediaEntryResult;
        zoom: number;
        /**
         * Which faction's frame to draw - an index into `chrome.factionFrames`, which the engine
         * would pick from the viewing player's faction. A preview has no player, so the caller
         * chooses. Out-of-range simply draws no frame, which is what the game does for a faction
         * past the last entry.
         */
        factionSlot?: number;
    },
): React.JSX.Element {
    const layout = entry.layout;
    // Chrome cut from the mega texture. Every piece is optional and every use falls back to the
    // calibrated CSS, so a mod whose atlas lacks these still gets the card it got before.
    const chrome = entry.chrome;
    const u = (n: number): string => `${n * zoom}px`;

    // With no Population_Value the game shifts the header left into the blip's space. The blip
    // does not sit beside the icon though - they overlap, the blip spanning 4.9..23.3 while the
    // icon starts at 13.0 - so this is a measured pair of insets, not icon + blip width.
    const hasBlip = entry.populationValue !== null && entry.populationValue !== undefined;
    const iconLeft = hasBlip ? CHROME.iconLeftWithBlip : CHROME.iconLeftNoBlip;
    // The band art already contains the disc, so the variant IS the blip's background.
    const topBarArt = hasBlip ? chrome?.topBar : chrome?.topBarNoBlip;
    // The band's own height, not an assumed 20: it is atlas art and a mod may ship it taller.
    const headerHeight = topBarArt?.height && topBarArt.height > 0
        ? topBarArt.height
        : CHROME.headerHeight;
    // The portrait at the size the engine would draw it. The slot stays a fixed ICON_SIZE so the
    // name and class line keep their left edge whatever the art measures, but the art itself is
    // drawn at its true proportions - several shipped portraits are NOT square (50x49, 47x47,
    // 44x45) and stretching them all into a square box visibly distorted those.
    const portrait = drawnSize(entry.icon, layout.iconScale, ICON_SIZE);
    // The name and the class line share this left edge - measured at x=206 and x=205 in the game's
    // galactic card. The name is NOT centred; it only looked centred in the tactical card because
    // that particular name happened to be long enough to fill the row.
    const textLeft = iconLeft + ICON_SIZE + CHROME.textGap;

    // The faction frame the engine would draw for the viewing player's faction. Absent art is
    // normal - the base game ships no frame past slot 1, and a mod may name one the atlas lacks.
    const factionFrame = chrome?.factionFrames?.[factionSlot]?.image;

    const hasAgainst = entry.goodAgainst.length > 0 || entry.vulnerableTo.length > 0;
    // Half the card's inner width, so the pair spans edge to edge with one gap between them.
    const againstWidth = (layout.width - 2 - CHROME.againstGap) / 2;

    return (
        <div
            style={{
                width: u(layout.width),
                // The backdrop art is drawn by the rotated layer below, so the CSS gradient here is
                // only the fallback for an atlas that ships no E_BACKGROUND.
                background: chrome?.background
                    ? 'transparent'
                    : `linear-gradient(${CHROME.backgroundTop}, ${CHROME.backgroundBottom})`,
                // The card's border IS the faction frame - it is the only per-faction art the
                // component names, no atlas entry supplies a border of its own, and both shipped
                // frames are edge art rather than fills. Drawn with border-image so the 4x4 flat
                // swatch and the 64x64 feathered rect each render as what they are; the measured
                // colour stays the fallback. Slicing at 25% takes the outer quarter of each edge,
                // which is the feathered ring on the empire frame and flat colour on the rebel one.
                border: `${u(1)} solid ${CHROME.border}`,
                ...(factionFrame
                    ? { borderImage: `url("${factionFrame.dataUri}") 25% stretch` }
                    : {}),
                // Anchors the backdrop layer, and the z-index makes this a stacking context so a
                // negative-z child cannot escape behind the panel's own background.
                position: 'relative',
                zIndex: 0,
                // No inset at the top: the grey band starts immediately under the border (the
                // game's border line sits at y=874 and the band at y=876, about one unit apart).
                // The band also runs the full width inside the border - x 65..535 on a card of
                // 60..530 - so the horizontal inset belongs to the text below it, not the card.
                padding: `0 0 ${u(layout.offsetY)} 0`,
                boxSizing: 'border-box',
            }}
        >
            {/*
              * The backdrop, ROTATED A QUARTER TURN.
              *
              * E_BACKGROUND is 48x48 and stretched across the card, not tiled - tiling is the
              * obvious reading of a small square texture and it is wrong, since the harness showed
              * visible seams where the game's backdrop is smooth. But the texture's gradient runs
              * HORIZONTALLY (rgb(22,33,73) at x=0 to rgb(17,25,56) at x=47, rows near-identical)
              * while the card's own pixels show it running vertically between those same two
              * colours - so the engine draws this one turned on its side.
              *
              * Rotating a stretched fill needs the box with its axes swapped, and the card's height
              * is content-driven. Container query units give it without measuring: 100cqh/100cqw
              * are the container's height and width, so the inner box is exactly the transpose.
              */}
            {chrome?.background && (
                <div
                    style={{
                        position: 'absolute',
                        inset: 0,
                        overflow: 'hidden',
                        containerType: 'size',
                        // Behind the content but still inside this card's stacking context.
                        zIndex: -1,
                    }}
                >
                    <div
                        style={{
                            position: 'absolute',
                            top: '50%',
                            left: '50%',
                            width: '100cqh',
                            height: '100cqw',
                            transform: 'translate(-50%, -50%) rotate(90deg)',
                            background:
                                `url("${chrome.background.dataUri}") no-repeat center / 100% 100%`,
                        }}
                    />
                </div>
            )}

            {/*
              * Header: a full-width grey band with the portrait laid OVER its left end.
              *
              * Scanning the game's own pixels across the band's row shows grey (83,83,83) at x=80,
              * which is left of the portrait, then the portrait, then grey again all the way to the
              * right edge - so the band runs edge to edge and the portrait sits on top of it. The
              * name is centred on the whole card too (its centre lands within a pixel of the
              * card's), not on the space beside the portrait.
              */}
            <div style={{ position: 'relative', height: u(CHROME.headerBlockHeight) }}>
                <div
                    style={{
                        position: 'absolute',
                        inset: `0 0 auto 0`,
                        height: u(headerHeight),
                        // E_TOPBAR is exactly 262x20 - the card's full width - so unlike the
                        // backdrop tile this one IS stretched to fit, which is a no-op at 1x.
                        // Two variants, and the choice is the blip: E_TOPBAR carries the black disc
                        // baked into its left end, E_TOPBAR2 is the same band without it. Falling
                        // back to E_TOPBAR when the no-blip variant is missing would stamp a disc
                        // on a card that has no population to put in it, so the CSS band is the
                        // safer fallback there.
                        background: topBarArt
                            ? `url("${topBarArt.dataUri}") no-repeat center / 100% 100%`
                            : CHROME.headerBand,
                        display: 'flex',
                        alignItems: 'center',
                        // Left-aligned on the same edge as the class line below it.
                        paddingLeft: u(textLeft),
                        boxSizing: 'border-box',
                    }}
                >
                    <span
                        style={{
                            ...textStyle(layout.header, layout, u),
                            color: '#ffffff',
                            whiteSpace: 'nowrap',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                        }}
                    >
                        {entry.displayName ?? entry.objectId}
                    </span>
                </div>

                {hasBlip && (
                    <div
                        style={{
                            position: 'absolute',
                            left: u(CHROME.blipLeft),
                            top: u(CHROME.blipTop),
                            width: u(CHROME.blipSize),
                            height: u(CHROME.blipSize),
                            // Over the icon: the two overlap by about half the blip's width.
                            zIndex: 3,
                            // NO disc when the band art is present - E_TOPBAR already carries one,
                            // and painting a second put a slightly-misplaced circle on top of the
                            // real one. The CSS disc survives only as the fallback for an atlas
                            // that supplied no band at all.
                            ...(topBarArt
                                ? {}
                                : { borderRadius: '50%', background: CHROME.blipFill }),
                            color: CHROME.blipLabel,
                            display: 'flex',
                            alignItems: 'center',
                            // Right-justified, per the TextButton's Text_Offset X of 12: the digit's
                            // right edge lands at 12 against a disc centred on 10, which is what
                            // makes the number sit a shade left of centre in the game rather than
                            // dead centre as a naive rendering does.
                            justifyContent: 'flex-end',
                            paddingRight: u(CHROME.blipSize + CHROME.blipLeft - CHROME.blipTextRight),
                            boxSizing: 'border-box',
                            ...fontOf('Arial Bold'),
                            fontSize: u(CHROME.blipSize * 0.62),
                            lineHeight: 1,
                        }}
                        title={`Population_Value ${entry.populationValue}`}
                    >
                        {entry.populationValue}
                    </div>
                )}

                <div
                    style={{
                        position: 'absolute',
                        left: u(iconLeft),
                        top: u(CHROME.iconTop),
                        width: u(ICON_SIZE),
                        height: u(ICON_SIZE),
                        // Above the divider, which runs the full width underneath it. The icon is
                        // taller than the header block, so it hangs over the line.
                        zIndex: 2,
                        // NO background and NO border. Mega-texture icons carry an alpha channel and
                        // the engine composites them straight onto the popup, so whatever is behind
                        // them - the grey header band at the top, the backdrop below it - shows
                        // through their transparent regions. Painting a slab here would put a box
                        // around every portrait that the game does not draw. Only the empty-slot
                        // placeholder below opts back into an outline.
                        boxSizing: 'border-box',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        ...fontOf('Arial'),
                        fontSize: u(5),
                        color: 'rgba(255, 255, 255, 0.45)',
                    }}
                    title={encyclopediaIconTitle(entry.icon)}
                >
                    {entry.icon ? (
                        <img
                            src={entry.icon.dataUri}
                            alt={entry.icon.name}
                            style={{
                                // Its OWN proportions, scaled the way the engine scales it - not
                                // stretched to fill the slot. "Every shipped portrait is square"
                                // was simply false: the atlas has 50x49, 47x47, 44x45 and more, and
                                // forcing those into a square box distorted them. Centred in the
                                // fixed slot so the name and class line keep their left edge.
                                width: u(portrait.width),
                                height: u(portrait.height),
                                display: 'block',
                                // Icons are small pixel art blown up several times at high zoom;
                                // smoothing them turns crisp 2006 artwork into mush.
                                imageRendering: 'pixelated',
                            }}
                        />
                    ) : (
                        // Nothing to composite: the object names no icon at all. This is an editor
                        // affordance rather than anything the game draws, so it gets its own faint
                        // outline - the alpha rules above only apply to real artwork.
                        <div
                            style={{
                                width: '100%',
                                height: '100%',
                                border: `${u(1)} solid rgba(255, 255, 255, 0.18)`,
                                boxSizing: 'border-box',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                            }}
                        >
                            icon
                        </div>
                    )}
                </div>

                <div
                    style={{
                        position: 'absolute',
                        left: u(textLeft),
                        // Inset from the card edge, not flush to it: the game's right-hand ability
                        // slot ends about one content inset short of the border.
                        right: u(layout.offsetX),
                        top: u(headerHeight),
                        bottom: 0,
                        display: 'flex',
                        // Top-aligned, not centred: the game sets the class line immediately under
                        // the header band. The row is taller than the text because the ability
                        // icons hang below it, so centring opens a gap the game does not have.
                        alignItems: 'flex-start',
                        gap: u(2),
                        boxSizing: 'border-box',
                    }}
                >
                        <span
                            style={{
                                // Body font, not the header's: the game draws the name bold and the
                                // class line in the ordinary face, just white rather than grey.
                                ...textStyle(layout.body, layout, u),
                                color: '#ffffff',
                                flex: '1 1 0',
                                minWidth: 0,
                                whiteSpace: 'nowrap',
                                overflow: 'hidden',
                                textOverflow: 'ellipsis',
                            }}
                        >
                            {/* Verbatim - do NOT prepend "Class: ". That label is part of the
                                translation, not something the engine adds: the shipped English
                                MasterTextFile stores whole values like "Class: Turret" and
                                "Class: Tank", and it could not be hardcoded anyway since the game
                                ships French, German, Spanish and Italian. Prefixing it here
                                rendered "Class: Class: Corvette". */}
                            {entry.unitClass ?? ''}
                        </span>

                    {/* Exactly two slots: ability 0 left, ability 1 right. Anything further down
                        the list is simply not drawn - the game does the same, and modders rely on
                        it to keep auto-activated abilities off the UI or to drive tactical GUI
                        grouping, so the extras are intent rather than overflow to surface.
                        The artwork is a placeholder: an ability's ordinary icon is nowhere in the
                        data, only the alternate-state one, so the engine must supply it. The type
                        stands in so a slot still says which ability it is. */}
                    {entry.abilities.slice(0, CHROME.abilitySlots).map(ability => {
                        // Sized from THIS icon: the ability scale applies to the art's own
                        // dimensions, so two abilities whose icons differ in size get slots that
                        // differ the same way, exactly as the engine draws them.
                        const slot = abilitySlotSize(layout, ability.icon);
                        return (
                        <div
                            key={ability.abilityName ?? ability.type}
                            style={{
                                width: u(slot),
                                height: u(slot),
                                flex: '0 0 auto',
                                // No background and no border, for the same reason as the portrait:
                                // the game draws no slot chrome here. Ability icons merely LOOK like
                                // filled squares because most of them are opaque and square - that
                                // is the artwork, not a frame around it. Painting one would show
                                // through every icon that is not.
                                boxSizing: 'border-box',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                // No padding: the engine scales the atlas cut to fill the slot, so
                                // insetting it here drew every icon a unit smaller than the game's.
                                // The text fallback below opts back into its own padding.
                                ...fontOf('Arial'),
                                fontSize: u(3.4),
                                lineHeight: u(3.8),
                                color: 'rgba(255, 255, 255, 0.9)',
                                textAlign: 'center',
                                overflow: 'hidden',
                                wordBreak: 'break-word',
                            }}
                            title={
                                `${ability.type}${ability.abilityName ? ` (${ability.abilityName})` : ''}`
                                + `${ability.alternateIconName
                                    ? ` - alternate icon ${ability.alternateIconName}` : ''}`
                            }
                        >
                            {ability.icon ? (
                                <img
                                    src={ability.icon.dataUri}
                                    alt={ability.type}
                                    style={{
                                        width: '100%',
                                        height: '100%',
                                        display: 'block',
                                        objectFit: 'fill',
                                        imageRendering: 'pixelated',
                                    }}
                                />
                            ) : (
                                // No icon resolved - the engine's ability-to-icon mapping is not in
                                // the data, so this is expected for a good share of abilities. The
                                // type text is still the most useful thing to show, and unlike the
                                // icon it needs an outline to read as a slot at all. That outline is
                                // an editor affordance; the game draws nothing here.
                                <div
                                    style={{
                                        width: '100%',
                                        height: '100%',
                                        background: CHROME.abilitySlot,
                                        border: `${u(1)} solid rgba(255, 255, 255, 0.25)`,
                                        boxSizing: 'border-box',
                                        padding: u(0.5),
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center',
                                        overflow: 'hidden',
                                    }}
                                >
                                    {ability.type}
                                </div>
                            )}
                        </div>
                        );
                    })}
                </div>
            </div>

            {/* Everything below the band keeps the horizontal inset, so the body's content box
                stays 250 units (262 less the 1-unit border and 5-unit inset per side) - the width
                the wrap calibration is fitted to. */}
            <div style={{ padding: `0 ${u(layout.offsetX)}` }}>
            {/* Full width. An earlier scan found no line to the left of the icon and I read that as
                the divider starting at the icon's edge - but the icon is 50 units tall in a 42-unit
                header block, so at the divider's row the scan was hitting the icon, which is drawn
                over the line. */}
            <Divider
                art={chrome?.line}
                u={u}
                marginTop={CHROME.separatorGap}
                marginBottom={CHROME.bodyTopGap}
            />

            {/* Body runs the full width, under the portrait - not in the column beside it. */}
            <div>
                {entry.body.map((line, i) => (
                    line.text === null || line.text === undefined
                        ? (
                            <div
                                key={`${line.key}-${i}`}
                                style={{ ...textStyle(layout.body, layout, u), color: '#f04c4c' }}
                                title={`No translation loaded for '${line.key}'`}
                            >
                                {`<${line.key}>`}
                            </div>
                        )
                        : (
                            <div key={`${line.key}-${i}`} style={textStyle(layout.body, layout, u)}>
                                {line.text}
                            </div>
                        )
                ))}
            </div>
            </div>

            {/* All or nothing: either tag being set brings up BOTH panels, so the six slots always
                appear together and an empty counterpart shows as an empty box rather than being
                dropped. The pair spans the card edge to edge rather than sitting inside the body's
                inset - the game's boxes start about 0.6 units from the border. */}
            {/* The game repeats the divider above this section, but only when the section is there
                at all - it separates the body from the panels, so with no panels there is nothing
                to separate. Same art and same core-row offset as the divider under the class row. */}
            {hasAgainst && <Divider art={chrome?.line} u={u} marginTop={CHROME.sectionGap} />}

            {hasAgainst && (
                <div
                    style={{
                        display: 'flex',
                        gap: u(CHROME.againstGap),
                        marginTop: u(CHROME.sectionGap),
                    }}
                >
                    <AgainstPanel
                        label="Strong Against:"
                        refs={entry.goodAgainst}
                        layout={layout}
                        u={u}
                        width={againstWidth}
                        labelColor={CHROME.strongLabel}
                        slotFill={CHROME.strongSlot}
                        slotFillDark={CHROME.strongSlotDark}
                        border={CHROME.strongBorder}
                        frameImage={chrome?.againstFrame}
                        slotImage={chrome?.unitAgainst}
                    />
                    <AgainstPanel
                        label="Weak Against:"
                        refs={entry.vulnerableTo}
                        layout={layout}
                        u={u}
                        width={againstWidth}
                        labelColor={CHROME.weakLabel}
                        slotFill={CHROME.weakSlot}
                        slotFillDark={CHROME.weakSlotDark}
                        border={CHROME.weakBorder}
                        frameImage={chrome?.againstFrame}
                        slotImage={chrome?.unitAgainst}
                    />
                </div>
            )}

        </div>
    );
}
