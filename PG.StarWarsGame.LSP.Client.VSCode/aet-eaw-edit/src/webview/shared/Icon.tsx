// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every icon the webviews draw, named by what it MEANS.
//
// Call sites ask for `skeleton` or `damage`, never for a library's own name. That is the whole point
// of this file: the mapping from a meaning to a glyph lives in one table, so re-drawing an icon, or
// changing icon library altogether, is an edit here and nowhere else. The preview had icons from
// three different naming schemes before this - a codicon class string, a pictogram, a bare word -
// and no way to see what the set as a whole looked like. A lint holds every other file to it:
// `codicon-` is named here, in SeverityTag.tsx and in ProblemsPanel.tsx, and nowhere else.
//
// Tabler (MIT) rather than codicons for the preview specifically. Codicons is an EDITOR icon set:
// 535 glyphs with no ground plane, no skeleton, no mesh, no LOD, no axes - the vocabulary a 3D
// viewport is made of. Tabler carries all of them, so the alternative was hand-drawing a domain set,
// which is more work and worse. The preview reads a little more like a modelling tool than the rest
// of the extension as a result; that boundary is deliberate and sits BETWEEN editors rather than
// inside one toolbar.
//
// React components, so each icon inlines as SVG: no font file, no stylesheet link, and nothing for
// the webview's content-security policy to allow. Tree-shaken, so only what this table names ships.

import {
    IconActivity,
    IconAdjustments,
    IconAlertTriangle,
    IconArrowBackUp,
    IconArrowDown,
    IconArrowForward,
    IconArrowLeft,
    IconArrowRight,
    IconArrowsHorizontal,
    IconArrowUp,
    IconAsterisk,
    IconAtom,
    IconAxisX,
    IconBolt,
    IconBone,
    IconBook,
    IconBox,
    IconBrightnessHalf,
    IconCamera,
    IconCategory,
    IconCheck,
    IconChevronDown,
    IconChevronRight,
    IconChevronUp,
    IconCircleDot,
    IconCircleOff,
    IconCircleX,
    IconClipboardCopy,
    IconCloudDownload,
    IconCoins,
    IconCopy,
    IconCrosshair,
    IconCube,
    IconCube3dSphere,
    IconDeviceFloppy,
    IconDots,
    IconEngineOff,
    IconExternalLink,
    IconEye,
    IconEyeClosed,
    IconEyeDotted,
    IconFileCode,
    IconFilter,
    IconFolderOpen,
    IconFilterOff,
    IconFlag,
    IconFlame,
    IconGauge,
    IconGift,
    IconGridDots,
    IconHandClick,
    IconHeading,
    IconHeartBroken,
    IconHierarchy,
    IconHistory,
    IconId,
    IconInfoCircle,
    IconLayersUnion,
    IconLayoutGrid,
    IconLetterCase,
    IconLink,
    IconListDetails,
    IconListTree,
    IconLock,
    IconMesh,
    IconMessage,
    IconMessageCheck,
    IconMinus,
    IconMovie,
    IconPackage,
    IconPackageExport,
    IconPencil,
    IconPerspective,
    IconPhoto,
    IconPin,
    IconPlanet,
    IconPlayerPause,
    IconPlayerPlay,
    IconPlayerSkipBack,
    IconPlayerSkipForward,
    IconPlayerStop,
    IconPlayerTrackNext,
    IconPlayerTrackPrev,
    IconPoint,
    IconPlus,
    IconPolygon,
    type IconProps,
    IconRadar,
    IconRefresh,
    IconRegex,
    IconRepeat,
    IconRoute,
    IconRuler,
    IconSchema,
    IconScript,
    IconSearch,
    IconSelector,
    IconSeparatorHorizontal,
    IconSettings,
    IconShadow,
    IconSparkles,
    IconStack2,
    IconStairsUp,
    IconSun,
    IconSunHigh,
    IconSunLow,
    IconSwords,
    IconTag,
    IconTank,
    IconTargetArrow,
    IconTexture,
    IconTimeline,
    IconTool,
    IconTransform,
    IconTrash,
    IconTypography,
    IconUsers,
    IconView360,
    IconVolume,
    IconWind,
    IconWorld,
    IconX,
} from "@tabler/icons-react";
import { type ComponentType } from "react";

