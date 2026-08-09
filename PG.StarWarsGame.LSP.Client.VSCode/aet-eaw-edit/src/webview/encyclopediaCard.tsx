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
//   - Everything the components do NOT carry: the band height, the header block height, the
//     separator and the Strong/Weak panels. Those live in CHROME below and were measured by
//     sampling pixels out of a screenshot of the running game. They are calibration, not data, and
//     they are the part to re-check when something looks off - preferably by sampling again rather
//     than by eye, which got several of them wrong.

import {
    EncyclopediaLayout, EncyclopediaRgba, EncyclopediaTextStyle, GetEncyclopediaEntryResult,
} from '../protocol/encyclopedia';

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
 * The face the popup is measured in.
 *
 * `encyclopedia_text` says `Arial`, and Arial is provably not what the engine wraps with: the game
 * keeps "Skywalker trained under Jedi Master Yoda" on one line but breaks before "to become the
 * first of a new generation of", and in Arial the string it KEEPS is 1.6% *wider* than the one it
 * BREAKS - so no width can satisfy both. That holds for fractional/kerned measurement and for
 * GDI-style integer per-glyph advances at every size from 8px to 22px. Sweeping the common Windows
 * faces, Tahoma satisfies every break with a 1.5% margin, and it was the standard Windows UI font
 * of this game's era. Only Arial is remapped; a mod naming any other font gets what it asked for.
 */
function cssFontStack(gameFontName: string): string {
    const family = gameFontName.trim();
    return /^arial$/i.test(family)
        ? "'Tahoma', 'Verdana', sans-serif"
        : `'${family}', 'Tahoma', sans-serif`;
}

/**
 * The unit icon, drawn 50 units square.
 *
 * Measured off an annotated screenshot: the icon spans 53.6 x 47.1 units, averaging ~50. Note
 * `layout.iconScale` (encyclopedia_icon's `Size` X, stock 0.75) is NOT applied here - taking it as
 * a draw scale gives 37.5, a third too small against that measurement, so whatever that value
 * scales it is not the header icon's drawn size. The measurement wins; the field stays on the wire
 * because the Y half is the ability-icon scale, which the icons work will need.
 */
const ICON_SIZE = 50;

