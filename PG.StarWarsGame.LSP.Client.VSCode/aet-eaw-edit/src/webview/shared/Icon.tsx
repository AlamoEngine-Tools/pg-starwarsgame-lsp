// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every icon the model preview draws, named by what it MEANS.
//
// Call sites ask for `skeleton` or `damage`, never for a library's own name. That is the whole point
// of this file: the mapping from a meaning to a glyph lives in one table, so re-drawing an icon, or
// changing icon library altogether, is an edit here and nowhere else. The preview had icons from
// three different naming schemes before this - a codicon class string, a pictogram, a bare word -
// and no way to see what the set as a whole looked like.
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
    IconAdjustments, IconAlertTriangle, IconArrowBackUp, IconAxisX, IconBone, IconBox,
    IconCamera, IconCheck, IconCircleX,
    IconChevronDown, IconChevronLeft, IconChevronRight, IconCircleOff, IconClipboardCopy,
    IconCloudDownload, IconCube, IconDeviceFloppy, IconExternalLink, IconFileCode,
    IconAsterisk, IconBook, IconFilterOff, IconHierarchy, IconLetterCase, IconListTree,
    IconRegex,
    IconEye, IconEyeClosed, IconEyeDotted, IconFlame, IconGridDots, IconHeartBroken,
    IconInfoCircle,
    IconLayoutGrid, IconMesh,
    IconPerspective, IconPhoto, IconPlayerPause, IconPlayerPlay, IconPlayerSkipBack,
    IconPlayerSkipForward, IconPlayerTrackNext, IconPlayerTrackPrev, IconPlus,
    IconPlayerStop, IconPolygon, IconRadar, IconRefresh, IconRepeat,
    IconCrosshair, IconTargetArrow, IconTool, IconHistory,
    IconRuler, IconSearch, IconSettings, IconShadow, IconSparkles, IconStack2,
    IconSun, IconSunHigh, IconSunLow, IconTexture, IconTrash, IconWind, IconWorld, IconX,
    type IconProps,
} from '@tabler/icons-react';
import { type ComponentType } from 'react';

import { codiconFor } from './iconSource';

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
    remove: IconTrash,
    copy: IconClipboardCopy,
    save: IconDeviceFloppy,
    close: IconX,
    check: IconCheck,
    download: IconCloudDownload,
    open: IconExternalLink,

    // Jumping to where a name is DECLARED, which is not the same as `open` - that one is an
    // external link and means "hand this to another application". Drawn as the EDITOR's own
    // go-to-file, because the story graph already draws it that way and one action must not look
    // like two. `iconSource` routes it; the entry stays here so the meaning is still listed.
    definition: IconFileCode,
    expanded: IconChevronDown,
    collapsed: IconChevronRight,

    bounds: IconBox,

    // ── the story graph's own tools ───────────────────────────────────────────
    // Taking every filter OFF at once. Not `reset`, which undoes what you did to the SUBJECT, and
    // not `search`, which is the control that put a filter on in the first place.
    clearFilter: IconFilterOff,

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
    // `iconSource` routes all three to the editor's own marks - see the note there.
    searchLiteral: IconLetterCase,
    searchWildcard: IconAsterisk,
    searchRegex: IconRegex,
} satisfies Record<string, ComponentType<IconProps>>;

export type IconName = keyof typeof ICONS;

export interface IconProps_ {
    name: IconName;

    /**
     * Pixel size. 16 by default, which is the size the rest of this extension's chrome is drawn at -
     * Tabler's own default is 24 and would tower over everything beside it.
     */
    size?: number;
}

export function Icon({ name, size = 16 }: IconProps_): React.JSX.Element {
    // An EDITOR action keeps the editor's own glyph - see `iconSource`. Only the domain icons are
    // Tabler's, because those are the ones codicons has not got.
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
    return <Glyph size={size} stroke={2} color="currentColor" aria-hidden="true" />;
}