/**
 * What the preview can draw, by meaning.
 *
 * Grouped the way the UI is: what the model is made of, what the room is, where you look from, and
 * the plain actions. A name here is a promise about MEANING - if a control's meaning changes, it
 * gets a different name rather than quietly keeping a glyph that no longer fits.
 */
const ICONS = {
    // ── what the model is made of ─────────────────────────────────────────────
    skeleton: IconBone,
    mesh: IconMesh,
    geometry: IconPolygon,
    collision: IconCube,
    shadowVolume: IconShadow,
    effects: IconFlame,
    texture: IconTexture,
    detail: IconStack2,

    // A break, not a prohibition. This was a crossed-out circle, which is the glyph for OFF - and
    // it was the same glyph the faction palette uses for "no team tint", where it is right. Seen
    // side by side on a rendered panel, the damage slider read as a disabled control.
    damage: IconHeartBroken,

    // A tank, because its turret is the one shape that says turret: there is no glyph for a turret
    // alone, and a crosshair is already the targeting mark.
    turret: IconTank,

    // What a unit is doing or has had done to it, beside its damage state: how fast it is going,
    // an ion stun - a charge, which the lens already has the bolt for - and its engines disabled
    // for a time.
    speed: IconGauge,
    ionStun: IconAtom,
    enginesDisabled: IconEngineOff,

    // ── the room ──────────────────────────────────────────────────────────────
    // A globe, not an aperture: this opens the settings for the ROOM the model stands in, and an
    // aperture reads as photography - which is the camera panel opposite.
    scene: IconWorld,

    // A trapezoid - a rectangle receding into the distance, which is what a ground plane looks
    // like from any camera that is not directly above it. Two others were tried and rejected by
    // looking at them: a mountain read as scenery, and Tabler's `baseline` draws a literal letter A
    // on a line, which reads as text formatting.
    ground: IconPerspective,
    grid: IconGridDots,
    light: IconSun,
    lightHigh: IconSunHigh,
    lightLow: IconSunLow,
    wind: IconWind,
    bloom: IconSparkles,

    // A disc half in shade: a shadow the preview casts in place of one the game does not draw.
    // Not `shadowVolume`'s glyph, which is the model's own shadow mesh - the thing this stands in for.
    standInShadow: IconBrightnessHalf,

    // A sweep of coverage from a point, which is what a weapon's firing arc is.
    arcs: IconRadar,

    // The mark the game puts over something you can shoot at. A crosshair rather than a dot or a
    // ring: it is the shape of the artwork it switches on.
    target: IconCrosshair,

    // FIRING at it, which is a different thing from the mark drawn over it: `target` is the mark,
    // this is the act. A target with an arrow struck into it, so the two do not read alike sitting
    // in the same corner of the stage.
    fire: IconTargetArrow,

    // Putting it back together. A wrench, not the undo arrow it used to borrow - undo is a
    // reversal of what YOU did, and repairing a hull is a thing done TO the model.
    repair: IconTool,

    // What has already happened, in order. Not a plain list: the log is a RECORD of shots, and the
    // clock face says the order is the point of it.
    log: IconHistory,

    // ── where you look from ───────────────────────────────────────────────────
    camera: IconCamera,
    axes: IconAxisX,
    capture: IconPhoto,
    frame: IconLayoutGrid,

    // ── plain actions ─────────────────────────────────────────────────────────
    // The absence of a choice, where a choice is normally shown - the faction palette's swatch for
    // no team tint at all. Distinct from `hidden`, which is about whether a thing is DRAWN.
    none: IconCircleOff,
    // The tree row's three states, which are Blender's - see `preview/rowEye.ts`. `inherited` is
    // the one that needed a third glyph: a row undrawn because something ABOVE it is hidden, which
    // this control cannot change. A dotted eye reads as an answer nobody asserted, which is exactly
    // what it is.
    visible: IconEye,
    inherited: IconEyeDotted,
    hidden: IconEyeClosed,
    details: IconInfoCircle,
    warning: IconAlertTriangle,
    error: IconCircleX,
    // Narrowing a list to what you are looking for. Not `measure` and not `details` - this one is
    // about what is SHOWN, and the two beside it are about reading one thing closely.
    search: IconSearch,
    settings: IconSettings,
    tune: IconAdjustments,
    measure: IconRuler,
    play: IconPlayerPlay,
    pause: IconPlayerPause,
    stop: IconPlayerStop,

    // The transport, spelled the way a CD player spells it - which is the reader's own reference,
    // and the only vocabulary these four have. A clip is a TRACK and a frame is a position within
    // it, so the double-arrow pair steps between clips and the single-arrow pair runs to an end of
    // the one playing. "Last frame" used to draw a bare play triangle, which is the same glyph as
    // Play and so said the opposite of what it did.
    previousClip: IconPlayerTrackPrev,
    firstFrame: IconPlayerSkipBack,
    lastFrame: IconPlayerSkipForward,
    nextClip: IconPlayerTrackNext,
    restart: IconRefresh,
    loop: IconRepeat,
    reset: IconArrowBackUp,
    add: IconPlus,
    // Stepping a number down. Not `remove`, which deletes and is drawn as a trash can.
    decrement: IconMinus,
    // Editing a name in place.
    rename: IconPencil,
    remove: IconTrash,
    copy: IconClipboardCopy,
    save: IconDeviceFloppy,
    close: IconX,
    check: IconCheck,
    download: IconCloudDownload,
    open: IconExternalLink,
    moreActions: IconDots,
    /** Keeps a preview tab open, so the next entry opened gets a tab of its own. */
    pin: IconPin,
    /** The column a table is sorted by, and which way. */
    sortAscending: IconArrowUp,
    sortDescending: IconArrowDown,

    // What an entry of the Mod Browser opens in: the assembled unit or a model in the 3D preview,
    // the object's encyclopedia card, the object with its variant chain merged.
    preview3d: IconView360,
    encyclopediaCard: IconId,
    effectiveObject: IconLayersUnion,

    // Jumping to where a name is DECLARED, which is not the same as `open` - that one is an
    // external link and means "hand this to another application". Drawn as the EDITOR's own
    // go-to-file, because the story graph already draws it that way and one action must not look
    // like two. `CODICONS` below routes it; the entry stays here so the meaning is still listed.
    definition: IconFileCode,
    /** Shows a file in the operating system's file manager. */
    reveal: IconFolderOpen,
    expanded: IconChevronDown,
    collapsed: IconChevronRight,
    // A band that folds away upwards and opens downwards - the dock's header. The chevron points
    // the way pressing it moves the band, which is not what `expanded` and `collapsed` say: those
    // mark a row whose content sits below it.
    foldUp: IconChevronUp,
    unfoldDown: IconChevronDown,
    /** A list to pick from, opening whichever way it finds room. The mark on a PillSelect. */
    picker: IconSelector,

    bounds: IconBox,

    // ── the story graph's own tools ───────────────────────────────────────────
    // Taking every filter OFF at once. Not `reset`, which undoes what you did to the SUBJECT, and
    // not `search`, which is the control that put a filter on in the first place.
    clearFilter: IconFilterOff,
    // A column's own filter, in its header.
    filter: IconFilter,

    // Recomputing the automatic layout. The glyph is the shape the layout produces - a graph laid
    // out in ranks - because "arrange" on its own could mean sorting a list.
    arrange: IconHierarchy,

    // The two swimlane groupings. A thread is a sequence you follow down the graph and a chapter is
    // a division of the story, so one is drawn as an outline and the other as a book. They are a
    // PAIR and must not converge: the two are toggled independently and the reader has to see which
    // of them is on.
    threadLanes: IconListTree,
    chapterLanes: IconBook,

    // The find widget's three modes. Listed here so the whole set can be read in one place, but
    // `CODICONS` below routes all three to the editor's own marks - see the note there.
    searchLiteral: IconLetterCase,
    searchWildcard: IconAsterisk,
    searchRegex: IconRegex,

    // ── the story simulation ──────────────────────────────────────────────────
    // What the running story is waiting on the AUTHOR for: a hand on a control, because every
    // decision here is the author standing in for the player.
    decision: IconHandClick,
    // The fact table the story reads - planets, units, tech, credits. The same globe as the
    // preview's room, and never beside it: one is the world a model stands in, this is the world a
    // story plays in.
    world: IconWorld,
    planet: IconPlanet,
    unit: IconUsers,
    tech: IconStairsUp,
    credits: IconCoins,
    flag: IconFlag,
    // A battle's outcome, which the author decides rather than fights.
    tactical: IconSwords,
    // A campaign script's state machine, drawn as a script rather than as a file: the file is what
    // `definition` opens; this is the thing running inside it.
    script: IconScript,
    // The record of what fired when. A timeline, not the preview's clock-faced `log`: the ticks
    // are the point of it.
    trace: IconTimeline,
    breakpoint: IconCircleDot,
    // Play picks itself up again once the decision it stopped for is answered.
    autoResume: IconArrowForward,
    // A speech or movie ends on its own on the next tick, rather than waiting for the author.
    mediaCompletes: IconMessageCheck,
    // The tint that shows which edges the story has run along.
    flow: IconRoute,
    // Stepping INTO a battle's own graph from its node, and back OUT to the campaign event that
    // opens it. A pair of plain arrows: the two are a direction of travel between graphs.
    openSubgraph: IconArrowRight,
    openParentGraph: IconArrowLeft,
    // What an event node's type chip is: the condition that arms it, or what it hands out.
    trigger: IconBolt,
    reward: IconGift,
    // Taking the type off a node, which keeps the node. Not `remove`, which deletes and is a trash
    // can: this one is undone by choosing a type again.
    detach: IconX,
    // A node another project owns, which this one can read and not change.
    readOnly: IconLock,
    // The three ways a path filter runs from an event: what leads to it, both, what it leads to.
    incoming: IconArrowLeft,
    bothDirections: IconArrowsHorizontal,
    outgoing: IconArrowRight,

    // ── the mode dials ────────────────────────────────────────────────────────
    // A position is a 24px circle and the readout a 40px one, so these are thin, open glyphs: a
    // dense or already-round one fills its button edge to edge and the dial stops reading as a
    // dial. The same shapes the codicon set drew before (play, package, zap, eye, edit, play), so
    // a reader who knew the dials finds every position where it was.
    animationLens: IconPlayerPlay,
    modelLens: IconPackage,
    gameplayLens: IconBolt,
    viewMode: IconEye,
    editMode: IconPencil,
    simulationMode: IconPlayerPlay,

    // ── the localisation and credits docks ────────────────────────────────────
    // Filling one language's empty entries from another's text.
    fillLanguage: IconCopy,
    convertFormat: IconTransform,
    // Writing the files the game loads, which leave the editor as a package.
    exportFile: IconPackageExport,
    // The three kinds of credits line, by what each looks like on the crawl: a heading, a name in
    // larger type, a gap.
    creditsLabel: IconHeading,
    creditsName: IconTypography,
    creditsBlank: IconSeparatorHorizontal,
    // The aside under a dialog's buttons. Not `details`, which opens more about one thing: this one
    // is a sentence already on screen.
    note: IconInfoCircle,

    // What the Mod Browser lists, by what each thing is. A game object is an object of an XML type,
    // never a unit: most of them are not units. `script` above is the Lua script, as the story's.
    gameObject: IconCategory,
    model: IconCube3dSphere,
    animation: IconMovie,
    audio: IconVolume,
    speech: IconMessage,
    enumeration: IconListDetails,
    references: IconLink,
    tag: IconTag,
    // The Schema outline's types and behaviours.
    schemaType: IconSchema,
    behaviour: IconActivity,
    // One value of an enum.
    enumValue: IconPoint,
} satisfies Record<string, ComponentType<IconProps>>;