const CHROME = {
    /**
     * Band top to the divider. The icon is TALLER than this and deliberately overflows it - its
     * bottom sits about 9 units below the divider, which is why a scan across the divider's row
     * finds the icon rather than the line on the left.
     */
    headerBlockHeight: 38,
    /** Full width, and the icon is drawn ON TOP of its left end - not beside it. */
    headerHeight: 21,
    /** The icon's own inset. Larger when a blip is present, since the two overlap. */
    iconLeftWithBlip: 13,
    iconLeftNoBlip: 8,
    iconTop: 1,
    /** Gap between the icon's right edge and the name/class left edge (measured 69.3 - 63). */
    textGap: 6,
    /** The population blip: a yellow disc, measured at 18.4 units. */
    blipSize: 18,
    blipTop: 2,
    /** Ability slots at the right of the class row, measured ~34px = ~19 units. */
    abilityIconSize: 19,
    abilityGap: 2,

    /**
     * The Strong/Weak Against panels are STATICALLY sized - both measure 229x95px whatever they
     * contain - and they span the card edge to edge rather than sitting inside the text inset.
     * They are also all-or-nothing: either tag being present brings up BOTH panels, so the pair
     * always shows its six slots together rather than one panel appearing alone.
     *
     * Their geometry is DERIVED, not measured: each panel holds exactly three square icon slots,
     * so the slot is a third of the panel's interior width and the interior is one slot tall.
     * Measuring agrees - slot 2 spans 75px in both panels against a ~226px interior.
     */
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
    abilitySlot: 'rgba(110, 126, 158, 0.70)',
    /**
     * The blip is a BLACK disc with a GOLD numeral, not the other way round. A cut straight
     * through it reads rgb(0,0,0) either side of a rgb(255,215,56) stroke. An earlier reading of
     * rgb(206,173,45) as the fill was an antialiased edge pixel of the digit itself - the yellow
     * region measured 7px wide and 14px tall, which is a "1", not a 30px disc.
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
    }: {
        label: string;
        refs: readonly { objectId: string; displayName?: string | null }[];
        layout: EncyclopediaLayout;
        u: (n: number) => string;
        width: number;
        labelColor: string;
        slotFill: string;
        slotFillDark: string;
        border: string;
    },
): React.JSX.Element {
    // Three square slots fill the interior exactly, so the slot size is the interior width over
    // three - and the interior is one slot tall.
    const slot = (width - 2) / CHROME.againstSlots;
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
                    border: `${u(1)} solid ${border}`,
                    height: u(slot + 2),
                    boxSizing: 'border-box',
                    display: 'flex',
                    overflow: 'hidden',
                }}
            >
                {/* Always three slots, filled left to right. Slots 1 and 3 take the lighter tone
                    and slot 2 a darker hue of it - that banding is the panel's own chrome, not
                    something the icons bring, so an empty slot still shows its tone.
                    The game draws each referenced unit's icon in a slot. Textures are not decoded
                    yet, so an occupied slot stands in with the unit's name set very small - enough
                    to answer "did I point this at the right unit?" without faking the artwork. */}
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
                                height: u(slot),
                                flex: '0 0 auto',
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
                            {ref === undefined ? '' : ref.displayName ?? ref.objectId}
                        </div>
                    );
                })}
            </div>
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
    { entry, zoom }: { entry: GetEncyclopediaEntryResult; zoom: number },
): React.JSX.Element {
    const layout = entry.layout;
    const u = (n: number): string => `${n * zoom}px`;

    // With no Population_Value the game shifts the header left into the blip's space. The blip
    // does not sit beside the icon though - they overlap, the blip spanning 4.9..23.3 while the
    // icon starts at 13.0 - so this is a measured pair of insets, not icon + blip width.
    const hasBlip = entry.populationValue !== null && entry.populationValue !== undefined;
    const iconLeft = hasBlip ? CHROME.iconLeftWithBlip : CHROME.iconLeftNoBlip;
    // The name and the class line share this left edge - measured at x=206 and x=205 in the game's
    // galactic card. The name is NOT centred; it only looked centred in the tactical card because
    // that particular name happened to be long enough to fill the row.
    const textLeft = iconLeft + ICON_SIZE + CHROME.textGap;

    const hasAgainst = entry.goodAgainst.length > 0 || entry.vulnerableTo.length > 0;
    // Half the card's inner width, so the pair spans edge to edge with one gap between them.
    const againstWidth = (layout.width - 2 - CHROME.againstGap) / 2;

    return (
        <div
            style={{
                width: u(layout.width),
                background:
                    `linear-gradient(${CHROME.backgroundTop}, ${CHROME.backgroundBottom})`,
                border: `${u(1)} solid ${CHROME.border}`,
                // No inset at the top: the grey band starts immediately under the border (the
                // game's border line sits at y=874 and the band at y=876, about one unit apart).
                // The band also runs the full width inside the border - x 65..535 on a card of
                // 60..530 - so the horizontal inset belongs to the text below it, not the card.
                padding: `0 0 ${u(layout.offsetY)} 0`,
                boxSizing: 'border-box',
            }}
        >
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
                        height: u(CHROME.headerHeight),
                        background: CHROME.headerBand,
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
                            left: u(layout.offsetX),
                            top: u(CHROME.blipTop),
                            width: u(CHROME.blipSize),
                            height: u(CHROME.blipSize),
                            // Over the icon: the two overlap by about half the blip's width.
                            zIndex: 3,
                            borderRadius: '50%',
                            background: CHROME.blipFill,
                            color: CHROME.blipLabel,
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
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
                        // Opaque: the real portrait covers the band it sits on, so a translucent
                        // placeholder would let the grey through and misrepresent the layering.
                        background: 'rgb(12, 18, 38)',
                        border: `${u(1)} solid rgba(255, 255, 255, 0.22)`,
                        boxSizing: 'border-box',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        ...fontOf('Arial'),
                        fontSize: u(5),
                        color: 'rgba(255, 255, 255, 0.45)',
                    }}
                    title="Unit icon - textures are not decoded yet"
                >
                    icon
                </div>

                <div
                    style={{
                        position: 'absolute',
                        left: u(textLeft),
                        right: 0,
                        top: u(CHROME.headerHeight),
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

                    {/* One slot per GUI-activated ability the object actually has - none, one or
                        two. The artwork is still a placeholder: an ability's ordinary icon is not
                        in the data anywhere, only the alternate-state one, so the engine must
                        supply it. The type stands in so the slot still identifies its ability. */}
                    {entry.abilities.map(ability => (
                        <div
                            key={ability.abilityName ?? ability.type}
                            style={{
                                width: u(CHROME.abilityIconSize),
                                height: u(CHROME.abilityIconSize),
                                flex: '0 0 auto',
                                background: CHROME.abilitySlot,
                                border: `${u(1)} solid rgba(255, 255, 255, 0.25)`,
                                boxSizing: 'border-box',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                padding: u(0.5),
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
                            {ability.type}
                        </div>
                    ))}
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
            <div
                style={{
                    borderTop: `${u(1)} solid ${CHROME.separator}`,
                    marginTop: u(CHROME.separatorGap),
                    marginBottom: u(CHROME.bodyTopGap),
                }}
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
                    />
                </div>
            )}
        </div>
    );
}
