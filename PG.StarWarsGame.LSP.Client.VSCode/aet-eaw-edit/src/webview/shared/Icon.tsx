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
    IconCloudDownload, IconCube, IconDeviceFloppy, IconExternalLink,
    IconEye, IconEyeOff, IconFlame, IconGridDots, IconInfoCircle, IconLayoutGrid, IconMesh,
    IconPerspective, IconPhoto, IconPlayerPause, IconPlayerPlay, IconPlus,
    IconPlayerStop, IconPlayerTrackPrev, IconPolygon, IconRadar, IconRefresh, IconRepeat,
    IconRuler, IconSettings, IconShadow, IconSparkles, IconStack2,
    IconSun, IconSunHigh, IconSunLow, IconTexture, IconTrash, IconWind, IconWorld, IconX,
    type IconProps,
} from '@tabler/icons-react';
import { type ComponentType } from 'react';

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
    damage: IconCircleOff,

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

    // ── where you look from ───────────────────────────────────────────────────
    camera: IconCamera,
    axes: IconAxisX,
    capture: IconPhoto,
    frame: IconLayoutGrid,

    // ── plain actions ─────────────────────────────────────────────────────────
    visible: IconEye,
    hidden: IconEyeOff,
    details: IconInfoCircle,
    warning: IconAlertTriangle,
    error: IconCircleX,
    settings: IconSettings,
    tune: IconAdjustments,
    measure: IconRuler,
    play: IconPlayerPlay,
    pause: IconPlayerPause,
    stop: IconPlayerStop,
    rewind: IconPlayerTrackPrev,
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
    expanded: IconChevronDown,
    collapsed: IconChevronRight,

    // A PAIR, for paging through a table. Not the same names as the tree's twisty: those say
    // "folded or unfolded" and these say "back or forward", and a table that borrowed the twisty's
    // chevron would be claiming to expand something.
    previous: IconChevronLeft,
    next: IconChevronRight,
    bounds: IconBox,
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
    const Glyph = ICONS[name];

    // `currentColor` and aria-hidden are the two things that make an icon behave: it takes the
    // colour of whatever it sits in - including a disabled or a warning state - and it is invisible
    // to a screen reader, which reads the control's own label instead.
    return <Glyph size={size} stroke={2} color="currentColor" aria-hidden="true" />;
}