export type IconName = keyof typeof ICONS;

// ── Which meanings the EDITOR already has a glyph for ─────────────────────────
//
// Tabler answers the domain because codicons has no 3D vocabulary - no ground plane, no skeleton,
// no mesh, no LOD, no axes - and hand-drawing that set would be more work and worse. That argument
// covers the DOMAIN and stops there.
//
// A handful of marks are the editor's own and a reader has learned them there. "Go to where this
// is defined" is drawn by the editor as `go-to-file`; answering it with a different picture here
// makes one action look like two. The table below routes those meanings past their Tabler entries,
// so a call site still asks for a meaning and this decides which set answers it.

/** Meanings that keep the editor's glyph rather than taking Tabler's. */
const CODICONS = {
    definition: "go-to-file",

    // The find widget's three search modes, and the same argument again. A reader recognises Aa,
    // the star and .* from the editor's own search box - they are not descriptions of anything,
    // they are learned marks - so redrawing them in another set would make a control someone
    // already knows look like a new one. Tabler entries exist for all three; this routes past them.
    searchLiteral: "case-sensitive",
    searchWildcard: "star-full",
    searchRegex: "regex",
} as const satisfies Partial<Record<IconName, string>>;

type CodiconMeaning = keyof typeof CODICONS;

/** Every meaning answered by a codicon, for the test that keeps this list honest. */
export const CODICON_NAMES = Object.keys(CODICONS) as CodiconMeaning[];

/** The codicon name for a meaning, or null when Tabler answers it. */
export function codiconFor(name: string): string | null {
    return CODICONS[name as CodiconMeaning] ?? null;
}

export interface IconProps_ {
    name: IconName;

    /**
     * Pixel size. 16 by default, which is the size the rest of this extension's chrome is drawn at -
     * Tabler's own default is 24 and would tower over everything beside it.
     */
    size?: number;
}

export function Icon({ name, size = 16 }: IconProps_): React.JSX.Element {
    // An EDITOR mark keeps the editor's own glyph - see `CODICONS` above. Everything else is
    // Tabler's.
    const codicon = codiconFor(name);

    if (codicon !== null) {
        return (
            <span
                className={`codicon codicon-${codicon}`}
                style={{ fontSize: size }}
                aria-hidden="true"
            />
        );
    }

    const Glyph = ICONS[name];

    // `currentColor` and aria-hidden are the two things that make an icon behave: it takes the
    // colour of whatever it sits in - including a disabled or a warning state - and it is invisible
    // to a screen reader, which reads the control's own label instead.
    return (
        <Glyph size={size} stroke={2} color="currentColor" aria-hidden="true" />
    );
}

/** The three severities a problem row can carry, which are also the editor's codicon names. */
export type Severity = "error" | "warning" | "info";

/**
 * A problem's severity mark: the editor's own error, warning and info codicons.
 *
 * The one family drawn from codicons on purpose, so a finding here carries the mark the Problems view
 * and the gutter show for the same finding. Coloured by the row it sits in; `className` adds the
 * `sev-*` class where a row colours the mark itself.
 */
export function SeverityMark(props: {
    severity: Severity;
    className?: string;
}): React.JSX.Element {
    return (
        <span
            className={
                `codicon codicon-${props.severity}` +
                (props.className ? ` ${props.className}` : "")
            }
            aria-hidden="true"
        />
    );
}
