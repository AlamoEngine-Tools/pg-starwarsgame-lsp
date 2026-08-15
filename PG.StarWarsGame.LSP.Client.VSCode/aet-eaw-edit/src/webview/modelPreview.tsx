// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The 3D preview panel: a canvas with the tool dock on the right, matching the layout the story
// graph, the localisation editors and the encyclopedia card already use.
//
// The dock's three levels have fixed MEANINGS, and a control goes in the one matching what it IS:
// the header reports state about the document (the subject and the severity tag), the content is
// this editor's library (the model's own stats and hardpoints), and the foot holds EVERY view
// control - camera angle, grid, which animation. Camera buttons floating over the canvas were the
// obvious place to write them and the wrong place to put them.
//
// The problems bar pops up from the bottom of the STAGE COLUMN, beside a full-height dock - never
// underneath it, and never as a list inside the dock, which is narrow enough to truncate every
// message. The localisation editor is the reference for this whole family of layout questions.
//
// React owns the chrome and nothing else. The renderer is a plain imperative object in
// preview/viewport.ts, held in a ref, because the two lifecycles do not line up: React re-renders
// when a checkbox moves, and a render loop must not be torn down for that.

import {
    Fragment, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState,
} from 'react';
import { createRoot } from 'react-dom/client';
import styled from 'styled-components';
import * as THREE from 'three';
// Static, not dynamic: the webview is bundled as an IIFE, which cannot be code-split, so an
// `await import()` here would not build.
import { DDSLoader } from 'three/examples/jsm/loaders/DDSLoader.js';
import { TGALoader } from 'three/examples/jsm/loaders/TGALoader.js';

import {
    AlamoParticleContent, GetModelDetailResult, GetModelGlbResult, GetModelTextureResult,
    GetParticleSystemResult,
    GetShaderSourceResult, PREVIEW_PARTICLE_GATE,
    GeometryTable, GetSubMeshGeometryResult, ModelDetail, PREVIEW_SCENE_KIND, PreviewParticle,
    PreviewProblem, PreviewRgba, PreviewScene, SubMeshGeometryPage,
} from '../protocol/modelPreview';
import { severityIconFor, worstSeverity } from './loc/validateState';
import { collectTextureNames } from './preview/materials';
import { geometryTable, inspectPanels, type MeshInspection } from './preview/inspector';
import { anchorFlyout } from './preview/flyoutAnchor';
import { groundRange, snapToZero } from './preview/groundRange';
import { InfoBadge } from './shared/InfoBadge';
import { PanelSection } from './shared/PanelSection';
import { Icon, type IconName } from './shared/Icon';
import { addressingFor } from './preview/textures';
import {
    DEFAULT_LIGHT_AZIMUTH_DEGREES, DEFAULT_LIGHT_ELEVATION_DEGREES,
} from './preview/lighting';
import {
    ancestorsOf, type BoneAttachment, type FlatBone, type LabelMode,
} from './preview/skeleton';
import {
    buildTree, defaultCollapsed, filterTree, selectionAfterClick, toggleTargets, visibleTreeRows, withDescendants,
    type TreeItem, type TreeKind,
} from './preview/previewTree';
import { PreviewViewport, type PresetView, type ViewportStats } from './preview/viewport';
import {
    dockBodyCss, dockChromeCss, dockHeaderCss, dockOverviewCss, problemsPanelCss, rightDockCss,
    rotarySwitchCss,
} from './shared/dockChrome';
import { ProblemsPanel } from './shared/ProblemsPanel';
import { parseHex, teamColour, toHex } from './preview/colour';
import { reviewFactionColour, type ColourFinding } from './preview/colourUsability';
import { translateEffect } from './preview/fx/effect';
import { lightBearing } from './preview/lightBearing';
import { modelCameraEntries } from './preview/modelCameras';
import { becauseText, setRow } from './preview/visibility';
import {
    luaFor, poseFromPreset, presetFromPose, type CameraPreset,
} from './preview/cameraPresets';
import { bindingFor, type CameraBinding, type PreviewSubject } from './preview/cameraBindings';
import { CAPTURE_SIZES, captureFileName, captureSize } from './preview/capture';
import { VIEWPORT_BACKGROUND } from './preview/viewport';
import { hasProgrammableShader, parseFxManifest, selectTechnique } from './preview/fx/fxParser';
import { materialStateFrom } from './preview/fx/renderState';
import { decalNames, effectPlaysNow, partHidden } from './preview/damage';
import type { DefinedLevels } from './preview/levels';
import {
    describeGate, groupState, particleGroups, type GroupState, type ParticleGroup,
} from './preview/particleScene';
import { defaultMode, otherModeChips, previewModes, type PreviewMode } from './preview/previewMode';
import { actionOf, groupAnimations } from './preview/animationNames';
import { ModeSelector, type ModeOption } from './shared/ModeSelector';
import { RotaryModeSwitch } from './shared/RotaryModeSwitch';
import { subjectStateFrom, type SubjectState } from './preview/subjectState';
import {
    colourFromHex, hexFromColour, viewerSettingsFrom, DEFAULT_LIGHTS, DEFAULT_VIEWER_SETTINGS,
    LIGHT_LABELS, LIGHT_NAMES, type DirectionalName, type DirectionalSetting, type LightRig,
    type BackgroundKind, type Wind,
} from './preview/viewerSettings';
import { RightDock } from './shared/RightDock';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

/** Messages the panel host sends in. */
type HostMessage =
    | { type: 'scene'; scene: PreviewScene }
    | {
        type: 'glb'; partId: string; attachToPartId?: string | null; attachBone?: string | null;
        result: GetModelGlbResult;
    }
    | { type: 'texture'; name: string; result: GetModelTextureResult }
    | { type: 'modelDetail'; modelReference: string; result: GetModelDetailResult }
    | { type: 'subMeshGeometry'; result: GetSubMeshGeometryResult }
    | { type: 'particleSystem'; name: string; result: GetParticleSystemResult }
    | { type: 'shader'; name: string; result: GetShaderSourceResult }
    // The room, and this subject's own state from earlier in the session. Both arrive before the
    // scene does, so nothing visibly snaps into place a frame after the model appears.
    | { type: 'viewerSettings'; settings: unknown; subject: unknown };

/** What each kind is called in the filter row. */
const KIND_LABELS: Record<TreeKind, string> = {
    bone: 'Bones',
    mesh: 'Meshes',
    particle: 'Effects',
};

/** What a group's switch means when it is on, off, or split between the two. */
const GROUP_STATE_TITLES: Record<GroupState, string> = {
    all: 'Every effect in this group is playing. Press to switch them all off.',
    none: 'Nothing in this group is playing. Press to switch them all on.',
    some: 'Some of this group is playing. Press to switch all of it on.',
};

/**
 * What a row IS, as a picture.
 *
 * A one-letter tag - B, M, P - was quicker to draw and needed the filter strip's legend to decode,
 * on every row of a hundred-row tree. A bone, a mesh and a flame need no legend.
 */
const KIND_ICONS: Record<TreeKind, IconName> = {
    bone: 'skeleton',
    mesh: 'mesh',
    particle: 'effects',
};

/**
 * The kinds, once, in the order the filter chips show them.
 *
 * Deliberately NOT a cast literal at each use. Both places that need this list used to write their
 * own `as TreeKind[]`, which is an assertion rather than a check - so renaming a kind left one of
 * them naming a kind that no longer existed, typechecked clean, and threw on the first render.
 */
const TREE_KINDS: readonly TreeKind[] = ['bone', 'mesh', 'particle'];

/**
 * Rows per page of a bulk-geometry table.
 *
 * Nobody reads a Star Destroyer sub-mesh's 3814 triangles in order; they look up a handful and
 * cross-reference them. A page this size answers that without the request ever being large, and the
 * server caps it anyway.
 */
const GEOMETRY_PAGE = 100;

/** The bulk tables, in the order the panel offers them. */
const GEOMETRY_TABLES: readonly { id: GeometryTable; label: string; title: string }[] = [
    { id: 'vertices', label: 'Vertices', title: 'Position, normal, UVs, tangents, colour and bone binding, per vertex' },
    { id: 'faces', label: 'Faces', title: 'Each triangle as the three vertex indices it draws' },
    {
        id: 'boneMapping', label: 'Bone mapping',
        title: 'Which model bone each local bone slot of this sub-mesh resolves to',
    },
];

/** Said once when a model loads with no mesh reaching the screen. */
const NOTHING_DRAWN = 'This model has no visible mesh at the current damage and detail level. '
    + 'Every sub-mesh is either tagged for another level, hidden in the file, or on a hidden bone.';

/** Remembered for the session so the dock does not snap back on every reload. */
let dockWidthMemo = 260;

const Shell = styled.div`
    ${dockChromeCss}
    ${rotarySwitchCss}
    ${rightDockCss}
    ${dockHeaderCss}
    ${dockBodyCss}
    ${dockOverviewCss}
    ${problemsPanelCss}

    height: 100%;
    display: flex;
    flex-direction: column;
    min-height: 0;

/* The header keeps its full height from the first frame, before the dial that fills it has
       anything to show.

       Without this it opened at the default height with only the two anchored buttons in it, then
       grew when the scene arrived and the dial appeared - so the top of the dock visibly drew
       itself in two steps. The story graph never did that because its dial is there from the start;
       78px is its measurement, and the same dial goes here. */
    .dock-header { min-height: 78px; }

    /* The header's centre slot holds the dial ALONE, so it reads as centred rather than as one item
       in a row - the graph's own layout, one anchored button either side. */
    /* The dial and the two hand-off buttons are one centred column, so the buttons sit under the
       thing they belong to rather than in a corner of the header. */
    .header-modes { display: inline-flex; flex-direction: column; align-items: center; gap: 2px; }
    .header-handoff { display: inline-flex; gap: 6px; }

    /* The model's identity, floating under the button that opens it. Anchored to the header, which
       is position: relative and clips nothing - inside the content it would be trapped by that
       panel's own scroll box. */
    .model-info {
        position: absolute;
        top: calc(100% + 8px);
        left: 6px;
        z-index: 20;
        min-width: 210px;
        max-width: min(280px, calc(100vw - 24px));
        padding: 8px 10px;
        text-align: left;
        border: 1px solid var(--vscode-panel-border, #444);
        border-radius: 4px;
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.45);
    }

    /* The caret, which is what makes it read as coming FROM the button rather than as a panel that
       happens to be nearby. Two squares: the border first, then the fill a pixel over it. */
    .model-info::before,
    .model-info::after {
        content: '';
        position: absolute;
        left: 12px;
        width: 8px; height: 8px;
        transform: rotate(45deg);
    }
    .model-info::before {
        top: -5px;
        border: 1px solid var(--vscode-panel-border, #444);
        background: var(--vscode-panel-border, #444);
    }
    .model-info::after {
        top: -4px;
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
    }

    .model-info-name {
        font-weight: 600;
        word-break: break-all;
        margin-bottom: 6px;
    }

    /* With the tree, not in the foot: the text box and the kind chips narrow the same list, and
       splitting them across two levels of the dock left the box acting on something out of sight. */
/* Room to breathe. Every control in this section had been tuned down to 1-2px of padding to
       win back vertical space for the tree - which stopped being the trade the moment the tree got
       the top of the panel to itself. Squeezed rows are harder to hit and harder to scan, and the
       saving was a dozen pixels. */
    .dock-section { gap: 8px; }
    .tree-filter { width: 100%; margin: 2px 0 6px; padding: 4px 6px; }

    /* What the selected row actually IS, read out of the file.

       A definition list rather than a table: the rows are label/value pairs of wildly different
       lengths - a shader name runs past the column a triangle count needs - and a table would set
       one column width for both. The label column is fixed and the value takes the rest, so the
       labels line up while a long value wraps under itself instead of widening the panel. */
    .inspect-group + .inspect-group { margin-top: 10px; }

    .inspect-title {
        font-size: 0.85em;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        opacity: 0.75;
        margin-bottom: 4px;
    }

    .inspect-rows {
        display: grid;
        grid-template-columns: minmax(0, 8.5em) minmax(0, 1fr);
        gap: 3px 8px;
        margin: 0;
    }

    .inspect-rows dt {
        opacity: 0.75;
        /* A label is a fixed vocabulary; truncating one loses nothing the reader cannot guess. */
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    /* Values are the author's own data, so they wrap rather than truncate - a texture name that
       ends in "...", or a vector missing its last component, is exactly the detail being looked up. */
    .inspect-rows dd {
        margin: 0;
        min-width: 0;
        overflow-wrap: anywhere;
    }

    /* Numbers and vectors line up digit for digit; names read as text. */
    .inspect-rows dd.number, .inspect-rows dd.vector, .inspect-rows dd.colour {
        font-variant-numeric: tabular-nums;
    }

    .inspect-rows dd.texture { font-family: var(--vscode-editor-font-family, monospace); }

    /* Four rows of four, laid out as the author authored them. Preformatted rather than line-break
       elements, so a narrow dock cannot re-wrap the rows into something that is no longer a matrix. */
    .inspect-rows dd.matrix {
        white-space: pre;
        overflow-x: auto;
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: 0.92em;
        line-height: 1.35;
    }

    /* The bulk tables. A vertex row is ten columns wide against a dock that is routinely 250px, so
       the table gets its own sideways scroller and the panel keeps its shape. */
    /* Wraps: three buttons ending in "Bone mapping" reach the edge of a dock that is routinely
       250px, and the last one was clipped against it. */
    .geometry-tables { margin: 4px 0 6px; flex-wrap: wrap; }

    .geometry-scroll {
        overflow-x: auto;
        max-height: 40vh;
        overflow-y: auto;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: 3px;
    }

    .geometry-table {
        border-collapse: collapse;
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: 0.88em;
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
    }

    .geometry-table th, .geometry-table td { padding: 2px 8px; text-align: right; }

    /* Names read left; every other column is a number. */
    .geometry-table td:last-child, .geometry-table th:last-child { text-align: left; }

    .geometry-table thead th {
        position: sticky;
        top: 0;
        background: var(--vscode-editorWidget-background, #202020);
        opacity: 0.95;
        font-weight: 600;
        text-align: right;
    }

    .geometry-table tbody tr:nth-child(even) {
        background: color-mix(in srgb, currentColor 4%, transparent);
    }

    .geometry-paging {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 8px;
        margin-top: 4px;
    }

    .geometry-range { opacity: 0.75; font-variant-numeric: tabular-nums; }

    .inspect-swatch {
        display: inline-block;
        width: 0.8em;
        height: 0.8em;
        margin-right: 5px;
        vertical-align: -1px;
        border-radius: 2px;
        /* Over a border rather than inside it: a border would eat into the colour being judged. */
        outline: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.5));
        outline-offset: -1px;
    }

/* Controls that describe the VIEW rather than the subject, docked to the stage's own corners.

       They belonged to nothing in the right-hand dock, which describes the MODEL, and between them
       they filled it: the camera, five overlay pills, a faction dropdown with a colour well under
       it and a renderer switch left no room for the one thing the foot is for, which is the
       playback bar. On the stage each cluster sits next to what it acts on, and the dock is free.

       One shared plate so the four corners read as the same kind of thing. Semi-transparent, since
       a solid panel over a viewport looks like a hole in it. */
    .stage-chrome {
        position: absolute;
        z-index: 2;
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 3px;
        border-radius: 6px;
        background: color-mix(in srgb,
            var(--vscode-editorWidget-background, #202020) 82%, transparent);
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        /* Never wider than the stage; a long faction roster wraps rather than running off it. */
        max-width: calc(100% - 16px);
        flex-wrap: wrap;
    }

/* Each EDGE is one row rather than two independently placed corners.

       Absolutely positioning a left cluster and a right cluster works until the stage is narrow -
       and this panel is routinely docked beside code. A Star Destroyer offers twelve factions, so
       its palette is fourteen swatches wide and would have run straight under the renderer switch.
       As a row they meet in the middle and each plate wraps inside itself instead.

       pointer-events is load-bearing: the row spans the whole stage, so left as it is it would
       swallow every camera drag that started anywhere near the top or bottom of the viewport. The
       row is transparent to the mouse and only the plates on it are not. */
    .stage-row {
        position: absolute;
        left: 8px;
        right: 8px;
        z-index: 2;
        display: flex;
        gap: 8px;
        pointer-events: none;
    }
    .stage-row > * { pointer-events: auto; }
    .stage-row .stage-chrome { position: static; }

    .stage-top { top: 8px; align-items: flex-start; justify-content: space-between; }

/* The palette is centred on the STAGE - the model is what it sits under - and the renderer
       switch is in the corner, and the two do not share a layout at all.

       They used to. Both a flex row and a grid make one of them squeeze the other, because with a
       twelve-faction palette there is genuinely not enough room for a centred strip AND a corner
       plate on a narrow panel: exact centring needs as much space to the left of the palette as the
       switch takes on the right, and below about 600px that space does not exist. Every attempt to
       share a row ended with "Default | Game" clipped to "Dif..|Ga...".

       So each is positioned on its own, and the narrow case is handled by moving the palette UP a
       row instead of squashing anything. The container query measures the stage rather than the
       window, which is the thing that actually varies - this panel is usually docked beside code. */
    .stage-bottom {
        bottom: 8px;
        display: block;
    }
    .stage-bottom .faction-palette {
        position: absolute;
        left: 50%;
        bottom: 0;
        transform: translateX(-50%);
        max-width: 100%;
    }

/* A COLUMN, which is the one arrangement that cannot squash.

       A wrapping flex row's intrinsic width is its largest item, not the sum - measured every way
       round, this box reported 201px around 276px of children, unchanged by width: max-content,
       min-width: max-content or an inline style, while a hard-coded width took effect. So as a row
       it either clipped "Default | Game" into "Dif..|Ga..." or rendered the button outside its own
       background, depending on how the wrapping was forced.

       Stacked, the intrinsic width IS the widest row and there is nothing left to get wrong. It
       also has exactly two children in every state now - the switch, and one line saying where the
       shader sources stand - so its height no longer changes underneath the palette. */
    .stage-bottom .shader-corner {
        position: absolute;
        right: 0;
        bottom: 0;
        flex-direction: column;
        align-items: stretch;
        gap: 5px;
        max-width: none;
    }

    /* A readout, not a control - so no hover, no pointer, and quieter than the switch above it. */
    .shader-state {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 4px;
        font-size: 0.9em;
        opacity: 0.7;
        color: var(--vscode-descriptionForeground, #999);
    }

    /* Below this there is no room for a centred palette AND a corner plate side by side, so they
       stack instead - and stacking with flex means nothing has to guess at the plate's height,
       which changes with whether the set-up button is showing. */
    @container (max-width: 700px) {
        .stage-bottom {
            display: flex;
            flex-direction: column-reverse;
            align-items: center;
            gap: 6px;
        }
        .stage-bottom .faction-palette,
        .stage-bottom .shader-corner {
            position: static;
            transform: none;
            bottom: auto;
            left: auto;
            right: auto;
        }
    }

/* The two side slots are EQUAL, which is the whole of what centres the middle one.

       They were not: the leading spacer was flex 1 1 0 while the end slot took its natural
       width, so the spacer grew to fill everything left over and shoved the palette rightwards into
       the renderer switch instead of balancing it. A centred element needs the same amount of
       nothing on both sides. */
    .stage-slot {
        flex: 1 1 0;
        min-width: 0;
        display: flex;
        align-items: flex-start;
        gap: 8px;
        flex-wrap: wrap;
    }
    .stage-slot-end { justify-content: flex-end; }
    .stage-bottom .stage-slot { align-items: flex-end; }

    /* Nothing in a side slot shrinks. Letting the renderer switch compress turned "Default | Game"
       into "Dif..|Ga..." - a control you can neither read nor aim at - while the palette in the
       middle wraps to another row perfectly well and stays centred doing it. */
    .stage-slot .stage-chrome { flex: 0 0 auto; }
    .stage-chrome .mode-selector { flex-shrink: 0; }
    .stage-chrome .mode-selector button { white-space: nowrap; }
    .stage-bottom .faction-palette { min-width: 0; justify-content: center; }

    /* Icon-only buttons are square rather than pill-shaped; the padding that makes room for a word
       beside the glyph just makes them lopsided without one. */
    .stage-chrome.icon-only .icon-btn {
        width: 22px;
        padding: 0;
        border-radius: 4px;
    }

    /* ONE height for everything on the stage, set here rather than left to each control's
       contents. A glyph and a word are different heights, and the plates ended up stepped: the
       scene globe stood taller than the preset words beside it and the camera button taller again.
       The box is the same and what sits in it is centred. */
    .stage-chrome .icon-btn, .stage-chrome .swatch, .stage-chrome .mode-selector button {
        height: 22px;
        font-size: 0.9em;
        padding: 0 8px;
        border-radius: 10px;
        /* A two-word label breaking in half makes the button taller than the plate it sits in,
           which then reports a height nothing else expects. */
        white-space: nowrap;
    }
    /* Sized to the box above rather than to the glyph, which is what made the globe the odd one
       out - a 16px icon in a row of 0.9em words. */
    /* An icon is an SVG here, sized by the component. The stage's own buttons carry nothing else,
       so this only has to stop a glyph stretching the 22px box it sits in. */
    .stage-chrome .icon-btn svg { flex-shrink: 0; }

    /* The colour IS the control, so the swatch carries no label - the faction's name is the title
       and reaches both a hover and a screen reader. Square and generous enough to judge a colour
       by, which a thin strip is not. */
    .faction-palette .swatch {
        width: 22px;
        height: 22px;
        padding: 0;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.5));
        border-radius: 4px;
        cursor: pointer;
        background-clip: padding-box;
    }
    /* The ring goes OUTSIDE the swatch. A border would eat into the colour being judged, and on a
       dark faction it is the only thing that says which one is applied. */
    .faction-palette .swatch.active {
        outline: 2px solid var(--vscode-focusBorder);
        outline-offset: 1px;
    }
    .faction-palette .swatch-plain {
        display: flex;
        align-items: center;
        justify-content: center;
        color: var(--vscode-descriptionForeground, #999);
        background: transparent;
    }
    /* Always last, whatever the roster does - see the markup. A divider says it is not a faction. */
    .faction-palette .swatch-custom {
        margin-left: 4px;
        border-left: 1px solid var(--vscode-panel-border, #444);
        padding-left: 6px;
        width: 28px;
        box-sizing: content-box;
    }

    /* One box, two corners. The scene hangs off the left of the stage and the camera off the
       right, mirroring the buttons that open them - so which side a panel is on says which button
       it belongs to before a word of it is read. */
    .stage-flyout {
        position: absolute;
        top: 40px;
        z-index: 3;
        /* Wide enough that a two-line checkbox label is the exception rather than the rule. At 270
           nearly every note and half the labels wrapped, which is what made the panel read as
           cramped even before the sections went in. */
        width: 304px;
        /* Never past the bottom of the stage; the body scrolls inside instead. */
        max-height: calc(100% - 56px);
        display: flex;
        flex-direction: column;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: 6px;
        background: var(--vscode-editorWidget-background, #202020);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.45);
    }
    .stage-flyout.on-left { left: 8px; }
    .stage-flyout.on-right { right: 8px; }

    .stage-flyout-head {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 4px 4px 4px 10px;
        border-bottom: 1px solid var(--vscode-panel-border);
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground, #999);
    }
    /* The first action floats to the far edge; the rest follow it. */
    .stage-flyout-head .icon-btn:first-of-type { margin-left: auto; }
    /* The gap here is BETWEEN sections and is deliberately larger than the one inside them
       (10px, see .panel-section-body): that difference is what makes a heading read as the start
       of a group rather than as one more row in a list. */
    .stage-flyout-body {
        display: flex;
        flex-direction: column;
        gap: 16px;
        padding: 12px 12px 14px;
        overflow-y: auto;
    }

    .body { flex: 1; display: flex; min-height: 0; }

    /* min-height: 0 is load-bearing - without it the canvas refuses to shrink below its content and
       shoves the problems bar off the bottom of the panel. */
    .stage-column { flex: 1; display: flex; flex-direction: column; min-width: 0; min-height: 0; }

    /* The canvas needs a positioned parent so the bone labels can sit over it - and the stage
       chrome asks this element how wide it is, which is not the same question as how wide the
       window is. */
    .viewport {
        position: relative;
        flex: 1;
        min-height: 0;
        display: flex;
        container-type: inline-size;
    }

    /* Absolutely positioned, NOT a flex child. The renderer sets the canvas width ATTRIBUTE, which
       becomes a flex item's min-content width, so the canvas could never shrink below its last size -
       and since it then measured wider than its parent, every resize made it bigger again. It reached
       1400px inside an 839px column. Taking it out of flow breaks the loop: it can only ever be the
       size of .viewport. */
    canvas {
        position: absolute;
        inset: 0;
        width: 100%;
        height: 100%;
        display: block;
        /* The scene paints its own ground. Without this the canvas borrows the panel background and
           an unlit hull on a dark theme is invisible. */
        background: #1b1d21;
    }

    /* Labels are DOM, not sprites: they stay crisp at any zoom, theme themselves, and can be read by
       a screen reader. pointer-events off, or they would swallow the drags that orbit the camera. */
    .bone-labels { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
    .bone-label {
        position: absolute;
        top: 0;
        left: 0;
        margin: -9px 0 0 8px;
        padding: 0 3px;
        font-size: 10px;
        white-space: nowrap;
        color: var(--vscode-editor-foreground);
        background: color-mix(in srgb, var(--vscode-editor-background) 70%, transparent);
        border-radius: 2px;
    }
    .bone-label.selected {
        color: var(--vscode-editor-background);
        background: var(--vscode-focusBorder);
    }

    /* A deep skeleton indents far enough that nowrap rows widened the WHOLE panel: the dock's
       min-content width won over its set width, .body grew past the window, and the canvas stretched
       to 1400px inside an 1100px viewport. min-width: 0 lets the dock hold its size; the tree scrolls
       inside it instead. */
    .right-dock { min-width: 0; }
    .dock-content { min-width: 0; }

    /* Toggle buttons in the icon-btn / .active language the localisation search and the story
       graph's lane switches already use, rather than a checkbox row of their own invention.
       No backticks in here: this is a styled-components template literal. */
    .kind-filter, .selection-actions { display: flex; gap: 5px; padding: 4px 0 6px; flex-wrap: wrap; }
    /* The scene-overlay strip, in the same pill language. Five stacked checkbox rows said nothing
       about the model between them; a wrapping strip says the same thing in one row and a bit. */
    .view-toggles { display: flex; gap: 4px; flex-wrap: wrap; }
    .kind-filter .icon-btn, .selection-actions .icon-btn, .view-toggles .icon-btn {
        font-size: 0.9em;
        padding: 3px 10px;
        border-radius: 10px;
    }

    /* A small picture, so a row's kind reads at a glance without widening every line in a deep
       tree. Sized as a BOX rather than by its contents - an icon that fails to load must still
       leave the row's columns lined up. */
    .row-kind {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
        width: 15px;
        height: 15px;
        opacity: 0.85;
    }
    .row-kind.kind-bone { color: var(--vscode-charts-blue); }
    .row-kind.kind-mesh { color: var(--vscode-charts-green); }
    .row-kind.kind-emitter { color: var(--vscode-charts-orange); }

    .bone-row .row-visible { margin: 0; flex-shrink: 0; }

/* Scrolls inside itself rather than growing without limit.

       The controls belong under the list, and a Star Destroyer's fifty rows would otherwise put the
       filter fifty rows down the panel - "below" has to still mean "reachable". The list keeps the
       top of the section, takes as much room as it needs up to this, and hands the rest back. */
    /* Scrolls DOWN only. Rows are nowrap so a name never breaks in half, which used to mean a
       deep tree with long hardpoint ids pushed the list sideways and put a horizontal scrollbar
       under it - and a tree you have to scroll sideways to read is a tree you cannot scan. The
       name gives up its tail instead; the whole of it is on the row's tooltip and in its details. */
    .bone-tree {
        list-style: none;
        margin: 0 0 2px;
        padding: 2px 0;
        overflow-y: auto;
        overflow-x: hidden;
        max-height: 48vh;
    }
    .bone-row {
        display: flex;
        align-items: center;
        gap: 5px;
        padding: 3px 6px;
        cursor: pointer;
        white-space: nowrap;
        /* Load-bearing: without it a flex item refuses to shrink below its content, so the row
           stays as wide as its longest name and the list scrolls sideways anyway. */
        min-width: 0;
    }
    .bone-row .bone-name {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    .bone-row:hover { background: var(--vscode-list-hoverBackground); }
    .bone-row.selected { background: var(--vscode-list-activeSelectionBackground); }
    .bone-row.hidden-bone .bone-name { opacity: 0.55; font-style: italic; }
    /* A real box, not one the glyph gives it. Sized by its icon alone it is 12px wide and NO
       pixels tall, so the one control that folds a subtree is a hairline to aim at - and nothing
       at all if the icon font has not loaded. Same fault as the row's details button had. */
    .bone-row .twisty {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 14px;
        height: 16px;
        flex-shrink: 0;
        opacity: 0.7;
    }
    .bone-row .twisty:hover { opacity: 1; }
    .bone-row .twisty.leaf { visibility: hidden; }
    .bone-row .bone-attach { color: var(--vscode-descriptionForeground); font-size: 0.9em; }

    /* Pushed to the far edge so the buttons line up in a column whatever the names do, and dim
       until the row is under the pointer - a deep tree has to read as a list of names, not as a
       column of controls. Never hidden outright: a button that only exists on hover is one nobody
       finds. */
    .bone-row .row-details {
        margin-left: auto;
        flex-shrink: 0;
        opacity: 0.25;
        /* An explicit box rather than one the glyph gives it. A button sized by its icon has no
           size at all if the icon font has not loaded, which makes it unclickable rather than
           merely unlabelled. */
        min-width: 18px;
        height: 18px;
        padding: 0 3px;
    }
    .bone-row:hover .row-details,
    .bone-row .row-details:focus-visible,
    .bone-row .row-details.active { opacity: 1; }

    /* Pinned against the WINDOW, because the row it belongs to lives inside a scroller that would
       clip it. placeDetails supplies the corner; everything here is the box itself. */
    .details-flyout {
        position: fixed;
        z-index: 6;
        width: 320px;
        max-height: 62vh;
        display: flex;
        flex-direction: column;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: 6px;
        background: var(--vscode-editorWidget-background, #202020);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.45);
    }
    .details-head {
        display: flex;
        align-items: baseline;
        gap: 6px;
        padding: 4px 4px 4px 10px;
        border-bottom: 1px solid var(--vscode-panel-border);
        font-size: 11px;
    }
    .details-title {
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    /* Takes what is left and gives it back: the title is what identifies the row, so it is the
       half that survives a narrow box. */
    .details-subtitle {
        flex: 1;
        min-width: 0;
        color: var(--vscode-descriptionForeground, #999);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }
    .details-head .icon-btn { margin-left: auto; align-self: center; }
    .details-body {
        display: flex;
        flex-direction: column;
        gap: 8px;
        padding: 8px 10px 10px;
        overflow-y: auto;
    }

    .preview-problems {
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        min-height: 0;
    }

    /* A readout, not a menu: no hover state and no pointer, because nothing here is clickable yet.
       A row that looks pressable is a promise of interaction. */
    .part-list { list-style: none; margin: 0; padding: 0; }
    .part-list li {
        display: flex;
        flex-direction: column;
        gap: 1px;
        padding: 3px 6px;
        min-width: 0;
        cursor: default;
    }
    .part-list li .field-label { min-width: 0; align-items: flex-start; }
    .part-list li .field-label input[type=checkbox] { margin-top: 2px; flex-shrink: 0; }
    /* The name WRAPS; only the detail under it is clipped. Alamo hardpoint ids share a long prefix
       and differ in their last two characters, so one clipped line gave ten rows all reading
       "HP_Star_Destroyer_Weapon_..." - which is worse than a taller row, because it is the same
       row ten times. Two lines hold every id the shipped models use. */
    .part-list .part-name {
        overflow-wrap: anywhere;
        line-height: 1.25;
        display: -webkit-box;
        -webkit-line-clamp: 2;
        -webkit-box-orient: vertical;
        overflow: hidden;
    }
    .part-list li .detail {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    /* Indented to sit under the name rather than under its checkbox. */
    .part-list li .detail {
        padding-left: 21px;
        font-size: 0.9em;
        color: var(--vscode-descriptionForeground);
    }

    .stat-row {
        display: flex;
        justify-content: space-between;
        gap: 12px;
        padding: 1px 6px;
        font-variant-numeric: tabular-nums;
    }
    .stat-row .value { color: var(--vscode-descriptionForeground); }

    .view-row { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; }

    /* The clip library. Wide rows that look pressable, because they ARE the play control - there is
       no separate play button and no dropdown to open first. */
    .player { display: flex; flex-direction: column; gap: 6px; }
    .player-row { display: flex; align-items: center; gap: 1px; flex-wrap: wrap; }
    /* Six controls and a clock have to share one dock-wide row. The toolbar default leaves them
       ~30px too wide at the dock's opening size, which wrapped the clock onto a line of its own. */
    .player-row .icon-btn { min-width: 20px; padding: 4px 4px; }
    .player-row .player-time {
        margin-left: auto;
        padding-right: 4px;
        font-variant-numeric: tabular-nums;
        font-size: 0.9em;
        color: var(--vscode-descriptionForeground);
    }
    /* The travel direction is explicit on every slider in this panel. A range input takes it from
       the inherited writing direction, so anything upstream that flips that - a host laying the
       webview out right-to-left, a stray rule - silently runs the playhead backwards. These read a
       timeline and a magnitude; both only make sense left to right.
       (No backticks in here: this is a styled-components template literal.) */
    .player-scrub, .player input[type=range], .field input[type=range] { direction: ltr; }
    .player-scrub { width: 100%; }
    .player-section { margin-bottom: 10px; }

    .skeleton-field { padding: 4px 0 0; }

    .anim-list { display: flex; flex-direction: column; gap: 2px; }
    .anim-group {
        margin-top: 6px;
        padding: 2px 6px 0;
        font-size: 0.85em;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground);
    }
    .anim-group:first-child { margin-top: 0; }
    .anim-row {
        display: flex;
        align-items: center;
        gap: 7px;
        width: 100%;
        padding: 5px 8px;
        border: 1px solid transparent;
        border-radius: 4px;
        background: var(--vscode-editorWidget-background, rgba(128, 128, 128, 0.08));
        color: var(--vscode-foreground, #ccc);
        font-family: var(--vscode-font-family, sans-serif);
        font-size: var(--vscode-font-size, 13px);
        text-align: left;
        cursor: pointer;
    }
    .anim-row:hover {
        background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.2));
        border-color: var(--vscode-focusBorder);
    }
    .anim-row.active {
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
    }
    .anim-row svg { flex-shrink: 0; opacity: 0.8; }
    .anim-row .anim-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
`;

/** What a faction with no colour of its own is drawn as, and the custom well's starting point. */
const NEUTRAL_COLOUR: PreviewRgba = { r: 184, g: 184, b: 184, a: 255 };

const PRESETS: { view: PresetView; label: string; title: string }[] = [
    { view: 'threeQuarter', label: '3/4', title: 'Three-quarter view' },
    { view: 'front', label: 'Front', title: 'Front view' },
    { view: 'side', label: 'Side', title: 'Side view' },
    { view: 'top', label: 'Top', title: 'Top-down view' },
];

/**
 * One page of a sub-mesh's bulk geometry.
 *
 * Its own component because the paging footer has to know what it is a page OF - the total differs
 * per table, and the bone mapping is not paged at all - and threading that through the panel body
 * would have put three ternaries inside the JSX.
 */
function GeometryPage(
    { page, onPage }: {
        page: SubMeshGeometryPage;
        onPage: (table: GeometryTable, offset: number) => void;
    },
): React.JSX.Element {
    const view = geometryTable(page);
    const last = page.offset + view.rows.length;

    // An empty table that is empty for a REASON says so instead of drawing a bare grid.
    if (view.rows.length === 0 && view.note !== undefined) {
        return <div className="field-note">{view.note}</div>;
    }

    // The bone mapping is short by nature - the longest skin table in the corpus is a fraction of
    // one page - so it comes whole and its own length IS the total.
    const total = page.table === 'faces'
        ? page.totalFaces
        : page.table === 'vertices' ? page.totalVertices : view.rows.length;

    return (
        <>
            {/* Its own scroller: a vertex row is ten columns wide and the dock is narrow, so the
                table scrolls sideways inside itself rather than pushing the panel out of shape. */}
            <div className="geometry-scroll">
                <table className="geometry-table">
                    <thead>
                        <tr>{view.columns.map(column => <th key={column}>{column}</th>)}</tr>
                    </thead>
                    <tbody>
                        {view.rows.map((row, at) => (
                            <tr key={at}>{row.map((cell, col) => <td key={col}>{cell}</td>)}</tr>
                        ))}
                    </tbody>
                </table>
            </div>

            {/* Where this page sits in the whole. Without it a table starting at row 300 reads as a
                sub-mesh with 300 fewer vertices than it has. */}
            <div className="geometry-paging">
                <button
                    type="button"
                    className="icon-btn"
                    title="The previous page"
                    disabled={page.offset === 0}
                    onClick={() => onPage(
                        page.table as GeometryTable, Math.max(0, page.offset - GEOMETRY_PAGE))}
                >
                    <Icon name="previous" />
                </button>

                <span className="geometry-range">
                    {view.rows.length === 0
                        ? 'no rows'
                        : `${page.offset + 1}-${last} of ${total.toLocaleString()}`}
                </span>

                <button
                    type="button"
                    className="icon-btn"
                    title="The next page"
                    disabled={last >= total}
                    onClick={() => onPage(page.table as GeometryTable, page.offset + GEOMETRY_PAGE)}
                >
                    <Icon name="next" />
                </button>
            </div>
        </>
    );
}

function ModelPreview(): React.JSX.Element {
    const canvasRef = useRef<HTMLCanvasElement | null>(null);
    const labelsRef = useRef<HTMLDivElement | null>(null);
    const viewportRef = useRef<PreviewViewport | null>(null);

    const [scene, setScene] = useState<PreviewScene | null>(null);
    const [stats, setStats] = useState<ViewportStats | null>(null);
    const [problems, setProblems] = useState<PreviewProblem[]>([]);
    const [problemsOpen, setProblemsOpen] = useState(false);
    const [grid, setGrid] = useState(true);
    // Off by default: a floor is the right backdrop for a walker and the wrong one for a
    // capital ship, and most of what opens here is in space.
    const [floor, setFloor] = useState(false);
    /** Where the ground sits. Not every model is authored standing on the origin. */
    const [floorLevel, setFloorLevel] = useState(DEFAULT_VIEWER_SETTINGS.floorLevel);
    const [wireframe, setWireframe] = useState(DEFAULT_VIEWER_SETTINGS.wireframe);
    const [heatOn, setHeatOn] = useState(DEFAULT_VIEWER_SETTINGS.heat);
    const [heatDebug, setHeatDebug] = useState(DEFAULT_VIEWER_SETTINGS.heatDebug);
    const [bloom, setBloom] = useState(DEFAULT_VIEWER_SETTINGS.bloom);
    const [lights, setLights] = useState<LightRig>(DEFAULT_LIGHTS);

    /**
     * The reader's own saved shots.
     *
     * Tier 1, with the rest of the room: a preset exists to frame a ROSTER the same way, so it has
     * to outlive the subject it was built on.
     */
    const [cameraPresets, setCameraPresets] = useState<CameraPreset[]>([]);

    /** Which saved shot a kind of subject opens with. Tier 1 with the presets they point at. */
    const [cameraBindings, setCameraBindings] = useState<CameraBinding[]>([]);

    /** How big a capture is written, and whether it carries the room with it. */
    const [captureSizePx, setCaptureSizePx] = useState(128);
    const [captureTransparent, setCaptureTransparent] = useState(true);
    const [captureScenery, setCaptureScenery] = useState(false);
    const [wind, setWind] = useState<Wind>(DEFAULT_VIEWER_SETTINGS.wind);
    const [background, setBackground] = useState<BackgroundKind>(
        DEFAULT_VIEWER_SETTINGS.background);

    /**
     * The camera preset in force.
     *
     * Held rather than fired and forgotten: the presets are a one-of-N choice, and a segmented
     * control that never shows which one you picked is just four buttons in a box.
     *
     * Null once the camera is dragged. A preset is a place to jump to, not a mode the camera stays
     * in, so keeping "Front" lit while you look at the model from underneath would be a lie.
     */
    const [cameraView, setCameraView] = useState<string | null>('threeQuarter');

    /**
     * Which lens the dock is showing.
     *
     * A SOFT switch: it changes which tools are on screen and nothing else, so an animation keeps
     * playing while the reader hops into Model mode to hide a mesh.
     */
    const [mode, setMode] = useState<PreviewMode>('model');

    /** Which light the four dials below are editing. The rig has three and they share one row. */
    const [editing, setEditing] = useState<DirectionalName>('sun');

    // Two angles do not say where a light is. This puts the same two numbers in the terms the
    // stage's own Front / Side / Top presets already gave the reader, and says out loud when the
    // light has dropped under the ground plane - which the slider marks nowhere.
    const bearing = lightBearing(lights[editing].azimuth, lights[editing].elevation);

    // The subject's own cameras. Derived, not stored: they belong to the model, so a new scene
    // replaces them and nothing has to be cleared.
    const authorCameras = useMemo(
        () => modelCameraEntries(scene?.cameras ?? []), [scene?.cameras]);

    /** What the subject IS, as far as a binding rule can see. */
    const subjectFacts = useMemo<PreviewSubject>(() => ({
        // Only a game object has an id worth matching. A bare model is previewed by filename, which
        // is not a thing anyone binds a roster shot to.
        objectId: scene?.kind === 'Object' ? scene.subject : null,
        objectType: scene?.objectType ?? null,
        categories: scene?.categories ?? [],
    }), [scene?.kind, scene?.subject, scene?.objectType, scene?.categories]);

    /** The rule that decides this subject's opening shot, if any. */
    const boundRule = useMemo(
        () => bindingFor(subjectFacts, cameraBindings), [subjectFacts, cameraBindings]);

    // Kept in refs as well as in state: `applyBoundCamera` runs from the message handler, which is
    // set up once, and a closure over the state would see whatever the panel opened with.
    const subjectFactsRef = useRef(subjectFacts);
    subjectFactsRef.current = subjectFacts;

    const bindingsRef = useRef(cameraBindings);
    bindingsRef.current = cameraBindings;

    const presetsRef = useRef(cameraPresets);
    presetsRef.current = cameraPresets;

    /**
     * What the subject on screen could be bound by, most specific first.
     *
     * Empty for a bare model, which has no game object at all - and the panel says so rather than
     * offering three buttons that would each bind nothing.
     */
    const bindTargets = useMemo(() => {
        const targets: {
            kind: CameraBinding['kind']; value: string; label: string; what: string;
        }[] = [];

        if (subjectFacts.objectId !== null) {
            targets.push({
                kind: 'object', value: subjectFacts.objectId,
                label: 'Bind this object', what: `'${subjectFacts.objectId}'`,
            });
        }

        if (subjectFacts.objectType !== null) {
            targets.push({
                kind: 'type', value: subjectFacts.objectType,
                label: `Bind type ${subjectFacts.objectType}`, what: subjectFacts.objectType,
            });
        }

        // The FIRST category only. A subject carries several - `Vehicle | AntiInfantry` - and a row
        // of buttons per category would swamp the panel; the rest can be reached by binding from a
        // subject whose first category is the one wanted.
        const category = subjectFacts.categories[0];
        if (category !== undefined) {
            targets.push({
                kind: 'category', value: category,
                label: `Bind category ${category}`, what: category,
            });
        }

        return targets;
    }, [subjectFacts]);

    /**
     * Frames the subject with its bound preset, if it has one.
     *
     * Read through refs rather than closed over: this runs from the message handler, which is set
     * up once and would otherwise capture the bindings as they were when the panel opened.
     */
    const applyBoundCamera = useCallback((viewport: PreviewViewport): boolean => {
        const rule = bindingFor(subjectFactsRef.current, bindingsRef.current);
        const preset = rule === null
            ? undefined
            : presetsRef.current.find(p => p.id === rule.presetId);

        if (preset === undefined) {
            return false;
        }

        const pose = poseFromPreset(preset, viewport.subjectSphere);
        viewport.applyCameraPose(pose.position, pose.target);
        setCameraView(null);

        return true;
    }, []);

    /**
     * Puts the whole room back to its defaults.
     *
     * Every Tier 1 value at once rather than one control at a time: the settings that need
     * recovering are the ones a reader has already lost track of, and hunting fourteen of them down
     * individually is the problem rather than the fix. The subject's own state is left alone - it
     * is not part of the room, and resetting it would undo a damage state the reader set on purpose.
     */
    const resetRoom = (): void => {
        const room = DEFAULT_VIEWER_SETTINGS;

        setGrid(room.grid);
        setFloor(room.floor);
        setFloorLevel(room.floorLevel);
        setWireframe(room.wireframe);
        setHeatOn(room.heat);
        setHeatDebug(room.heatDebug);
        setBloom(room.bloom);
        setBackground(room.background);
        setDrawDistance(room.drawDistance);
        setLights(room.lights);
        setCameraPresets(room.cameraPresets);
        setCameraBindings(room.cameraBindings);
        setFolded(new Set(room.collapsedPanels));
        setWind(room.wind);
        setSkeletonOn(room.skeleton);
        setLabelMode(room.boneLabels as LabelMode);
        setFireArcs(room.fireArcs);
        setTranslatedOn(room.effectShaders);
        setParticlesOn(room.particles);
        setParticleSpeed(room.particleSpeed);
        setCustomColour(room.customColour);
    };

    /** Changes one directional, leaving the other two and the global terms alone. */
    const setLight = (name: DirectionalName, change: Partial<DirectionalSetting>): void =>
        setLights(current => ({ ...current, [name]: { ...current[name], ...change } }));

    // How much further than the subject the camera can see. Fitted to the model AND its effects, so
    // 1 already clears a flamethrower's throw; the multiplier is for the trails no fit anticipates.
    const [drawDistance, setDrawDistance] = useState(DEFAULT_VIEWER_SETTINGS.drawDistance);

    /** The faction the reader last chose, by name, until this subject's own list arrives. */
    const storedFactionRef = useRef<string | null>(null);

    /** Nothing is written back until the stored room has been applied, or we would save defaults. */
    const restoredRef = useRef(false);

    /**
     * This subject's state from earlier in the session, until the scene arrives to apply it to.
     *
     * Held rather than applied on arrival: it lands before the scene, and the scene handler resets
     * every one of these fields on its way in.
     */
    const storedSubjectRef = useRef<SubjectState | null>(null);
    const [animation, setAnimation] = useState<string | null>(null);

    /**
     * The transport.
     *
     * Speed is a multiple of the clip's OWN rate, which the exporter bakes from the `.ala` header -
     * 30 fps for 1270 of the 1363 shipped animations, and 1, 5, 10 or 15 for the rest. So 1.0 is
     * already what the game does, and there is no default frame rate for the panel to impose.
     *
     * Looping defaults on because most of what a modder checks is a cycle - idles, walks, turns -
     * and a walk that plays once and stops cannot be judged at all.
     */
    const [animationPaused, setAnimationPaused] = useState(false);
    const [animationSpeed, setAnimationSpeed] = useState(1);
    const [animationLoop, setAnimationLoop] = useState(true);

    /** Where the playhead is. Polled from the mixer, which is the only thing that knows. */
    const [playhead, setPlayhead] = useState({ time: 0, duration: 0, running: false });

    /**
     * True while the scrubber is held.
     *
     * The poll below would otherwise fight the drag: it writes the mixer's time into the slider
     * sixty times a second, so a slider the reader is dragging snapped back under their finger.
     */
    const scrubbingRef = useRef(false);

    const [bones, setBones] = useState<FlatBone[]>([]);
    const [attachments, setAttachments] = useState<Map<number, BoneAttachment[]>>(new Map());
    const [skeletonOn, setSkeletonOn] = useState(false);
    const [labelMode, setLabelMode] = useState<LabelMode>('selected');
    const [selectedBone, setSelectedBone] = useState<number | null>(null);
    const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());

    /** Whether the room's controls are unfolded. Closed on open: they are not why you came here. */
    const [worldOpen, setWorldOpen] = useState(false);

    /**
     * The flyout sections folded shut, by id.
     *
     * Held as the FOLDED ones rather than the open ones, so a section added later arrives open - a
     * control that appears already hidden is one nobody discovers. Tier 1: which groups you keep
     * shut describes how you work, not the model in front of you.
     */
    const [folded, setFolded] = useState<ReadonlySet<string>>(new Set());

    /**
     * Drops the selection, and with it the box drawn round it in the viewport.
     *
     * `selectedBone` goes too: it is what the bone labels highlight, and a label left lit for a row
     * nothing is pointing at is the same defect in a different place.
     */
    const clearSelection = useCallback(() => {
        setSelected(new Set());
        setAnchor(null);
        setSelectedBone(null);
    }, []);

    const toggleSection = useCallback((id: string) => {
        setFolded(current => {
            const next = new Set(current);

            if (!next.delete(id)) {
                next.add(id);
            }

            return next;
        });
    }, []);

    /**
     * Whether the camera's controls are unfolded.
     *
     * Its own panel rather than a section of the scene's: where you look FROM is not part of the
     * room you are looking at, and the two were mixed in one list that had grown long enough to
     * scroll past whichever half you wanted.
     */
    const [cameraOpen, setCameraOpen] = useState(false);
    const cameraRef = useRef<HTMLDivElement | null>(null);

    /** Whether the model's identity flyout is showing. Closed on open - it is a check, not a tool. */
    const [infoOpen, setInfoOpen] = useState(false);
    const infoRef = useRef<HTMLDivElement | null>(null);

    /**
     * The tree row whose details are open, if any.
     *
     * Deliberately NOT the selection. Details used to appear in the dock the moment anything was
     * picked, which made every click in the tree a page of text nobody asked for - and selecting is
     * how you aim the viewport, not how you ask what something is made of. It takes its own button
     * on the row now, and one row at a time.
     */
    const [detailsRow, setDetailsRow] = useState<string | null>(null);

    /**
     * Whether the ground slider is being DRAGGED rather than stepped with the keyboard.
     *
     * The detent at zero exists because a pointer cannot reliably hit one position in four hundred.
     * A keyboard can: an arrow key moves exactly one step, so a reader who steps to the first mark
     * above the ground meant it and must get it. The flag is what tells the two apart - the change
     * event itself does not say where it came from.
     */
    const groundDragging = useRef(false);

    /** Where that flyout is pinned, recomputed whenever the row it belongs to could have moved. */
    const [detailsAt, setDetailsAt] = useState<{ left: number; top: number } | null>(null);
    const detailsRef = useRef<HTMLDivElement | null>(null);

    /**
     * The scene flyout's box.
     *
     * Escape closes it, but a click OUTSIDE deliberately does not: these are settings you adjust
     * while watching what they do to the model, and dismissing the panel the moment you touch the
     * viewport would make the two impossible to use together. It has its own close button instead.
     */
    const worldRef = useRef<HTMLDivElement | null>(null);

    // Both stage flyouts, one rule. Escape closes both; an outside click deliberately closes
    // neither - see above. They can be open TOGETHER on purpose: setting up a shot means moving the
    // light and the camera against each other, and a panel that shuts the other one turns that into
    // a trip back to the button every time. Opposite corners, so they never overlap.
    useEffect(() => {
        if (!worldOpen && !cameraOpen) {
            return;
        }

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === 'Escape') {
                setWorldOpen(false);
                setCameraOpen(false);
            }
        };

        document.addEventListener('keydown', onKey);

        return () => document.removeEventListener('keydown', onKey);
    }, [worldOpen, cameraOpen]);

    /**
     * Closes the flyout on Escape or a click anywhere else.
     *
     * A popover that can only be dismissed by finding the button again is a popover in the way. The
     * button's own click is excluded, or its toggle would fire twice and immediately reopen it.
     */
    useEffect(() => {
        if (!infoOpen) {
            return;
        }

        const dismiss = (event: Event): void => {
            const target = event.target as Node | null;
            const onButton = target instanceof Element
                && target.closest('.header-left') !== null;

            if (!onButton && infoRef.current?.contains(target ?? null) !== true) {
                setInfoOpen(false);
            }
        };

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === 'Escape') {
                setInfoOpen(false);
            }
        };

        document.addEventListener('pointerdown', dismiss);
        document.addEventListener('keydown', onKey);

        return () => {
            document.removeEventListener('pointerdown', dismiss);
            document.removeEventListener('keydown', onKey);
        };
    }, [infoOpen]);

    /** So the opening fold happens once per subject rather than every time the tree is rebuilt. */
    const foldedRef = useRef(false);
    const [boneFilter, setBoneFilter] = useState('');

    /**
     * Particle systems by name, and the attachments waiting on each.
     *
     * Refs, not state: they are mutated from the message handler as parts and systems arrive in
     * whatever order the server answers, and re-rendering on each arrival would buy nothing. Fetched
     * once per NAME and instantiated once per entry - a Star Destroyer has twenty proxies calling for
     * p_hp_imperial_damage, and fetching that twenty times is twenty round trips for one answer.
     */
    const systemsRef = useRef(new Map<string, AlamoParticleContent>());
    const pendingRef = useRef<PreviewParticle[]>([]);
    const requestedSystemsRef = useRef(new Set<string>());

    /** Shaders already asked for, so a ten-part unit does not request its shared effect ten times. */
    const requestedShadersRef = useRef(new Set<string>());

    /**
     * Every `.fx` and `.fxh` fetched, by lower-cased file name. Null means "asked for, not there".
     *
     * Headers arrive over the same channel as the effects that include them and in no fixed order,
     * so translation is retried against this whole map each time anything new lands.
     */
    const shaderSourcesRef = useRef(new Map<string, string | null>());

    /** Effects already handed to the viewport, so a retry does not rebuild materials for nothing. */
    const translatedShadersRef = useRef(new Set<string>());

    /**
     * Whether any `.fx` source was reachable at all.
     *
     * The difference between "nothing translated" and "there was nothing to translate". The game
     * ships COMPILED `.fxo`, not `.fx`, so without a shader directory configured every effect comes
     * back null and the count is 0 for a reason the reader can actually act on.
     */
    const [anyShaderSource, setAnyShaderSource] = useState(false);

    /** Death explosions asked for but not yet delivered, to play the moment they arrive. */
    const explosionsRef = useRef<{ id: string; system: string }[]>([]);

    /**
     * Draw with the effects' own translated shaders, rather than the archetype reading of them.
     *
     * On by default: that is the point of the feature, and anything that cannot translate - a
     * mod-authored effect, or one of the eleven that are render state only - falls back to the
     * archetype on its own. The switch stays so the two can be compared.
     */
    const [translatedOn, setTranslatedOn] = useState(true);

    /** How many sub-meshes that actually reaches, out of how many are loaded. */
    const [translatedCount, setTranslatedCount] = useState({ translated: 0, total: 0 });

    /** Everything the model is made of, for the one tree. */
    const [treeItems, setTreeItems] = useState<TreeItem[]>([]);

    /**
     * What the server knows about the hull that the glTF cannot say: bone transforms in the file's
     * own axes, the bounding boxes the file stores, and the proxies - which the exporter writes into
     * the glTF not at all.
     */
    const [modelDetail, setModelDetail] = useState<ModelDetail | null>(null);

    /** The model the outstanding detail request was for, so a crossed reply can be spotted. */
    const detailWantedRef = useRef<string | null>(null);

    /**
     * The open bulk-geometry table, if any.
     *
     * Closed by default and per selection: this is the one genuinely large thing the preview can
     * ask for - a Star Destroyer sub-mesh is 3814 triangles - so it is fetched only when someone
     * says they want it, and forgotten when they look at something else.
     */
    const [geometry, setGeometry] = useState<SubMeshGeometryPage | null>(null);
    const [geometryError, setGeometryError] = useState<string | null>(null);

    /** Which kinds the tree shows. All three to begin with. */
    const [kinds, setKinds] = useState<ReadonlySet<TreeKind>>(
        new Set<TreeKind>(TREE_KINDS));

    /** Multi-select, so a run of meshes can be switched together. */
    const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
    const [anchor, setAnchor] = useState<string | null>(null);


    const [particlesOn, setParticlesOn] = useState(true);
    const [particlesPaused, setParticlesPaused] = useState(false);
    const [particleSpeed, setParticleSpeed] = useState(1);
    /** Emitter names in file order. Names repeat, so the index is the identity, not the name. */
    const [emitters, setEmitters] = useState<string[]>([]);
    const [hiddenEmitters, setHiddenEmitters] = useState<ReadonlySet<number>>(new Set());

    /** Hardpoints currently blown off. */
    const [destroyed, setDestroyed] = useState<ReadonlySet<string>>(new Set());

    const [fireArcs, setFireArcs] = useState(false);

    /** Empty means the model's own colours; otherwise the faction whose tint is applied. */
    const [faction, setFaction] = useState('');
    const [customColour, setCustomColour] = useState<string | null>(null);

    const [alt, setAlt] = useState(0);
    const [lod, setLod] = useState(0);

    /**
     * Whether the user has picked a detail level themselves.
     *
     * Until they do, the preview follows the model: it opens at the HIGHEST level, which is the
     * close-up one. The engine's numbering runs the opposite way to most - LOD0 is the distant,
     * lowest-detail mesh - so defaulting to zero opened every model on its crudest version.
     */
    const lodChosenRef = useRef(false);
    const [levels, setLevels] = useState<DefinedLevels>({ alt: [0], lod: [0] });

    /**
     * Read inside the message handler, which is not re-created when `destroyed` changes.
     *
     * Geometry and effects arrive asynchronously, so a part or system landing after a hardpoint was
     * destroyed has to see the current state rather than the one captured when the handler was made.
     */
    const destroyedRef = useRef<ReadonlySet<string>>(destroyed);
    destroyedRef.current = destroyed;

    /** Same reason as {@link destroyedRef}: the message handler outlives any one scene. */
    const sceneRef = useRef<PreviewScene | null>(scene);
    sceneRef.current = scene;

    /**
     * The bulk switches this scene offers, derived from it rather than stored.
     *
     * Deriving is what keeps the panel and the tree from disagreeing. The old version held a
     * `hiddenGroups` set beside the tree's own state and had to be re-seeded from the damage rules
     * in two places; miss one and the list claimed "off" about something visibly burning.
     */
    const groups = useMemo(
        () => particleGroups({
            particles: scene?.particles ?? [], hardpoints: scene?.hardpoints ?? [],
        }),
        [scene]);

    /**
     * What the row with its details open is made of.
     *
     * Depends on `treeItems` rather than only on the row id, because the map behind `inspectionOf`
     * is rebuilt by the same `treeItems()` call - so a level change or a reload refreshes the
     * flyout with the rows instead of leaving it describing geometry that is gone.
     */
    const inspectSources = useMemo(() => {
        if (detailsRow === null || !treeItems.some(item => item.id === detailsRow)) {
            return [];
        }

        return viewportRef.current?.inspectionOf(detailsRow) ?? [];
    }, [detailsRow, treeItems]);

    /**
     * How far the ground can be moved on this subject.
     *
     * Keyed on `treeItems` for the same reason the inspection is: that is what a load, a level
     * change or a reload rebuilds, so the track is re-scaled with the model rather than staying
     * sized for the last one.
     */
    const ground = useMemo(
        () => groundRange(viewportRef.current?.subjectSphere.radius ?? 0),
        [treeItems]);

    const inspection = useMemo(
        () => inspectSources.length === 0
            ? null
            : inspectPanels(inspectSources, modelDetail ?? undefined),
        [inspectSources, modelDetail]);

    /** The selected row's geometry, when it has any - what the bulk tables are fetched for. */
    const inspectedMesh = useMemo(
        () => inspectSources.find(source => source.kind === 'mesh') as MeshInspection | undefined,
        [inspectSources]);

    // A table belongs to the row it was opened on. Carrying one across a change of selection would
    // show one sub-mesh's vertices under another's name.
    useEffect(() => {
        setGeometry(null);
        setGeometryError(null);
    }, [inspectSources]);

    const loadGeometry = useCallback((table: GeometryTable, offset: number) => {
        const model = detailWantedRef.current;

        if (inspectedMesh?.meshIndex === undefined || inspectedMesh.subMeshIndex === undefined
            || model === null) {
            return;
        }

        setGeometryError(null);
        vscode.postMessage({
            type: 'requestSubMeshGeometry',
            modelReference: model,
            meshIndex: inspectedMesh.meshIndex,
            subMeshIndex: inspectedMesh.subMeshIndex,
            table,
            offset,
            count: GEOMETRY_PAGE,
        });
    }, [inspectedMesh]);

    // ── the renderer's lifetime, which is the canvas's and not the component's state ──
    useEffect(() => {
        if (canvasRef.current === null || labelsRef.current === null) {
            return;
        }

        const viewport = new PreviewViewport(canvasRef.current, labelsRef.current);
        viewport.expose();
        viewportRef.current = viewport;

        // The renderer, reachable from the page. Everything that decides whether a mesh is on
        // screen - the level gates, the visibility overrides, the material each sub-mesh ended up
        // with - lives inside this object, and the standing rule is that rendering questions get
        // settled by driving the real thing rather than by reading the code. Without a handle the
        // playwright harness can only photograph the canvas, which is how "the collision hull is
        // white" and "some checkboxes do nothing" both went a long time without a cause.
        (window as unknown as { aetDebugViewport?: unknown }).aetDebugViewport = viewport;

        // Clicking a joint selects it in the tree; selecting in the tree highlights the joint. One
        // selection, two ways in, so neither view can disagree with the other about what is chosen.
        viewport.onBoneSelected = index => {
            setSelectedBone(index);
            if (index !== null) {
                setCollapsed(current => {
                    const next = new Set(current);
                    // Tree ids, not raw bone indices: the tree holds meshes and emitters too, so a
                    // bone is keyed `bone:<index>` to keep the three kinds from colliding.
                    for (const ancestor of ancestorsOf(viewport.skeleton(), index)) {
                        next.delete(`bone:${ancestor}`);
                    }
                    return next;
                });
            }
        };

        // Orbiting leaves the preset behind, so the preset stops claiming to be where you are.
        viewport.onCameraMoved = () => setCameraView(null);

        const observer = new ResizeObserver(() => viewport.resize());
        observer.observe(canvasRef.current);

        return () => {
            observer.disconnect();
            viewport.dispose();
            viewportRef.current = null;
        };
    }, []);

    const refreshStats = useCallback(() => {
        const viewport = viewportRef.current;
        setStats(viewport?.stats() ?? null);
        setBones(viewport?.skeleton() ?? []);
        setTreeItems(viewport?.treeItems() ?? []);
        setAttachments(viewport?.attachmentsByBone() ?? new Map());
        const defined = viewport?.definedLevels() ?? { alt: [0], lod: [0] };
        setLevels(defined);

        // Geometry loaded and not one mesh reaching the screen is worth saying out loud. It is a
        // real authoring state - every sub-mesh tagged for a damage or detail level this model
        // never selects, or the whole hull hung off a bone that ships hidden - and it is
        // indistinguishable from a broken preview if the panel just draws an empty grid.
        const current = viewport?.stats() ?? null;
        if (current !== null && current.parts > 0 && current.meshes === 0) {
            setProblems(existing => existing.some(p => p.message === NOTHING_DRAWN)
                ? existing
                : [...existing, { severity: 'warning', message: NOTHING_DRAWN }]);
        }

        // Not until geometry has actually ARRIVED. `definedLevels` seeds its sets with 0, so an
        // empty model reports `lod: [0]` - a full-looking answer that is really "nothing has told me
        // anything yet". Choosing from it picked LOD 0 and latched, and since Alamo numbers detail
        // the other way up, every model opened at its LOWEST detail and stayed there. The guard
        // below tested for an EMPTY list, which that placeholder never is.
        if (lodChosenRef.current || (current?.parts ?? 0) === 0 || defined.lod.length === 0) {
            return;
        }

        // Where this subject was left, but only if the model still HAS that level. A mesh's `_ALT2`
        // suffix can be edited away between one open and the next, and restoring a level that no
        // longer exists shows nothing at all - indistinguishable from a broken model.
        const previous = storedSubjectRef.current;

        if (previous !== null && defined.alt.includes(previous.alt)) {
            setAlt(previous.alt);
        }

        lodChosenRef.current = true;
        setLod(previous !== null && defined.lod.includes(previous.lod)
            ? previous.lod
            : defined.lod[defined.lod.length - 1]);
    }, []);

    /**
     * Shows or hides one emitter.
     *
     * Held in the shell as well as the viewport because the checkbox has to survive a restart, which
     * rebuilds every instance from scratch.
     */
    const toggleEmitter = useCallback(
        (systemId: string, index: number, visible: boolean): void => {
            setHiddenEmitters(current => {
                const next = new Set(current);
                if (visible) {
                    next.delete(index);
                } else {
                    next.add(index);
                }
                return next;
            });

            viewportRef.current?.setEmitterVisible(systemId, index, visible);
        }, []);

    /**
     * Whether a particle system plays right now.
     *
     * Damage smoke starts off: the hardpoint is intact, and a ship that smokes from every mount the
     * moment you open it tells the author nothing. Chunk 14's destroy switches it on.
     */

    /**
     * Translates every effect whose headers have all arrived, and hands the result to the viewport.
     *
     * Retried on each arrival rather than tracked as a dependency graph: there are a handful of
     * headers, they are shared, and re-running a pure translation is cheaper than the bookkeeping.
     */
    const translatePending = useCallback((): void => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        const read = (name: string): string | null =>
            shaderSourcesRef.current.get(name.toLowerCase()) ?? null;

        for (const [name, source] of shaderSourcesRef.current) {
            if (source === null || !name.endsWith('.fx')
                || translatedShadersRef.current.has(name)) {
                continue;
            }

            const result = translateEffect(source, read);

            // Ask for the headers this one needs. Each is requested once; an absent one comes back
            // as null and stops the retries rather than looping.
            for (const missing of result.missingIncludes) {
                if (!requestedShadersRef.current.has(missing.toLowerCase())) {
                    requestedShadersRef.current.add(missing.toLowerCase());
                    vscode.postMessage({ type: 'requestShader', name: missing });
                }
            }

            if (result.effect !== null) {
                translatedShadersRef.current.add(name);
                viewport.applyTranslatedEffect(name, result.effect);
                setTranslatedCount(viewport.translatedCoverage());

                // A sampler reading nothing draws black, which looks like a translation bug and is
                // usually a texture that did not resolve. Say so rather than leave it on screen.
                for (const problem of viewport.shaderTextureProblems()) {
                    setProblems(current => current.some(p => p.message === problem)
                        ? current
                        : [...current, { severity: 'info', message: problem }]);
                }
            } else if (result.missingIncludes.length === 0) {
                // Nothing more is coming for this one; the archetype is what it draws with.
                translatedShadersRef.current.add(name);
            }
        }
    }, []);

    /** Attaches every pending particle whose system and part have both arrived. */
    const attachPending = useCallback((): void => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        const stillWaiting: PreviewParticle[] = [];

        for (const particle of pendingRef.current) {
            const system = systemsRef.current.get(particle.systemRef.toLowerCase());

            if (system === undefined || !viewport.hasPart(particle.partId)) {
                stillWaiting.push(particle);
                continue;
            }

            viewport.addParticleSystem(particle.id, system, particle.partId, particle.bone, {
                alt: particle.alt ?? null,
                lod: particle.lod ?? null,
                altDecreaseStayHidden: particle.altDecreaseStayHidden ?? false,
            }, particle.boneIndex);

            viewport.setParticleSystemVisible(
                particle.id, effectPlaysNow(particle, destroyedRef.current));
        }

        pendingRef.current = stillWaiting;

        // Everything the dock reads, not just the tree. Systems attach well after the geometry
        // does, and a model can declare its damage states ENTIRELY on proxies - `Rv_mptl-2a.alo`
        // has eleven ALT-tagged smoke and static effects and not one ALT-tagged mesh. Refreshing
        // only the tree left `levels` at what the meshes alone implied, so the Damage state control
        // never appeared and ALT looked unimplemented on exactly the models that lean on it hardest.
        refreshStats();

        for (const name of viewport.particleTextureNames()) {
            vscode.postMessage({ type: 'requestTexture', name });
        }
    }, [refreshStats]);

    /**
     * Destroys or repairs one hardpoint.
     *
     * The death explosion fires here rather than in the effect above, because it is an event: it
     * belongs to the moment of destruction, not to the state of being destroyed, and replaying it
     * every time the scene re-renders would leave a ship permanently exploding.
     */
    const setHardpointDestroyed = useCallback((id: string, isDestroyed: boolean): void => {
        setDestroyed(current => {
            if (current.has(id) === isDestroyed) {
                return current;
            }

            const next = new Set(current);
            if (isDestroyed) {
                next.add(id);
            } else {
                next.delete(id);
            }
            return next;
        });

        if (!isDestroyed) {
            return;
        }

        const hardpoint = scene?.hardpoints.find(h => h.id === id);
        const explosion = hardpoint?.deathExplosionParticles ?? '';
        if (explosion === '') {
            return;
        }

        const system = systemsRef.current.get(explosion.toLowerCase());
        if (system === undefined) {
            // Fetched now and played on arrival; a system nothing referenced until this moment is
            // not worth loading up front for every hardpoint on a capital ship.
            explosionsRef.current.push({ id, system: explosion });
            vscode.postMessage({ type: 'requestParticleSystem', name: explosion });
            return;
        }

        viewportRef.current?.playOnce(
            `explosion:${id}:${Date.now()}`, system, 'hull', hardpoint?.attachBone ?? undefined);
    }, [scene]);

    /** Destroys or repairs every mount the XML allows to be destroyed. */
    const destroyAll = useCallback((isDestroyed: boolean): void => {
        for (const hardpoint of scene?.hardpoints ?? []) {
            if (hardpoint.isDestroyable) {
                setHardpointDestroyed(hardpoint.id, isDestroyed);
            }
        }
    }, [scene, setHardpointDestroyed]);

    /**
     * What is wrong with the colour currently applied, if anything.
     *
     * Recomputed rather than stored: it is a pure function of the chosen colour and the faction
     * list, and caching it would only create a second thing that can be stale.
     */
    const colourFindings = useMemo(() => {
        const chosen = customColour !== null
            ? parseHex(customColour)
            : scene?.factions.find(f => f.name === faction)?.color ?? null;

        return chosen === null
            ? []
            : reviewFactionColour(
                chosen, customColour !== null ? '' : faction, scene?.factions ?? []);
    }, [customColour, faction, scene]);

/**
     * Switches every system in a group at once.
     *
     * Writes straight through to the systems and keeps nothing: the group's own switch reads back
     * out of them, so this is the only direction state ever moves. The tree refreshes with it,
     * because those same systems each have a row of their own.
     */
    const toggleGroup = useCallback((group: ParticleGroup, visible: boolean): void => {
        for (const id of group.ids) {
            // Through the row chain like every other row, so the effects master and the level
            // gate still get their say and nothing writes visibility behind it. The ROW is asked
            // for: an effect merged into its proxy bone is not addressed as `particle:<id>`.
            viewportRef.current?.applyRowOverrides([{
                row: viewportRef.current.rowForSystem(id),
                override: visible === null ? null : visible ? 'shown' : 'hidden',
            }]);
        }

        setTreeItems(viewportRef.current?.treeItems() ?? []);
    }, []);

    // ── messages from the host ────────────────────────────────────────────────
    useEffect(() => {
        const onMessage = async (event: MessageEvent<HostMessage>): Promise<void> => {
            const viewport = viewportRef.current;
            if (viewport === null) {
                return;
            }

            const message = event.data;

            if (message.type === 'scene') {
                viewport.clear();
                setScene(message.scene);
                setProblems(message.scene.problems);
                setAnimation(null);
                setSelectedBone(null);
                setBoneFilter('');
                setCollapsed(new Set());
                setEmitters([]);
                setHiddenEmitters(new Set());
                setAlt(0);
                setLod(0);
                lodChosenRef.current = false;
                setDestroyed(new Set());

                // The faction the reader last picked, if THIS subject has one by that name -
                // reviewing a roster should survive opening the next unit, and fall away quietly
                // when it does not apply rather than tinting the wrong thing.
                setFaction(message.scene.factions.some(f => f.name === storedFactionRef.current)
                    ? storedFactionRef.current ?? ''
                    : '');
                setCustomColour(null);
                foldedRef.current = false;
                setMode(defaultMode(message.scene.kind, {
                    animations: 0,
                    hardpoints: message.scene.hardpoints.length,
                    particles: message.scene.particles.length,
                }));

                // Where this subject was left earlier in the session. Applied AFTER the reset above,
                // which is what the reset is for on a subject opened for the first time - the
                // opening rules - and would otherwise undo.
                const previous = storedSubjectRef.current;

                if (previous !== null) {
                    setDestroyed(new Set(previous.destroyed));
                    setHiddenEmitters(new Set(previous.hiddenEmitters));
                    setAnimation(previous.animation);
                    setBoneFilter(previous.filterText);
                    setCollapsed(new Set(previous.collapsed));
                    foldedRef.current = true;
                }
                systemsRef.current.clear();
                requestedSystemsRef.current.clear();
                requestedShadersRef.current.clear();
                // Defensive: a server older than this client sends no particles at all, and
                // spreading undefined throws inside the handler - which loses the whole scene,
                // geometry included, for the sake of an effects list.
                const particles = message.scene.particles ?? [];
                pendingRef.current = [...particles];

                refreshStats();

                // One request per distinct system, however many proxies call for it.
                for (const name of new Set(particles.map(particle => particle.systemRef))) {
                    requestedSystemsRef.current.add(name.toLowerCase());
                    vscode.postMessage({ type: 'requestParticleSystem', name });
                }

                // A particle system is not geometry, so it takes getParticleSystem rather than
                // getModelGlb. The server has already classified the file by its root chunk; the
                // extension alone cannot tell the two apart.
                if (message.scene.kind === PREVIEW_SCENE_KIND.particle) {
                    vscode.postMessage({
                        type: 'requestParticleSystem',
                        name: message.scene.subject,
                    });
                    return;
                }

                // What is inside the HULL, for the inspector. Only the hull: a mounted turret is a
                // model of its own with its own bone list, and mixing two files' indices into one
                // answer is exactly the join mistake `alamoMeshIndex` exists to prevent.
                const hull = message.scene.parts.find(part => part.resolved) ?? null;
                detailWantedRef.current = hull?.modelRef ?? null;
                setModelDetail(null);

                if (hull !== null) {
                    vscode.postMessage({
                        type: 'requestModelDetail', modelReference: hull.modelRef,
                    });
                }

                // Ask for every part's geometry separately, so a large unit draws its hull as soon as
                // it arrives rather than after the last turret.
                for (const part of message.scene.parts) {
                    if (!part.resolved) {
                        continue;
                    }

                    vscode.postMessage({
                        type: 'requestGlb',
                        partId: part.id,
                        modelReference: part.modelRef,
                        attachToPartId: part.attachToPartId ?? undefined,
                        attachBone: part.attachBone ?? undefined,
                    });
                }

                return;
            }

            if (message.type === 'glb') {
                const glb = message.result.glb ?? null;

                if (glb === null) {
                    const error = message.result.error ?? null;
                    if (error !== null) {
                        setProblems(current => [...current, { severity: 'error', message: error }]);
                    }
                    return;
                }

                await viewport.addPart(
                    message.partId, glb,
                    message.attachToPartId ?? undefined, message.attachBone ?? undefined);

                // The bound shot, if this subject has one, INSTEAD of the default framing - not
                // after it, so the model does not visibly jump from one to the other on open.
                // Always overridable by hand afterwards: a rule is a default, not a cage.
                if (!applyBoundCamera(viewport)) {
                    viewport.frameAll();
                }

                refreshStats();

                // Textures are asked for only once the geometry that samples them exists, so nothing
                // is fetched for a part that failed to load.
                for (const name of collectTextureNames(viewport.materialExtras())) {
                    vscode.postMessage({ type: 'requestTexture', name });
                }

                // The effects this geometry names. Each is fetched once; a missing one is normal and
                // simply leaves the archetype material in place.
                for (const shader of viewport.shaderNames()) {
                    if (!requestedShadersRef.current.has(shader.toLowerCase())) {
                        requestedShadersRef.current.add(shader.toLowerCase());
                        vscode.postMessage({ type: 'requestShader', name: shader });
                    }
                }

                // This part's bones now exist, so anything waiting to hang off them can attach.
                attachPending();

                return;
            }

            if (message.type === 'modelDetail') {
                // Only when it is still about the subject on screen. Replies cross with a change of
                // model, and showing one model's bone matrices against another is worse than
                // showing none - the numbers would look perfectly plausible.
                //
                // Matched on the ECHOED request, not on `detail.model`: the server normalises a
                // reference (`EV_StarDestroyer.ALO` comes back as `Ev_stardestroyer`), so comparing
                // against what was asked for would never match and the panel would stay empty.
                const arrived = message.result.detail ?? null;

                if (arrived !== null && message.modelReference === detailWantedRef.current) {
                    setModelDetail(arrived);
                }

                return;
            }

            if (message.type === 'subMeshGeometry') {
                setGeometry(message.result.page ?? null);
                setGeometryError(message.result.error ?? null);
                return;
            }

            if (message.type === 'particleSystem') {
                const system = message.result.system ?? null;

                if (system === null) {
                    const error = message.result.error ?? null;
                    if (error !== null) {
                        setProblems(current => [...current, { severity: 'error', message: error }]);
                    }
                    return;
                }

                // A particle file opened directly is its own subject and hangs off nothing.
                if (pendingRef.current.length === 0 && !requestedSystemsRef.current.has(
                    message.name.toLowerCase())) {
                    viewport.addParticleSystem(message.name, system);
                    viewport.frameAll();

                    // Heat emitters are labelled rather than hidden: they draw as a distortion of
                    // the frame behind them, which is easy to miss and easy to mistake for a bug
                    // in the effect if the row does not say what it is.
                    setEmitters(system.emitters.map(emitter => emitter.name
                        + (emitter.properties.isHeatParticle ? ' [heat]' : '')));

                    // Asked for only now, because the system is what names them.
                    for (const name of viewport.particleTextureNames()) {
                        vscode.postMessage({ type: 'requestTexture', name });
                    }

                    return;
                }

                systemsRef.current.set(message.name.toLowerCase(), system);

                // A death explosion asked for at the moment of destruction plays as soon as it lands.
                const waiting = explosionsRef.current
                    .filter(pending => pending.system.toLowerCase() === message.name.toLowerCase());

                if (waiting.length > 0) {
                    explosionsRef.current = explosionsRef.current.filter(
                        pending => !waiting.includes(pending));

                    for (const pending of waiting) {
                        const bone = sceneRef.current?.hardpoints
                            .find(h => h.id === pending.id)?.attachBone ?? undefined;

                        viewport.playOnce(
                            `explosion:${pending.id}:${Date.now()}`, system, 'hull', bone);
                    }
                }

                attachPending();

                return;
            }

            if (message.type === 'viewerSettings') {
                // The room the reader left behind. Applied before any geometry arrives, so nothing
                // visibly snaps into place a frame later.
                const room = viewerSettingsFrom(message.settings);

                setGrid(room.grid);
                setFloor(room.floor);
                setFloorLevel(room.floorLevel);
                setWireframe(room.wireframe);
                setHeatOn(room.heat);
                setHeatDebug(room.heatDebug);
                setBloom(room.bloom);
                setLights(room.lights);
                setCameraPresets(room.cameraPresets);
                setCameraBindings(room.cameraBindings);
                setFolded(new Set(room.collapsedPanels));
                setWind(room.wind);
                setBackground(room.background);
                // Only a PRESET is restorable. A camera id names a bone in whatever model was
                // open at the time, and a stored `camera:1` would light a button for a camera this
                // subject may not have - so anything else comes back as "no preset", which is what
                // a dragged camera already looks like.
                setCameraView(PRESETS.some(p => p.view === room.cameraPreset)
                    ? room.cameraPreset
                    : null);
                setDrawDistance(room.drawDistance);
                setSkeletonOn(room.skeleton);
                setLabelMode(room.boneLabels as LabelMode);
                setFireArcs(room.fireArcs);
                setTranslatedOn(room.effectShaders);
                setParticlesOn(room.particles);
                setParticleSpeed(room.particleSpeed);
                setCustomColour(room.customColour);

                // By NAME, and only if this subject has it: "I am reviewing the Rebel roster"
                // should survive the next unit and fall away quietly when it does not apply.
                storedFactionRef.current = room.faction;
                storedSubjectRef.current = message.subject === null
                    ? null
                    : subjectStateFrom(message.subject);

                restoredRef.current = true;
                return;
            }

            if (message.type === 'shader') {
                const source = message.result.source ?? null;
                shaderSourcesRef.current.set(message.name.toLowerCase(), source);

                if (source !== null) {
                    setAnyShaderSource(true);
                }

                if (source === null) {
                    // No copy of this effect is reachable. The archetype material stands, which is
                    // the same thing that happens for any mod-authored shader.
                    return;
                }

                const technique = selectTechnique(parseFxManifest(source));
                const pass = technique?.passes[0];

                if (pass !== undefined) {
                    viewport.applyShaderState(message.name, materialStateFrom(pass));
                }

                if (technique !== null && !hasProgrammableShader(technique)) {
                    // Eleven of the shipped effects are render state and nothing else. Worth knowing
                    // that the plain look is the effect, not a failure to read it.
                    setProblems(current => [...current, {
                        severity: 'info',
                        message: `'${message.name}' sets render state only, so there is no shader `
                            + 'to translate.',
                    }]);
                }

                // A header may have arrived after the effect that wanted it, so every effect still
                // waiting is retried whenever anything new lands.
                translatePending();

                return;
            }

            if (message.type === 'texture') {
                const texture = (message.result.data ?? null) === null
                    ? null
                    : decodeTexture(message.result);

                if (texture !== null) {
                    // Offered to both: a name resolves to one texture whichever kind of thing samples
                    // it, and the shell does not track which asked.
                    viewport.setTexture(message.name, texture);
                    viewport.setParticleTexture(message.name, texture);
                } else {
                    // The reply IS the answer - the server sends no data when the name did not
                    // resolve. Dropping it silently left every emitter naming that texture drawing
                    // a plain tinted quad, which is exactly what a soft coloured puff is supposed
                    // to look like. A decode that fails counts the same: either way the reader
                    // cannot see the texture, and needs to know that rather than guess.
                    viewport.setParticleTextureMissing(message.name);

                    // And said in words, not only drawn. The marker tells you SOMETHING is missing
                    // at the place it is missing from; the problems bar is what names the file. The
                    // server-side ModelTextureExistence diagnostic covers the same ground without
                    // opening the preview, but only for a texture a model names statically - this
                    // also catches one the server resolved and the client could not decode.
                    const missing = message.result.error ?? `Texture '${message.name}' was not `
                        + 'found. Anything that draws with it shows the AET logo instead.';

                    setProblems(current => current.some(p => p.message === missing)
                        ? current
                        : [...current, { severity: 'warning', message: missing }]);
                }
            }
        };

        // Wrapped rather than cast: an async handler is not an EventListener, and asserting it to be
        // one hides that the rejection would go unhandled.
        const listener = (event: Event): void => {
            void onMessage(event as MessageEvent<HostMessage>);
        };

        globalThis.addEventListener('message', listener);
        vscode.postMessage({ type: 'ready' });

        return () => globalThis.removeEventListener('message', listener);
        // Deliberately NOT depending on `scene`: this effect posts 'ready', so re-running it asks
        // for another scene, which changes `scene`, which re-runs it. The handler reads the current
        // scene through a ref instead.
    }, [attachPending, refreshStats]);

    useEffect(() => {
        viewportRef.current?.setGridVisible(grid);
    }, [grid]);

    useEffect(() => {
        viewportRef.current?.setFloorVisible(floor);
    }, [floor]);

    useEffect(() => {
        viewportRef.current?.setFloorLevel(floorLevel);
    }, [floorLevel]);

    useEffect(() => {
        viewportRef.current?.setWireframe(wireframe);
    }, [wireframe]);

    useEffect(() => {
        viewportRef.current?.setHeat(heatOn);
    }, [heatOn]);

    useEffect(() => {
        viewportRef.current?.setHeatDebug(heatDebug);
    }, [heatDebug]);

    useEffect(() => {
        viewportRef.current?.setBloom(bloom);
    }, [bloom]);

    useEffect(() => {
        viewportRef.current?.setLightRig(lights);
        viewportRef.current?.setShadowColour(hexFromColour(lights.shadow));
    }, [lights]);

    useEffect(() => {
        viewportRef.current?.setWind(wind.heading, wind.speed);
    }, [wind]);

    useEffect(() => {
        viewportRef.current?.setBackground(background);
    }, [background]);

    /**
     * Starting a clip puts the MODEL back first.
     *
     * A full reset, not just the row overrides the viewport already clears: the damage state and the
     * detail level go back to what the model opens at, and whatever the reader picks afterwards
     * layers on top of a known state. Watching an animation is asking whether the animation works,
     * and that question cannot be answered against a model carrying twenty minutes of half-forgotten
     * ticks, a damage level and a detail level.
     *
     * Deliberately only when a clip STARTS. Stopping one leaves the model where the reader left it,
     * because putting it back at that point would undo work they did while watching.
     */
    useEffect(() => {
        if (animation !== null) {
            setAlt(0);
            setLod(current => levels.lod.length > 0 ? levels.lod[levels.lod.length - 1] : current);
            setDestroyed(new Set());
        }

        viewportRef.current?.play(animation);
        setTreeItems(viewportRef.current?.treeItems() ?? []);
        // `levels` is read but deliberately NOT a dependency: this fires when the CLIP changes, and
        // adding it would reset the model every time the defined levels were recomputed - which
        // happens on every load and every level change.
    }, [animation]);

    useEffect(() => {
        viewportRef.current?.setSkeletonVisible(skeletonOn);
    }, [skeletonOn]);

    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null || scene === null) {
            return;
        }

        for (const hardpoint of scene.hardpoints) {
            if (hardpoint.partId !== null && hardpoint.partId !== undefined) {
                viewport.setPartHidden(
                    hardpoint.partId, partHidden(hardpoint.partId, scene.hardpoints, destroyed));
            }
        }

        // Every decal the scene knows about, and the subset that should be showing.
        viewport.setDecals(
            decalNames(scene.hardpoints, new Set(scene.hardpoints.map(h => h.id))),
            decalNames(scene.hardpoints, destroyed));

        for (const particle of scene.particles ?? []) {
            viewport.setParticleSystemVisible(particle.id, effectPlaysNow(particle, destroyed));
        }

        // Both the tree and the group switches read out of the systems that just changed, so this
        // is all it takes to bring the dock along. Destroying a mount lights its smoke and every row
        // that names it says so.
        setTreeItems(viewport.treeItems() ?? []);
    }, [destroyed, scene]);

    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        // Every fire bone of every hardpoint that declares an arc. A hardpoint with no cone tags is
        // skipped rather than drawn as a zero-length stub.
        viewport.setFireArcs((scene?.hardpoints ?? []).flatMap(hardpoint => {
            const fire = hardpoint.fire;
            if (fire === null || fire === undefined || (fire.range ?? 0) <= 0) {
                return [];
            }

            return fire.bones.map(bone => ({
                partId: hardpoint.partId ?? 'hull',
                bone,
                widthDegrees: fire.coneWidthDegrees ?? 0,
                heightDegrees: fire.coneHeightDegrees ?? 0,
                range: fire.range ?? 0,
            }));
        }));

        viewport.setFireArcsVisible(fireArcs);

        // Turrets sit where the XML says they rest, rather than wherever the model was exported.
        viewport.setTurretRestAngles((scene?.hardpoints ?? []).flatMap(hardpoint => {
            const turret = hardpoint.turret;
            const bone = turret?.turretBone ?? '';

            return turret === null || turret === undefined || bone === ''
                || turret.restAngle === null || turret.restAngle === undefined
                ? []
                : [{
                    partId: hardpoint.partId ?? 'hull',
                    bone,
                    restAngleDegrees: turret.restAngle,
                }];
        }));
    }, [scene, stats, fireArcs]);

    useEffect(() => {
        const chosen = customColour !== null
            ? parseHex(customColour)
            : scene?.factions.find(f => f.name === faction)?.color ?? null;

        viewportRef.current?.setColorization(chosen === null ? null : teamColour(chosen));
    }, [faction, customColour, scene, stats]);

    useEffect(() => {
        viewportRef.current?.setLevels(alt, lod);
        // Geometry may have been hidden or shown, which changes the stat line - and the tree's
        // checkboxes, since a level change drops every hand-set visibility along with it.
        setStats(viewportRef.current?.stats() ?? null);
        setAttachments(viewportRef.current?.attachmentsByBone() ?? new Map());
        setTreeItems(viewportRef.current?.treeItems() ?? []);
    }, [alt, lod]);

    useEffect(() => {
        viewportRef.current?.setParticlesVisible(particlesOn);
    }, [particlesOn]);

    useEffect(() => {
        viewportRef.current?.setAnimationSpeed(animationSpeed);
    }, [animationSpeed]);

    useEffect(() => {
        viewportRef.current?.setAnimationLoop(animationLoop);
    }, [animationLoop]);

    useEffect(() => {
        viewportRef.current?.setPaused(animationPaused);
    }, [animationPaused, animation]);

    /**
     * Follows the playhead while a clip is loaded.
     *
     * A timer rather than a hook into the render loop: the scrubber only has to look continuous to
     * a person, and 20 a second does that for a tenth of the re-renders 60 would cost. It runs only
     * in Animation mode, so the other lenses pay nothing for a control they do not show.
     */
    useEffect(() => {
        if (mode !== 'animation' || animation === null) {
            return;
        }

        const tick = setInterval(() => {
            if (!scrubbingRef.current) {
                setPlayhead(viewportRef.current?.animationProgress()
                    ?? { time: 0, duration: 0, running: false });
            }
        }, 50);

        return () => clearInterval(tick);
    }, [mode, animation]);

    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        viewport.setTranslatedShaders(translatedOn);

        // Game mode shadows the way the ENGINE does: stencil volumes cast by the model's own
        // authored shadow mesh, with the shadow colour reaching the hull as well as the ground.
        // Default mode keeps three's shadow mapping, which is the right answer for the 63% of
        // models that author no volume at all.
        viewport.setShadowVolumes(translatedOn);
        setTranslatedCount(viewport.translatedCoverage());
    }, [translatedOn]);

    useEffect(() => {
        viewportRef.current?.setParticlesPaused(particlesPaused);
    }, [particlesPaused]);

    useEffect(() => {
        viewportRef.current?.setParticleSpeed(particleSpeed);
    }, [particleSpeed]);

    useEffect(() => {
        viewportRef.current?.setLabelMode(labelMode);
    }, [labelMode]);

    useEffect(() => {
        viewportRef.current?.setDrawDistance(drawDistance);
    }, [drawDistance]);

    /**
     * Folds the scaffolding away the first time a subject's rows arrive.
     *
     * Most of a skeleton is structure nobody opened the tree to read - 50 of the Star Destroyer's
     * 74 rows are bones carrying no geometry and no effect - and with all of it expanded the few
     * rows being looked for are buried. Collapsed, not hidden: one click brings any limb back.
     *
     * Once per subject, so a rebuild after a level change or a toggle cannot re-fold something the
     * reader has just opened.
     */
    useEffect(() => {
        if (foldedRef.current || treeItems.length === 0) {
            return;
        }

        foldedRef.current = true;
        setCollapsed(current => current.size > 0
            ? current
            : defaultCollapsed(buildTree(treeItems)));

        // And whatever the reader had switched by hand, now that a mesh key survives a reload.
        // Both directions: a hidden mesh and a system deliberately lit are equally deliberate, and
        // a system is usually off to begin with so only `shown` can carry it back.
        const hidden = storedSubjectRef.current?.hidden ?? [];
        const shown = storedSubjectRef.current?.shown ?? [];

        if (hidden.length > 0 || shown.length > 0) {
            viewportRef.current?.setItemsVisible(hidden, false);
            viewportRef.current?.setItemsVisible(shown, true);
            setTreeItems(viewportRef.current?.treeItems() ?? []);
        }
    }, [treeItems]);

    /**
     * Remembers the room.
     *
     * One effect over every Tier 1 value rather than a write beside each control: a setting is
     * remembered because of what it IS, not because whoever added its checkbox remembered to save
     * it. Skipped until the stored room has been applied, or the first frame would overwrite it
     * with defaults.
     */
    useEffect(() => {
        if (!restoredRef.current) {
            return;
        }

        vscode.postMessage({
            type: 'setViewerSettings',
            settings: {
                ...DEFAULT_VIEWER_SETTINGS,
                grid, floor, floorLevel, drawDistance,
                wireframe,
                heat: heatOn,
                heatDebug,
                bloom,
                skeleton: skeletonOn,
                boneLabels: labelMode,
                fireArcs,
                effectShaders: translatedOn,
                particles: particlesOn,
                particleSpeed,
                faction: faction === '' ? null : faction,
                customColour,
                lights,
                cameraPresets,
                cameraBindings,
                collapsedPanels: [...folded],
                wind,
                background,
                cameraPreset: cameraView ?? '',
            },
        });
    }, [grid, floor, floorLevel, wireframe, heatOn, heatDebug, bloom, drawDistance, lights, wind,
        cameraPresets, cameraBindings, folded, background, cameraView, skeletonOn, labelMode,
        fireArcs,
        translatedOn, particlesOn, particleSpeed, faction, customColour]);

    /**
     * Remembers where this subject was left, for as long as the window lives.
     *
     * Session-scoped on purpose: reopening the same model should pick up where you were, while a
     * fresh window must obey the opening rules rather than restore last week's damage.
     */
    useEffect(() => {
        if (!restoredRef.current) {
            return;
        }

        vscode.postMessage({
            type: 'setSubjectState',
            state: {
                alt, lod,
                destroyed: [...destroyed],
                hiddenEmitters: [...hiddenEmitters],
                animation,
                filterText: boneFilter,
                collapsed: [...collapsed],
                hidden: viewportRef.current?.hiddenItemIds() ?? [],
                shown: viewportRef.current?.shownItemIds() ?? [],
            } satisfies SubjectState,
        });
    }, [alt, lod, destroyed, hiddenEmitters, animation, boneFilter, collapsed, treeItems]);

    useEffect(() => {
        viewportRef.current?.setSelectedBone(selectedBone);
    }, [selectedBone]);

    // Depends on `treeItems` as well as on the selection, because the rows a box spans are only
    // known once the tree has been built - a selection restored before the model arrives would
    // otherwise box nothing and look broken.
    useEffect(() => {
        viewportRef.current?.setSelectedRows([...selected]);
    }, [selected, treeItems]);

    /**
     * Escape lets go of the selection.
     *
     * After the panels, so a flyout still closes first: Escape means "back out of the innermost
     * thing", and dropping a selection while a panel is open would be backing out of the wrong one.
     */
    useEffect(() => {
        if (selected.size === 0) {
            return;
        }

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === 'Escape' && !worldOpen && !cameraOpen && detailsRow === null) {
                clearSelection();
            }
        };

        document.addEventListener('keydown', onKey);

        return () => document.removeEventListener('keydown', onKey);
    }, [selected, worldOpen, cameraOpen, detailsRow, clearSelection]);

    /**
     * Pins the details flyout beside the row it belongs to.
     *
     * Measured rather than positioned in CSS, because the row lives inside a scroller: a flyout
     * parented to it would be clipped by the list, and one parented outside has no idea where the
     * row ended up. Both are read at the moment of placing, so a resized dock or a scrolled tree
     * moves it rather than leaving it pointing at nothing.
     */
    const placeDetails = useCallback(() => {
        const box = detailsRef.current;

        if (detailsRow === null || box === null) {
            return;
        }

        const button = document.querySelector(`[data-row="${detailsRow}"] .row-details`);
        const dock = document.querySelector('.right-dock');

        if (button === null || dock === null) {
            return;
        }

        const row = button.getBoundingClientRect();

        setDetailsAt(anchorFlyout({
            row: { top: row.top, bottom: row.bottom },
            dockLeft: dock.getBoundingClientRect().left,
            window: { width: window.innerWidth, height: window.innerHeight },
            size: { width: box.offsetWidth, height: box.offsetHeight },
        }));
    }, [detailsRow]);

    // Before the browser paints, so the flyout never appears at the last row's position and jumps.
    // `inspection` is in the dependencies because the content is what decides how TALL it is, and
    // the height is half of where it goes.
    useLayoutEffect(() => { placeDetails(); }, [placeDetails, inspection, geometry]);

    useEffect(() => {
        if (detailsRow === null) {
            return;
        }

        const onMove = (): void => placeDetails();
        const list = document.querySelector('.bone-tree');

        window.addEventListener('resize', onMove);
        list?.addEventListener('scroll', onMove);

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === 'Escape') {
                setDetailsRow(null);
            }
        };

        document.addEventListener('keydown', onKey);

        return () => {
            window.removeEventListener('resize', onMove);
            list?.removeEventListener('scroll', onMove);
            document.removeEventListener('keydown', onKey);
        };
    }, [detailsRow, placeDetails]);

    /**
     * The skeleton control's three positions, over the two switches underneath.
     *
     * `skeletonOn` and `labelMode` are what the viewport takes, and they stay that way - the
     * viewport has no business knowing this is one control. Off is the only reading that needs a
     * choice made for it: leaving `labelMode` at whatever it was means turning the skeleton back
     * on restores the labelling you had, which is what you meant by turning it off.
     */
    const skeletonMode: 'off' | 'selected' | 'always' =
        !skeletonOn ? 'off' : labelMode === 'all' ? 'always' : 'selected';

    const setSkeletonMode = (next: 'off' | 'selected' | 'always'): void => {
        setSkeletonOn(next !== 'off');

        if (next !== 'off') {
            setLabelMode(next === 'always' ? 'all' : 'selected');
        }
    };

    /**
     * Everything worth reporting about this preview, from every source at once.
     *
     * Derived rather than pushed into the problems state: a colour finding is a pure function of
     * the colour currently applied, so appending it to state would stack a fresh copy on every
     * click of the palette. The severity tag and the problems bar both read THIS, which is what
     * makes "where do I find out what is wrong" have one answer.
     */
    const notices = useMemo<PreviewProblem[]>(() => {
        const fromColour = colourFindings.map(finding => ({
            severity: finding.severity,
            message: finding.message,
            hardpointId: null,
        }));

        // The shader gap is a limitation of the preview rather than a fault in the mod, so it is
        // info. The corner button is the way OUT of it; this is the explanation of it.
        const fromShaders = translatedOn && !anyShaderSource
            ? [{
                severity: 'info',
                message: 'No shader sources are reachable, so nothing can be translated. A game '
                    + 'install ships compiled .fxo files; the .fx sources are a separate download '
                    + 'published by Petroglyph.',
                hardpointId: null,
            }]
            : [];

        return [...problems, ...fromColour, ...fromShaders];
    }, [problems, colourFindings, translatedOn, anyShaderSource]);

    const severity = worstSeverity(notices);
    const problemWord = notices.length === 1 ? 'note' : 'notes';

    /**
     * Everything the viewport can be asked to draw or not draw, as one strip of pills.
     *
     * They are gathered here rather than written out in the markup because which ones apply depends
     * on the lens and on the subject - the skeleton is Model's business, firing arcs are Gameplay's,
     * and particles only exist if the model has emitters. Assembling the list keeps that reasoning
     * in one place instead of scattering five conditions through the panel.
     */
    const overlays: {
        id: string; label: string; icon: IconName; title: string;
        on: boolean; set: (on: boolean) => void; disabled?: boolean;
    }[] = [
        {
            id: 'grid', label: 'Grid', icon: 'grid', on: grid, set: setGrid,
            title: 'The ground grid and the world axes, for scale and orientation',
        },
        {
            id: 'floor', label: 'Ground', icon: 'ground', on: floor, set: setFloor,
            title: 'A lit floor under the model, so a walker or a turret does not read as floating',
        },
        {
            // With the view toggles rather than in the scene panel: this is flipped constantly
            // while reading geometry, the same way the grid is, and it describes how the model is
            // DRAWN rather than what the room is like.
            id: 'wireframe', label: 'Wireframe', icon: 'geometry',
            on: wireframe, set: setWireframe,
            title: 'Draw every mesh as its edges. Reaches the translated effect shaders too.',
        },
    ];

    // Never removed. A control that is absent on one model and present on the next reads as a
    // feature that comes and goes; disabled says "this exists, and here is why it is not available".
    const anyEffect = emitters.length > 0 || groups.length > 0;

    overlays.push({
        id: 'particles', label: 'Effects', icon: 'effects',
        on: particlesOn && anyEffect, set: setParticlesOn,
        disabled: !anyEffect,
        title: anyEffect
            ? `Every particle system on this model at once - ${emitters.length} emitter`
                + `${emitters.length === 1 ? '' : 's'}. Off leaves the geometry alone.`
            : 'This model carries no particle systems.',
    });
    // Arcs LAST, and deliberately so. It is the one pill here that still comes and goes - it
    // belongs to the Gameplay lens, which is a statement about which lens you are in rather than
    // about this model - so it sits at the end of the strip where arriving and leaving cannot move
    // the three permanent pills beside it.
    if (mode === 'gameplay') {
        const anyArc = scene?.hardpoints.some(h => h.fire !== null && h.fire !== undefined) ?? false;

        overlays.push({
            id: 'arcs', label: 'Arcs', icon: 'arcs', on: fireArcs && anyArc, set: setFireArcs,
            disabled: !anyArc,
            title: anyArc
                ? 'The firing arc each weapon hardpoint declares'
                : 'No hardpoint on this model declares a firing arc.',
        });
    }

    const animations = stats?.animations ?? [];

    /**
     * The clips, gathered into families.
     *
     * Keyed off the HULL's model, not off the subject. A clip is named after the .alo it drives -
     * `EV_StarDestroyer_idle_00` - while the subject is whatever was opened, which for a game object
     * is its XML name (`Generic_Star_Destroyer`). Grouping on the subject would strip no prefix at
     * all and leave every row reading the model's name back at you.
     */
    const animationModel = useMemo(() => {
        const hull = scene?.parts.find(part => part.origin === 'Hull') ?? scene?.parts[0];

        return (hull?.modelRef ?? '').replace(/\.[^.]*$/, '');
    }, [scene?.parts]);

    const animationGroups = useMemo(
        () => groupAnimations(animationModel, animations), [animationModel, animations]);

    /**
     * Every clip in the order the library shows them, which is the order the track buttons step
     * through. Grouped order, not file order - stepping past the end of Idle should land on the
     * first Movement clip, the way the eye reads the list.
     */
    const orderedClips = animationGroups.flatMap(group => group.items.map(item => item.name));
    const clipAt = animation === null ? -1 : orderedClips.indexOf(animation);

    /**
     * Whether the clip is actually advancing, taken from the mixer rather than from the panel.
     *
     * A clip that runs off its end without looping is stopped, but nothing told the panel: the
     * pause button went on showing a pause glyph over a model standing perfectly still, and
     * pressing it "paused" something that had already finished. The mixer knows; the panel asks.
     */
    const playing = playhead.running;
    const atEnd = playhead.duration > 0 && playhead.time >= playhead.duration - 1e-4;

    /** Play, resume, or replay - a media player's one button does all three. */
    const togglePlay = (): void => {
        if (playing) {
            setAnimationPaused(true);
            return;
        }

        // Pressing play on a finished clip replays it. Resuming from the last frame would finish
        // again the same instant, which reads as a dead button.
        if (atEnd && !animationLoop) {
            viewportRef.current?.seekAnimation(0);
            setPlayhead(current => ({ ...current, time: 0 }));
        }

        // Straight at the viewport, not only through the state. A clip that ran off its end was
        // paused by the MIXER, not by the reader, so `animationPaused` was already false - setting
        // it to false again changes nothing, runs no effect, and the button does nothing at all.
        viewportRef.current?.setPaused(false);
        setAnimationPaused(false);
    };

    const step = (by: number): void => {
        if (orderedClips.length === 0) {
            return;
        }

        // Wraps, so the buttons never dead-end at the top or bottom of the list.
        const next = (clipAt + by + orderedClips.length) % orderedClips.length;
        setAnimation(orderedClips[clipAt < 0 ? (by > 0 ? 0 : orderedClips.length - 1) : next]);
        setAnimationPaused(false);
    };

    const seek = (seconds: number): void => {
        viewportRef.current?.seekAnimation(seconds);
        setPlayhead(current => ({ ...current, time: seconds }));
    };


    /**
     * The systems playing right now, by scene id.
     *
     * Taken from the tree items, which the viewport rebuilds after every change - so the group
     * switches and the tree rows are reading the same snapshot and cannot drift apart.
     */
    const shownSystems = useMemo(() => new Set(treeItems
        .filter(item => item.visible && item.systemId !== undefined)
        .map(item => item.systemId!)), [treeItems]);

    const treeRows = visibleTreeRows(
        filterTree(buildTree(treeItems), { text: boneFilter, kinds }), collapsed);

    // A flyout is anchored to a row, so it cannot outlive the row being on screen - filtering it
    // away or collapsing its parent leaves a panel pinned to nothing, describing something the
    // reader can no longer see.
    useEffect(() => {
        if (detailsRow !== null && !treeRows.some(row => row.node.id === detailsRow)) {
            setDetailsRow(null);
        }
    }, [detailsRow, treeRows]);

    /**
     * Whether the model has been moved off the state it opened in.
     *
     * Reads the same three things Reset puts back, so the button cannot claim there is nothing to
     * do while something plainly is - and cannot offer to reset a model already at rest.
     */
    const touched = alt !== 0
        || (levels.lod.length > 0 && lod !== levels.lod[levels.lod.length - 1])
        || destroyed.size > 0
        || hiddenEmitters.size > 0
        || (viewportRef.current?.rowOverrideEntries().length ?? 0) > 0;

    /**
     * Puts the model back the way it opened.
     *
     * The OPENING RULES, which are a decision of their own: undamaged, at the highest detail the
     * model defines, and quiet. Not "the defaults" - the highest LOD is the LAST defined one,
     * because Alamo numbers detail the other way up.
     *
     * The room is left alone. The grid, the lights and the backdrop are not part of the model, and
     * someone resetting a hull they have been unpicking for ten minutes has not asked for their
     * lighting back.
     */
    const resetModel = useCallback(() => {
        viewportRef.current?.clearRowOverrides();
        setAlt(0);
        setLod(current => levels.lod.length > 0 ? levels.lod[levels.lod.length - 1] : current);
        setDestroyed(new Set());
        setHiddenEmitters(new Set());
        setTreeItems(viewportRef.current?.treeItems() ?? []);
    }, [levels]);

    /**
     * Applies a visibility change to the clicked row, or to the whole selection it belongs to -
     * and in either case to everything beneath, the way a tree is expected to behave.
     */
    const setRowsVisible = (id: string, visible: boolean): void => {
        const roots = buildTree(treeItems);
        const clicked = toggleTargets(selected, id);

        // `visible` is what the reader is asking FOR - a checkbox's change event carries the
        // state it is moving to, and a button labelled Hide says what it will do. It used to be
        // handed to `toggleRow`, which is told what the reader can SEE and flips it, so every one
        // of these was inverted: the tick set each row to the state it was already in and looked
        // dead, and Hide showed things while Show hid them.
        //
        // Hiding pushes down the subtree; showing forces only the clicked rows on and CLEARS what
        // is under them, so switching a bone back on does not drag its collision hull into view.
        const changes = clicked.flatMap(row => setRow(
            row, withDescendants(roots, [row]).filter(under => under !== row), visible));

        viewportRef.current?.applyRowOverrides(changes);
        setTreeItems(viewportRef.current?.treeItems() ?? []);
    };

    const toggleCollapsed = (index: string): void => setCollapsed(current => {
        const next = new Set(current);
        if (!next.delete(index)) {
            next.add(index);
        }
        return next;
    });

    return (
        <Shell>
            <div className="body">
                <div className="stage-column">
                    <div className="viewport">
                        <canvas ref={canvasRef} />
                        <div className="bone-labels" ref={labelsRef} />

                        {/* The SCENE's settings, not the model's - so they hang off the viewport
                            rather than off the dock that describes the subject. A globe in the
                            stage's own corner says "this is about the room you are looking into";
                            the same controls in the right-hand dock read as properties of the
                            model, and buried three quarters of that dock while open. */}
                        <div className="stage-row stage-top">
                        <span className="stage-slot">

                        {/* Its own plate. The scene settings OPEN something, while the toggles beside
                            them switch something - putting all of it on one bar said they were one
                            group of related switches, which the flyout button is not. */}
                        <div className="stage-chrome">
                            <button
                                className={'icon-btn world-globe' + (worldOpen ? ' active' : '')}
                                aria-expanded={worldOpen}
                                title="Scene settings: backdrop, lights, weather and draw distance"
                                onClick={() => setWorldOpen(open => !open)}
                            >
                                <Icon name="scene" />
                            </button>
                        </div>

                        {/* The grid, the ground and the effects master describe the ROOM the model
                            is standing in - so they belong on the stage rather than in a dock that
                            describes the subject. Their persistence is unchanged: every one was
                            already a Tier 1 viewer setting, and moving a control does not move
                            where its value lives.

                            Icon only. These are the same switch repeated, so the icon is what tells
                            them apart and the words only made the bar long enough to reach the
                            camera. The name leads the tooltip. */}
                        <div className="stage-chrome icon-only">
                            {overlays.map(overlay => (
                                <button
                                    key={overlay.id}
                                    type="button"
                                    className={'icon-btn' + (overlay.on ? ' active' : '')}
                                    aria-pressed={overlay.on}
                                    aria-label={overlay.label}
                                    disabled={overlay.disabled ?? false}
                                    title={`${overlay.label}: ${overlay.title}`}
                                    onClick={() => overlay.set(!overlay.on)}
                                >
                                    <Icon name={overlay.icon} />
                                </button>
                            ))}
                        </div>
                        </span>

                        {/* Where you are looking FROM is a property of the view, not of the model,
                            so it sits on the stage like the scene controls opposite.

                            The four presets read as words rather than pictograms, because no icon
                            says "three-quarter view". They carried a small camera glyph to say what
                            the row was for; the camera BUTTON beside them now says it, and two
                            camera icons in a row said it twice. */}
                        <span className="stage-slot stage-slot-end">
                        <div className="stage-chrome">
                            {PRESETS.map(({ view, label, title }) => (
                                <button
                                    key={view}
                                    type="button"
                                    className={'icon-btn' + (cameraView === view ? ' active' : '')}
                                    aria-pressed={cameraView === view}
                                    title={title}
                                    onClick={() => {
                                        setCameraView(view);
                                        viewportRef.current?.frameAll(view);
                                    }}
                                >
                                    {label}
                                </button>
                            ))}

                            {/* The model's own camera, beside the ones we invented. Always present:
                                87% of models carry none, and an entry that disappears never
                                teaches anyone the other 13% have one. */}
                            {authorCameras.map(entry => (
                                <button
                                    key={entry.id}
                                    type="button"
                                    className={'icon-btn'
                                        + (cameraView === entry.id ? ' active' : '')}
                                    aria-pressed={cameraView === entry.id}
                                    disabled={entry.disabled}
                                    title={entry.title}
                                    onClick={() => {
                                        if (viewportRef.current?.applyModelCamera(entry.label)
                                            === true) {
                                            setCameraView(entry.id);
                                        }
                                    }}
                                >
                                    {entry.label}
                                </button>
                            ))}
                        </div>

                        {/* The mirror of the scene button opposite: the presets are the switches,
                            this OPENS something, and the two are not the same kind of control. Last
                            on the right as the scene is first on the left, so the stage reads
                            outwards from the model in both directions. */}
                        <div className="stage-chrome">
                            <button
                                className={'icon-btn' + (cameraOpen ? ' active' : '')}
                                aria-expanded={cameraOpen}
                                title="Camera: saved shots, what a model opens with, draw distance
                                    and capture"
                                onClick={() => setCameraOpen(open => !open)}
                            >
                                <Icon name="camera" />
                            </button>
                        </div>
                        </span>
                        </div>

                        {worldOpen && (
                            <div
                                className="stage-flyout on-left"
                                role="dialog"
                                ref={worldRef}
                            >
                                <div className="stage-flyout-head">
                                    Scene
                                    <button
                                        className="icon-btn"
                                        title={'Put the backdrop, lights, wind and draw distance '
                                            + 'back to their defaults. The model`s own state is '
                                            + 'left as it is.'}
                                        onClick={resetRoom}
                                    >
                                        <Icon name="reset" />
                                    </button>
                                    <button
                                        className="icon-btn"
                                        title="Close"
                                        onClick={() => setWorldOpen(false)}
                                    >
                                        <Icon name="close" />
                                    </button>
                                </div>
                                <div className="stage-flyout-body">

        <PanelSection
            id="scene.room"
            title="Room"
            collapsed={folded.has('scene.room')}
            onToggle={toggleSection}
        >
        <div className="field">
            <span className="field-label">
                Backdrop
                <InfoBadge>
                    Never lit and never in the depth buffer, so a backdrop cannot be
                    mistaken for part of the model.
                </InfoBadge>
            </span>
            <ModeSelector
                label="Backdrop"
                value={background}
                options={[
                    { id: 'flat', label: 'Flat', title: 'A plain, neutral field' },
                    { id: 'starfield', label: 'Stars', title: 'A starfield' },
                    { id: 'sky', label: 'Sky', title: 'A sky with a horizon' },
                ] satisfies ModeOption<BackgroundKind>[]}
                onSelect={setBackground}
            />
        </div>

        <div className="field">
            <span className="field-label">
                Ground height
                <InfoBadge>
                    Zero is the middle of the track - the plane the model itself stands on. The ends are
                    one subject radius either way, so the control means the same thing on a trooper and
                    on a Star Destroyer. The grid moves with it: it is the ruler for the ground, not for
                    the origin.
                </InfoBadge>
                <span className="section-count">{floorLevel}</span>
            </span>
            {/* The tick marks where the model stands. Zero is always the exact middle, because
                the track is symmetrical by construction - see `groundRange`. */}
            <span className="detent-track">
                <input
                    type="range"
                    min={-ground.limit}
                    max={ground.limit}
                    step={ground.step}
                    value={floorLevel}
                    disabled={!floor}
                    title={floor
                        ? `Where the ground and its grid sit, in model units. The track runs `
                            + `${ground.limit} either side of the origin, which is the middle, `
                            + `and catches there when dragged.`
                        : 'Switch the ground on to place it'}
                    onPointerDown={() => { groundDragging.current = true; }}
                    onPointerUp={() => { groundDragging.current = false; }}
                    onPointerCancel={() => { groundDragging.current = false; }}
                    onKeyDown={() => { groundDragging.current = false; }}
                    onChange={e => setFloorLevel(snapToZero(
                        Number(e.target.value),
                        ground.step,
                        groundDragging.current ? 1 : 0))}
                />
            </span>
        </div>
        </PanelSection>

        <PanelSection
            id="scene.light"
            title="Light"
            collapsed={folded.has('scene.light')}
            onToggle={toggleSection}
        >
        <div className="field">
            <span className="field-label">
                Light
                <InfoBadge>
                    The engine's own rig: a sun that casts the shadow and two fills that
                    do not. All three reach the translated shaders.
                </InfoBadge>
            </span>
            <ModeSelector
                label="Light being edited"
                value={editing}
                options={LIGHT_NAMES.map(name => ({
                    id: name,
                    label: LIGHT_LABELS[name],
                    title: `Edit the ${LIGHT_LABELS[name].toLowerCase()}`,
                }))}
                onSelect={setEditing}
            />
        </div>

        <div className="field">
            <span className="field-label">
                Light around
                <InfoBadge>
                    Turns round the model the way the stage does, from the same zero: at 0 the light
                    stands where you stand in the Front view, and it moves clockwise seen from above.
                </InfoBadge>
                <span className="section-count">
                    {lights[editing].azimuth} deg, from {bearing.from}
                </span>
            </span>
            <input
                type="range"
                min={0}
                max={360}
                step={5}
                value={lights[editing].azimuth}
                onChange={e => setLight(editing, { azimuth: Number(e.target.value) })}
            />
        </div>

        <div className="field">
            <span className="field-label">
                Light height
                <InfoBadge severity={bearing.belowGround ? 'warning' : 'info'}>
                    {bearing.belowGround
                        ? (editing === 'sun'
                            ? 'Under the ground plane. The floor is between the sun and the model, so '
                              + 'it lights the hull from beneath and casts nothing onto the ground.'
                            : 'Under the ground plane, which is where the engine puts both of its own '
                              + 'fills. They cast no shadow, so nothing is hidden by it.')
                        : 'The sun drives the shadow. Straight overhead flattens the hull and hides '
                          + 'the shadow under it; raking is what makes panel lines read.'}
                </InfoBadge>
                <span className="section-count">
                    {lights[editing].elevation} deg, {bearing.height}
                </span>
            </span>
            <input
                type="range"
                min={-90}
                max={90}
                step={5}
                value={lights[editing].elevation}
                onChange={e =>
                    setLight(editing, { elevation: Number(e.target.value) })}
            />
        </div>

        <div className="field">
            <span className="field-label">
                Light colour
                <span className="section-count">
                    {lights[editing].intensity.toFixed(2)}x
                </span>
            </span>
            <span className="view-row">
                <input
                    type="color"
                    value={hexFromColour(lights[editing].colour)}
                    onChange={e =>
                        setLight(editing, { colour: colourFromHex(e.target.value) })}
                />
                <input
                    type="range"
                    min={0}
                    max={4}
                    step={0.05}
                    value={lights[editing].intensity}
                    onChange={e =>
                        setLight(editing, { intensity: Number(e.target.value) })}
                />
            </span>
        </div>

        <div className="field">
            <span className="field-label">
                Ambient
                <InfoBadge>
                    What reaches the parts no light does. Too much and the model reads
                    flat; none at all and its shadowed side is black.
                </InfoBadge>
                <span className="section-count">
                    {lights.ambient.intensity.toFixed(2)}x
                </span>
            </span>
            <span className="view-row">
                <input
                    type="color"
                    value={hexFromColour(lights.ambient.colour)}
                    onChange={e => setLights(current => ({
                        ...current,
                        ambient: {
                            ...current.ambient,
                            colour: colourFromHex(e.target.value),
                        },
                    }))}
                />
                <input
                    type="range"
                    min={0}
                    max={2}
                    step={0.05}
                    value={lights.ambient.intensity}
                    onChange={e => setLights(current => ({
                        ...current,
                        ambient: {
                            ...current.ambient,
                            intensity: Number(e.target.value),
                        },
                    }))}
                />
            </span>
        </div>

        <div className="field">
            <span className="field-label">
                Shadow colour
                <InfoBadge>
                    Alamo`s one shadow colour, and it MULTIPLIES: 0.5 grey means half as bright. In
                    Game mode it tints the stencil volume, so it reaches the hull`s own self-shadowing
                    as well as the ground. In Default mode only the ground can catch it.
                </InfoBadge>
            </span>
            <input
                type="color"
                value={hexFromColour(lights.shadow)}
                disabled={!floor}
                title={floor
                    ? 'What the ground tints a cast shadow'
                    : 'Switch the ground on - with no floor there is nothing to catch a shadow'}
                onChange={e => setLights(current => ({
                    ...current, shadow: colourFromHex(e.target.value),
                }))}
            />
        </div>

        <div className="field">
            <span className="field-label">
                Highlight colour
                <InfoBadge>
                    One global specular for all three lights, as the engine keeps it.
                    Only the translated effect shaders read it.
                </InfoBadge>
            </span>
            <input
                type="color"
                value={hexFromColour(lights.specular)}
                onChange={e => setLights(current => ({
                    ...current, specular: colourFromHex(e.target.value),
                }))}
            />
        </div>
        </PanelSection>

        <PanelSection
            id="scene.weather"
            title="Weather"
            collapsed={folded.has('scene.weather')}
            onToggle={toggleSection}
        >
        <div className="field">
            <span className="field-label">
                Wind from
                <span className="section-count">{wind.heading} deg</span>
            </span>
            <input
                type="range"
                min={0}
                max={360}
                step={5}
                value={wind.heading}
                onChange={e =>
                    setWind(current => ({ ...current, heading: Number(e.target.value) }))}
            />
        </div>

        <div className="field">
            <span className="field-label">
                Wind speed
                <InfoBadge>
                    Bends the trees, leans the grass, and carries the 484 emitters that
                    declare themselves affected by it.
                </InfoBadge>
                <span className="section-count">{wind.speed.toFixed(1)}</span>
            </span>
            <input
                type="range"
                min={0}
                max={20}
                step={0.5}
                value={wind.speed}
                onChange={e =>
                    setWind(current => ({ ...current, speed: Number(e.target.value) }))}
            />
        </div>
        </PanelSection>

        <PanelSection
            id="scene.effects"
            title="Effects"
            collapsed={folded.has('scene.effects')}
            onToggle={toggleSection}
        >
        <div className="field">
            <span className="field-label">
                Heat distortion
                <InfoBadge>
                    Debug draws the heat BUFFER instead of the bent picture - the only way to see which
                    pixels a distortion covers, since its whole effect is a displacement. Red and green
                    carry the direction, blue the strength, and ALL BLACK means nothing on this model is
                    distorting anything. It keeps the buffers alive while it is on.
                </InfoBadge>
            </span>
            <span className="view-row">
                <label className="field-label">
                    <input
                        type="checkbox"
                        checked={heatOn}
                        onChange={e => setHeatOn(e.target.checked)}
                    />
                    Bend the frame
                </label>
                <label className="field-label">
                    <input
                        type="checkbox"
                        checked={heatDebug}
                        disabled={!heatOn}
                        onChange={e => setHeatDebug(e.target.checked)}
                    />
                    Debug
                </label>
            </span>
        </div>

        <div className="field">
            <span className="field-label">
                <label className="field-label">
                    <input
                        type="checkbox"
                        checked={bloom}
                        onChange={e => setBloom(e.target.checked)}
                    />
                    Bloom
                </label>
                <InfoBadge>
                    Off by default, as it is in the game. It runs after the heat pass, so the two
                    compose in that order rather than fighting over the frame.
                </InfoBadge>
            </span>
        </div>
        </PanelSection>

                                </div>
                            </div>
                        )}

                        {/* Where you look FROM. The mirror of the scene panel opposite, down to the
                            corner it hangs in and the reset in its head - the two are the same kind
                            of thing about different halves of the picture. */}
                        {cameraOpen && (
                            <div
                                className="stage-flyout on-right"
                                role="dialog"
                                ref={cameraRef}
                            >
                                <div className="stage-flyout-head">
                                    Camera
                                    <button
                                        className="icon-btn"
                                        title="Close"
                                        onClick={() => setCameraOpen(false)}
                                    >
                                        <Icon name="close" />
                                    </button>
                                </div>
                                <div className="stage-flyout-body">

        <PanelSection
            id="camera.shots"
            title="Saved shots" count={cameraPresets.length}
            collapsed={folded.has('camera.shots')}
            onToggle={toggleSection}
        >
        <div className="field">
            <span className="field-label">
                Camera presets
                <InfoBadge>
                    Saved shots are kept in RADII rather than in game units, so one preset frames a
                    trooper and a Star Destroyer the same way. Copy as Lua works the distance back out
                    for whatever is on screen.
                </InfoBadge>
                <span className="section-count">{cameraPresets.length}</span>
            </span>

            {cameraPresets.map(saved => (
                <span className="view-row" key={saved.id}>
                    <button
                        type="button"
                        className="btn"
                        title={`Frame this subject the way ${saved.name} does: `
                            + `${saved.distance.toFixed(1)} radii out, pitch `
                            + `${Math.round(saved.pitch)}, yaw ${Math.round(saved.yaw)}`}
                        onClick={() => {
                            const viewport = viewportRef.current;
                            if (viewport === null) {
                                return;
                            }

                            const pose = poseFromPreset(saved, viewport.subjectSphere);
                            viewport.applyCameraPose(pose.position, pose.target);
                            setCameraView(null);
                        }}
                    >
                        {saved.name}
                    </button>
                    <button
                        type="button"
                        className="btn"
                        title={'Copy this shot as a Set_Cinematic_Camera_Key line, with the '
                            + 'distance worked out for the subject on screen'}
                        onClick={() => {
                            const viewport = viewportRef.current;
                            if (viewport === null) {
                                return;
                            }

                            void navigator.clipboard.writeText(luaFor(
                                saved, viewport.subjectSphere,
                                sceneRef.current?.kind === 'Object'
                                    ? sceneRef.current.subject
                                    : null));
                        }}
                    >
                        Copy as Lua
                    </button>
                    <button
                        type="button"
                        className="icon-btn"
                        title={`Delete ${saved.name}`}
                        onClick={() => setCameraPresets(
                            current => current.filter(p => p.id !== saved.id))}
                    >
                        <Icon name="remove" />
                    </button>
                </span>
            ))}

            <button
                type="button"
                className="btn"
                onClick={() => {
                    const viewport = viewportRef.current;
                    if (viewport === null) {
                        return;
                    }

                    // Named for where it is in the list rather than prompting: a webview has no
                    // modal worth the name, and a preset is renamed by editing its name field.
                    const saved = presetFromPose(
                        `Shot ${cameraPresets.length + 1}`,
                        viewport.cameraPosition, viewport.subjectSphere);

                    setCameraPresets(current => [...current, saved]);
                }}
            >
                <Icon name="add" />
                Save this shot
            </button>

        </div>

        <div className="field">
            <span className="field-label">
                Opens with
                <InfoBadge severity={bindTargets.length === 0 ? 'warning' : 'info'}>
                    {bindTargets.length === 0
                        ? 'A model opened on its own has no object, type or category to bind to. Open '
                          + 'it through a game object to bind a shot to a whole roster.'
                        : 'A bound shot is applied when the subject opens, most specific rule first: '
                          + 'this object beats its type, and its type beats a category. Moving the '
                          + 'camera afterwards is always allowed.'}
                </InfoBadge>
                <span className="section-count">
                    {boundRule === null
                        ? 'nothing'
                        : cameraPresets.find(p => p.id === boundRule.presetId)?.name ?? 'a lost shot'}
                </span>
            </span>

            {/* Bind the LAST saved shot: with no preset there is nothing to bind, and picking
                which one belongs in a list rather than in three buttons. */}
            {bindTargets.map(target => (
                <button
                    key={target.kind}
                    type="button"
                    className="btn"
                    disabled={cameraPresets.length === 0}
                    title={cameraPresets.length === 0
                        ? 'Save a shot first - there is nothing to bind yet'
                        : `Open every ${target.what} with ${
                            cameraPresets[cameraPresets.length - 1].name}`}
                    onClick={() => {
                        const preset = cameraPresets[cameraPresets.length - 1];
                        setCameraBindings(current => [
                            // Replacing rather than appending: two rules of one kind on the same
                            // value would leave the second permanently unreachable, since the first
                            // match wins.
                            ...current.filter(b => !(b.kind === target.kind
                                && b.value.toLowerCase() === target.value.toLowerCase())),
                            {
                                id: `bind-${Date.now().toString(36)}-${target.kind}`,
                                kind: target.kind,
                                value: target.value,
                                presetId: preset.id,
                            },
                        ]);
                    }}
                >
                    {target.label}
                </button>
            ))}

            {boundRule !== null && (
                <button
                    type="button"
                    className="btn"
                    title={`Stop opening ${boundRule.kind === 'object' ? 'this object' : `every ${
                        boundRule.value}`} with a saved shot`}
                    onClick={() => setCameraBindings(
                        current => current.filter(b => b.id !== boundRule.id))}
                >
                    <Icon name="remove" />
                    Unbind
                </button>
            )}

        </div>
        </PanelSection>

        <PanelSection
            id="camera.view"
            title="View"
            collapsed={folded.has('camera.view')}
            onToggle={toggleSection}
        >
        <div className="field">
            <span className="field-label">
                Draw distance
                <InfoBadge>
                    The fit already reaches the end of the effects, not just the hull.
                    Reach further for a long trail; a huge model may z-fight if you do.
                </InfoBadge>
            </span>
            <ModeSelector
                label="Draw distance"
                value={String(drawDistance)}
                options={[
                    { id: '1', label: 'Fit', title: 'Fit the model and its effects' },
                    { id: '2', label: '2x', title: 'Twice the fitted distance' },
                    { id: '4', label: '4x', title: 'Four times' },
                    { id: '8', label: '8x', title: 'Eight times' },
                ]}
                onSelect={value => setDrawDistance(Number(value))}
            />
        </div>
        </PanelSection>

        <PanelSection
            id="camera.capture"
            title="Capture"
            collapsed={folded.has('camera.capture')}
            onToggle={toggleSection}
        >
        <div className="field">
            <span className="field-label">
                Capture
                <InfoBadge>
                    Renders the shot on screen at the chosen size and asks where to put it. Off by
                    default the grid, the floor, the effects and every annotation stay out of it, which
                    is what an icon wants - tick the box to capture the room exactly as you see it.
                </InfoBadge>
                <span className="section-count">{captureSizePx}px</span>
            </span>

            <ModeSelector
                label="Capture size"
                value={String(captureSizePx)}
                options={CAPTURE_SIZES.map(size => ({
                    id: String(size),
                    label: String(size),
                    title: `Write a ${size} by ${size} image`,
                }))}
                onSelect={id => setCaptureSizePx(Number(id))}
            />

            <label className="check-row">
                <input
                    type="checkbox"
                    checked={captureTransparent}
                    onChange={e => setCaptureTransparent(e.target.checked)}
                />
                Transparent background
            </label>

            <label className="check-row">
                <input
                    type="checkbox"
                    checked={captureScenery}
                    onChange={e => setCaptureScenery(e.target.checked)}
                />
                Keep the grid, floor, effects and markers
            </label>

            <button
                type="button"
                className="btn"
                onClick={() => {
                    const viewport = viewportRef.current;
                    if (viewport === null) {
                        return;
                    }

                    const size = captureSize(captureSizePx, captureSizePx);
                    const dataUrl = viewport.capture({
                        ...size,
                        // The reader's own backdrop when they asked to keep the room, so what they
                        // see is what they get; otherwise nothing at all, which is what an icon
                        // going onto a mega texture wants.
                        // The viewport's own backdrop when a flat one is wanted, so an opaque
                        // capture matches what is on screen rather than inventing a colour.
                        background: captureTransparent
                            ? null
                            : `#${VIEWPORT_BACKGROUND.toString(16).padStart(6, '0')}`,
                        grid: captureScenery && grid,
                        floor: captureScenery && floor,
                        particles: captureScenery && particlesOn,
                        annotations: captureScenery,
                    });

                    vscode.postMessage({
                        type: 'saveCapture',
                        dataUrl,
                        fileName: captureFileName(scene?.subject ?? 'capture', size.width),
                    });
                }}
            >
                <Icon name="capture" />
                Save a picture
            </button>

        </div>
        </PanelSection>

                                </div>
                            </div>
                        )}

                        {/* The faction palette, centred under the model.

                            The colour IS the button - a swatch says what it will do far better
                            than its faction's name does, and the name is one hover away. The
                            custom well is always the RIGHTMOST, so the one control that is not a
                            faction never moves as the roster changes length.

                            Not gated on the lens. It is stage chrome now, and a corner that
                            empties when you change lens makes the stage feel like it is coming
                            apart. Whether a hull is tinted matters while destroying hardpoints
                            just as much as while reading its meshes. */}
                        <div className="stage-row stage-bottom">
                        {(scene?.factions.length ?? 0) > 0 && (
                            <div
                                className="stage-chrome faction-palette"
                                role="group"
                                aria-label="Faction colour"
                            >
                                <button
                                    type="button"
                                    className={'swatch swatch-plain'
                                        + (faction === '' && customColour === null ? ' active' : '')}
                                    title="The model`s own colours, with no team tint"
                                    onClick={() => { setFaction(''); setCustomColour(null); }}
                                >
                                    <Icon name="damage" />
                                </button>

                                {scene?.factions.map(entry => (
                                    <button
                                        key={entry.name}
                                        type="button"
                                        className={'swatch' + (
                                            faction === entry.name && customColour === null
                                                ? ' active'
                                                : '')}
                                        style={{
                                            background: toHex(entry.color ?? NEUTRAL_COLOUR),
                                        }}
                                        title={entry.name}
                                        onClick={() => {
                                            setFaction(entry.name);
                                            // Picking a faction drops a custom colour, or the strip
                                            // would highlight one and the hull show another.
                                            setCustomColour(null);
                                        }}
                                    />
                                ))}

                                <input
                                    type="color"
                                    className={'swatch swatch-custom'
                                        + (customColour !== null ? ' active' : '')}
                                    value={customColour ?? toHex(
                                        scene?.factions.find(f => f.name === faction)?.color
                                        ?? NEUTRAL_COLOUR)}
                                    title="A colour of your own, for trying one out before writing
                                        it into Factions.xml"
                                    onChange={e => setCustomColour(e.target.value)}
                                />
                            </div>
                        )}

                        {/* Which renderer draws the model - one or the other, never both, so a
                            segmented control rather than a tick box. The sub-mesh tally lives on
                            the segment it describes, where hovering asks the question. */}
                        {scene !== null && scene.kind !== 'Particle' && (
                            <div className="stage-chrome shader-corner">
                                <ModeSelector
                                    label="Which renderer draws the model"
                                    value={translatedOn && anyShaderSource ? 'game' : 'default'}
                                    options={[
                                        {
                                            id: 'default',
                                            label: 'Default',
                                            title: 'Every sub-mesh drawn with the archetype reading '
                                                + 'of its shader name. Never wrong about geometry - '
                                                + 'only about how a surface is lit.',
                                        },
                                        {
                                            id: 'game',
                                            label: 'Game',
                                            // Offered and disabled rather than hidden: without it
                                            // nobody learns the mode exists, and the row would
                                            // change width the moment sources appeared.
                                            disabled: !anyShaderSource,
                                            title: anyShaderSource
                                                ? 'The model`s own effect shaders, translated. '
                                                    + translatedCount.translated + ' of '
                                                    + translatedCount.total + ' sub-meshes are '
                                                    + 'drawn this way.'
                                                : 'The model`s own effect shaders. Set the sources '
                                                    + 'up first - there is nothing to translate '
                                                    + 'yet.',
                                        },
                                    ]}
                                    onSelect={id => setTranslatedOn(id === 'game')}
                                />

{/* Always here, in one of two states - never absent.

                                    The setup flow is a command, and a command nobody can find is
                                    not a path out of "nothing translated"; but a button that
                                    appears only while something is wrong is a control the reader
                                    never sees working, and it changed the plate's size underneath
                                    the switch above it. So the slot always says where the sources
                                    stand, and only the wording changes. */}
                                {anyShaderSource ? (
                                    <span className="shader-state">
                                        <Icon name="check" />
                                        Shader sources ready
                                    </span>
                                ) : (
                                    <button
                                        type="button"
                                        className="icon-btn"
                                        title="A game install ships compiled .fxo files. The .fx
                                            sources are a separate download published by
                                            Petroglyph; press to fetch and verify them."
                                        onClick={() =>
                                            vscode.postMessage({ type: 'obtainShaders' })}
                                    >
                                        <Icon name="download" />
                                        Set up shader sources
                                    </button>
                                )}
                            </div>
                        )}
                        </div>

                    </div>

                    {problemsOpen && notices.length > 0 && (
                        <ProblemsPanel
                            className="preview-problems"
                            memoKey="modelPreview"
                            defaultHeight={140}
                            title={`Problems (${notices.length})`}
                            onClose={() => setProblemsOpen(false)}
                        >
                            {notices.map((problem, index) => (
                                <div key={index} className="problem-row">
                                    <span
                                        className={`codicon codicon-${severityIconFor(
                                            problem.severity === 'error' ? 'error' : 'warning')}`}
                                    />
                                    <span className="problem-msg">
                                        {(problem.hardpointId ?? '') === ''
                                            ? problem.message
                                            : `${problem.hardpointId}: ${problem.message}`}
                                    </span>
                                </div>
                            ))}
                        </ProblemsPanel>
                    )}
                </div>

                <RightDock
                    initialWidth={dockWidthMemo}
                    minWidth={200}
                    maxWidth={460}
                    onWidthChange={w => { dockWidthMemo = w; }}
                    header={<>
                        {/* The graph's layout exactly: one button anchored left where its save sits,
                            the dial alone in the centred slot, one anchored right. The preview has
                            nothing to save, so the left slot carries what the model IS - its name
                            and its counts - which frees the centre entirely for the dial. */}
                        <button
                            className={'icon-btn header-left' + (infoOpen ? ' active' : '')}
                            aria-expanded={infoOpen}
                            onClick={() => setInfoOpen(open => !open)}
                            title={scene?.subject ?? 'No model'}
                        ><Icon name="details" /></button>

                        {/* A flyout, anchored under the button that opens it and pointing back at
                            it: the model's name and its counts are something you check, not
                            something you work with, so they float over the panel rather than
                            taking a block of it. The header is `position: relative` and clips
                            nothing, which is what lets this hang below it. */}
                        {infoOpen && (
                            <div className="model-info" role="dialog" ref={infoRef}>
                                <div className="model-info-name" title={scene?.subject}>
                                    {scene?.subject ?? 'No model'}
                                </div>
                                {stats === null ? (
                                    <div className="dock-hint">Nothing loaded yet.</div>
                                ) : (
                                    <>
                                        <div className="stat-row">
                                            <span>Parts</span>
                                            <span className="value">{stats.parts}</span>
                                        </div>
                                        <div className="stat-row">
                                            <span>Meshes</span>
                                            <span className="value">{stats.meshes}</span>
                                        </div>
                                        <div className="stat-row">
                                            <span>Triangles</span>
                                            <span className="value">
                                                {stats.triangles.toLocaleString()}
                                            </span>
                                        </div>
                                        <div className="stat-row">
                                            <span>Bones</span>
                                            <span className="value">{stats.bones}</span>
                                        </div>

                                        {/* Only when the file carries some, and said plainly: they
                                            are 3ds Max export residue. No shipped effect declares a
                                            point or spot light uniform that could consume one, so
                                            neither the engine nor this preview lights anything with
                                            them - and an author who finds them in their file
                                            deserves to be told that rather than left hunting for a
                                            light that never turns on. */}
                                        {(modelDetail?.lightCount ?? 0) > 0 && (
                                            <div
                                                className="stat-row"
                                                title="Lights baked into the model by the exporter.
                                                    The engine ignores them - it lights everything
                                                    with the three scene directionals - so they are
                                                    listed here and never drawn."
                                            >
                                                <span>Lights (ignored)</span>
                                                <span className="value">
                                                    {modelDetail?.lightCount}
                                                </span>
                                            </div>
                                        )}
                                    </>
                                )}
                            </div>
                        )}

                        {/* A particle file gets no dial: it has no meshes, no clips and no
                            hardpoints, so every other lens would be empty. */}
                        {scene !== null && scene.kind !== 'Particle' && (
                            <span className="header-modes">
                                <RotaryModeSwitch
                                    mode={mode}
                                    modes={previewModes({
                                        animations: animations.length,
                                        hardpoints: scene.hardpoints.length,
                                        particles: scene.particles.length,
                                    })}
                                    onSelect={setMode}
                                />

                                {/* Hand-off, not view controls: this preview is read-only by design
                                    and these are where editing happens. Under the dial rather than
                                    in a corner, because they are about the subject it names. */}
                                {scene.kind === 'Model' && (
                                    <span className="header-handoff">
                                        <button
                                            className="icon-btn"
                                            title="Open this model in AloViewer"
                                            onClick={() => vscode.postMessage(
                                                { type: 'openExternal', tool: 'model' })}
                                        >
                                            <Icon name="open" />
                                        </button>
                                        <button
                                            className="icon-btn"
                                            title="Open this file in the Particle Editor"
                                            onClick={() => vscode.postMessage(
                                                { type: 'openExternal', tool: 'particles' })}
                                        >
                                            <Icon name="bloom" />
                                        </button>
                                    </span>
                                )}
                            </span>
                        )}

                        <button
                            className={`icon-btn validate-btn header-right sev-${severity}`}
                            disabled={notices.length === 0}
                            aria-expanded={problemsOpen}
                            onClick={() => setProblemsOpen(open => !open)}
                            title={notices.length === 0
                                ? 'Nothing to report about this model.'
                                : `${notices.length} ${problemWord} about this model. `
                                  + `Press to ${problemsOpen ? 'hide' : 'read'} them.`}
                        >
                            <span className={`codicon codicon-${severityIconFor(severity)}`} />
                            {notices.length > 0 ? ` ${notices.length}` : ''}
                        </button>
                    </>}
                    content={<>
                        {mode === 'model' && treeItems.length > 0 && (
                            <div className="dock-section">
                                <div className="dock-section-title">
                                    Model tree
                                    <span className="section-count">
                                        {selected.size > 0
                                            ? `${selected.size} selected`
                                            : treeRows.length}
                                    </span>
                                </div>

                                <ul className="bone-tree">
                                    {treeRows.map(({ node, expandable, expanded }) => (
                                        <li
                                            key={node.id}
                                            data-row={node.id}
                                            className={
                                                'bone-row'
                                                + (selected.has(node.id) ? ' selected' : '')
                                                + (node.gatedOff ? ' hidden-bone' : '')
                                            }
                                            style={{ paddingLeft: 6 + node.depth * 10 }}
                                            title={node.because === undefined
                                                ? node.name
                                                : `${node.name}
${becauseText(node.because)}`}
                                            onClick={event => {
                                                const after = selectionAfterClick(
                                                    treeRows, selected, anchor, node.id, {
                                                        ctrl: event.ctrlKey || event.metaKey,
                                                        shift: event.shiftKey,
                                                    });
                                                setSelected(after.selected);
                                                setAnchor(after.anchor);

                                                if (node.kind === 'bone') {
                                                    setSelectedBone(Number(node.id.slice(5)));
                                                }
                                            }}
                                        >
                                            <span
                                                className={`twisty${expandable ? '' : ' leaf'}`}
                                                onClick={event => {
                                                    event.stopPropagation();
                                                    toggleCollapsed(node.id);
                                                }}
                                            >
                                                <Icon
                                                    name={expanded ? 'expanded' : 'collapsed'}
                                                    size={13}
                                                />
                                            </span>
                                            <input
                                                type="checkbox"
                                                className="row-visible"
                                                checked={node.visible}
                                                title="Show or hide this, and anything else selected"
                                                onClick={event => event.stopPropagation()}
                                                onChange={
                                                    e => setRowsVisible(node.id, e.target.checked)}
                                            />
                                            {/* The kind reads as a PICTURE now. A letter needed
                                                the legend in the filter strip to decode, on every
                                                row of a hundred-row tree. */}
                                            <span
                                                className={`row-kind kind-${node.kind}`}
                                                title={KIND_LABELS[node.kind]}
                                            >
                                                <Icon name={KIND_ICONS[node.kind]} size={13} />
                                            </span>
                                            <span className="bone-name">{node.name}</span>

                                            {/* Dim until the row is under the pointer or already
                                                open, so a deep tree stays a list of names rather
                                                than a column of buttons - but always THERE, since
                                                a control that only exists on hover is one nobody
                                                finds. */}
                                            <button
                                                type="button"
                                                className={'icon-btn row-details'
                                                    + (detailsRow === node.id ? ' active' : '')}
                                                aria-expanded={detailsRow === node.id}
                                                title={`What ${node.name} is made of`}
                                                onClick={event => {
                                                    event.stopPropagation();
                                                    setDetailsRow(open =>
                                                        open === node.id ? null : node.id);
                                                }}
                                            ><Icon name="details" size={13} /></button>
                                        </li>
                                    ))}
                                </ul>
                                {treeRows.length === 0 && (
                                    <div className="dock-hint">Nothing matches that filter.</div>
                                )}
                                {/* Show and Hide used to sit here, doing what the row's own tick
                                    does - and doing it to whatever happened to be selected, which
                                    is a second way to say the same thing and a second thing to get
                                    wrong. What the panel actually lacked was a way BACK: after
                                    twenty ticks, a damage level and a detail level, nothing said
                                    what the model looked like when it was opened. */}
                                {treeRows.length > 0 && (
                                    <div className="selection-actions">
                                        <button
                                            type="button"
                                            className="icon-btn"
                                            disabled={!touched}
                                            title={touched
                                                ? 'Put the model back the way it opened: every tick '
                                                    + 'released, undamaged, at full detail'
                                                : 'The model is as it opened'}
                                            onClick={resetModel}
                                        >
                                            <Icon name="reset" />
                                            Reset
                                        </button>
                                    </div>
                                )}


                                {/* The controls sit UNDER the list they act on.

                                    Above it they were the first thing in the panel and the
                                    tree was pushed below the fold - so the section opened on
                                    three rows of settings and none of the model. The list is
                                    what this section is; the filter, the kinds and the
                                    skeleton are how you narrow it, and they read as such
                                    once they follow it. */}
                                {/* A mesh origin IS a bone and an emitter attaches to one, so all
                                    three belong in the same tree; this narrows it rather than
                                    splitting them across panels. */}
                                <input
                                    className="tree-filter"
                                    type="text"
                                    value={boneFilter}
                                    placeholder="Filter the tree, e.g. HP_"
                                    onChange={e => setBoneFilter(e.target.value)}
                                    title="Matches keep their parents, so a match stays reachable in
                                        the tree. The children of a match are hidden."
                                />

                                <div className="kind-filter mode-group">
                                    {TREE_KINDS.map(kind => (
                                        <button
                                            key={kind}
                                            type="button"
                                            className={`icon-btn${kinds.has(kind) ? ' active' : ''}`}
                                            title={`Show ${KIND_LABELS[kind].toLowerCase()}`}
                                            aria-pressed={kinds.has(kind)}
                                            onClick={() => setKinds(current => {
                                                const next = new Set(current);
                                                if (!next.delete(kind)) {
                                                    next.add(kind);
                                                }
                                                return next;
                                            })}
                                        >
                                            {KIND_LABELS[kind]}
                                        </button>
                                    ))}
                                </div>
                                {/* The skeleton belongs to the TREE, not to a strip of scene
                                    overlays: it is the thing the tree is a list of. One control
                                    rather than two, because "draw the skeleton" and "label its
                                    bones" were never independent - nobody wants labels floating
                                    with no skeleton under them, and the old pair let you ask for
                                    exactly that. */}
                                <div className="field skeleton-field">
                                    <span className="field-label">Skeleton</span>
                                    <ModeSelector
                                        label="How the skeleton is drawn"
                                        value={skeletonMode}
                                        options={[
                                            {
                                                id: 'off', label: 'Off',
                                                title: 'The model alone',
                                            },
                                            {
                                                id: 'selected', label: 'Selected',
                                                title: 'The skeleton, with only the picked bone '
                                                    + 'named',
                                            },
                                            {
                                                id: 'always', label: 'Always',
                                                title: 'The skeleton with every bone named. '
                                                    + 'Unreadable on a large hull - overlapping '
                                                    + 'labels are dropped nearest-first, and the '
                                                    + 'selected bone always keeps its.',
                                            },
                                        ]}
                                        onSelect={setSkeletonMode}
                                    />
                                </div>

                            </div>
                        )}

                        {/* Bulk switches over the same systems the tree lists one by one - not a
                            second tree. Each row reads its state back OUT of those systems, so a
                            group goes half-lit the moment one of its members is switched off in the
                            tree, and there is only ever one answer to "is this effect playing".

                            Groups overlap on purpose: an effect is both "hardpoint damage" and
                            "p_hp_imperial_damage". A tree would have made someone choose. */}
                        {mode === 'model' && groups.length > 0 && (
                            <div className="dock-section">
                                <div className="dock-section-title">
                                    Effect groups
                                    <span className="section-count">{groups.length}</span>
                                </div>
                                <ul className="part-list">
                                    {groups.map(group => {
                                        const state = groupState(group, shownSystems);

                                        return (
                                            <li key={group.id}>
                                                <label className="field-label">
                                                    <input
                                                        type="checkbox"
                                                        checked={state === 'all'}
                                                        ref={box => {
                                                            // A split group is neither on nor off,
                                                            // and only the DOM property can say so.
                                                            if (box !== null) {
                                                                box.indeterminate = state === 'some';
                                                            }
                                                        }}
                                                        title={GROUP_STATE_TITLES[state]}
                                                        onChange={
                                                            e => toggleGroup(group, e.target.checked)}
                                                    />
                                                    {group.label}
                                                    <span className="section-count">
                                                        x{group.ids.length}
                                                    </span>
                                                </label>
                                            </li>
                                        );
                                    })}
                                </ul>
                            </div>
                        )}

                        {emitters.length > 0 && (
                            <div className="dock-section">
                                <div className="dock-section-title">
                                    Emitters
                                    <span className="section-count">{emitters.length}</span>
                                </div>
                                <ul className="part-list">
                                    {emitters.map((name, index) => (
                                        <li key={index}>
                                            <label className="field-label">
                                                <input
                                                    type="checkbox"
                                                    checked={!hiddenEmitters.has(index)}
                                                    onChange={e => toggleEmitter(
                                                        scene?.subject ?? '', index, e.target.checked)}
                                                />
                                                {name}
                                            </label>
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        )}


                        {mode === 'animation' && (
                            <div className="dock-section">
                                <div className="dock-section-title">
                                    Animations
                                    <span className="section-count">{animations.length}</span>
                                </div>

                                {animations.length === 0 ? (
                                    <div className="field-note">
                                        This subject carries no animations. An .ala beside the model
                                        only counts when its skeleton matches.
                                    </div>
                                ) : (
                                    <div className="anim-list">
                                        {/* A dropdown made every clip but one invisible, and hid
                                            the one thing a modder wants from an animation list:
                                            what KINDS of motion this unit has. A wide row you press
                                            to play says both, and a Star Destroyer's twelve clips
                                            cost twelve lines rather than twelve clicks. */}
                                        <button
                                            type="button"
                                            className={'anim-row' + (animation === null
                                                ? ' active'
                                                : '')}
                                            title="The model as the file stores it, with nothing
                                                driving the skeleton"
                                            onClick={() => setAnimation(null)}
                                        >
                                            <Icon name="stop" />
                                            <span className="anim-name">Rest pose</span>
                                        </button>

                                        {animationGroups.map(group => (
                                            <Fragment key={group.family}>
                                                <div className="anim-group">{group.label}</div>
                                                {group.items.map(item => (
                                                    <button
                                                        key={item.name}
                                                        type="button"
                                                        className={'anim-row'
                                                            + (animation === item.name
                                                                ? ' active'
                                                                : '')}
                                                        title={item.name}
                                                        onClick={() => setAnimation(item.name)}
                                                    >
                                                        <Icon name={animation === item.name
                                                            ? 'pause'
                                                            : 'play'} />
                                                        <span className="anim-name">
                                                            {item.label}
                                                        </span>
                                                    </button>
                                                ))}
                                            </Fragment>
                                        ))}
                                    </div>
                                )}
                            </div>
                        )}

                        {mode === 'gameplay' && (scene?.hardpoints.length ?? 0) === 0 && (
                            <div className="dock-section">
                                <div className="dock-section-title">Gameplay</div>
                                <div className="field-note">
                                    This subject declares no hardpoints, so there is nothing to
                                    destroy and no damage effects to drive.
                                </div>
                            </div>
                        )}

                        {mode === 'gameplay' && (scene?.hardpoints.length ?? 0) > 0 && (
                            <div className="dock-section">
                                <div className="dock-section-title">
                                    Hardpoints
                                    <span className="section-count">{scene?.hardpoints.length}</span>
                                </div>

                                {(scene?.hardpoints.some(h => h.isDestroyable) ?? false) && (
                                    <span className="view-row">
                                        <button
                                            className="btn"
                                            title="Blow every destroyable mount off at once"
                                            onClick={() => destroyAll(true)}
                                        >
                                            <Icon name="effects" />
                                            Destroy all
                                        </button>
                                        <button
                                            className="btn"
                                            title="Put every mount back and clear the damage"
                                            onClick={() => destroyAll(false)}
                                        >
                                            <Icon name="settings" />
                                            Repair all
                                        </button>
                                    </span>
                                )}

                                <ul className="part-list">
                                    {scene?.hardpoints.map(hardpoint => (
                                        <li key={hardpoint.id} title={hardpoint.type ?? undefined}>
                                            <label className="field-label">
                                                <input
                                                    type="checkbox"
                                                    checked={destroyed.has(hardpoint.id)}
                                                    disabled={!hardpoint.isDestroyable}
                                                    onChange={e => setHardpointDestroyed(
                                                        hardpoint.id, e.target.checked)}
                                                    title={hardpoint.isDestroyable
                                                        ? 'Destroyed'
                                                        : 'Is_Destroyable is off, so the game never '
                                                          + 'lets this one be shot away'}
                                                />
                                                <span className="part-name">{hardpoint.id}</span>
                                            </label>
                                            <span className="detail">
                                                {hardpoint.attachBone ?? 'no bone'}
                                                {hardpoint.health !== null
                                                    && hardpoint.health !== undefined
                                                    && ` - ${hardpoint.health} hp`}
                                                {!hardpoint.isDestroyable && ' - indestructible'}
                                            </span>
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        )}
                    </>}
                    overview={<>
                        {/* The soft switch's safety net. Leaving a lens does not stop what it
                            started, so each one says in a line what the others are up to - and a
                            chip implies a click, so it takes you there. */}
                        {otherModeChips(mode, {
                            playing: animation,
                            destroyed: destroyed.size,
                            particleSystems: scene?.particles.length ?? 0,
                        }).map(chip => (
                            <button
                                key={chip.mode}
                                className="icon-btn"
                                title={`Switch to ${chip.mode} mode`}
                                onClick={() => setMode(chip.mode)}
                            >
                                {chip.text}
                            </button>
                        ))}

                        {/* Fixed above the viewport controls, and DISABLED rather than removed when no
                            clip is selected. A control group that appears and disappears as you pick
                            things shifts every row under it, so the button you were reaching for is no
                            longer where you looked. It is gated only on whether the subject has clips at
                            all - a fact about the file - so nothing the reader does inside
                            Animation mode ever moves this. Changing LENS is the one thing that
                            does, which is what a lens is for. */}
                        {mode === 'animation' && animations.length > 0 && (
                        <div className="dock-section player-section">
                            <div className="dock-section-title">
                                Playback
                                <span className="section-count">
                                    {animation === null
                                        ? 'no clip'
                                        : actionOf(animationModel, animation)}
                                </span>
                            </div>

                            {/* The layout every media player has had for thirty years, because
                                it needs no explaining: transport on the left, time on the right,
                                the scrub bar across the bottom. */}
                            <div className="player">
                                {/* The order every media player uses: step back through the
                                    list, rewind, play, forward, step on, then the loop latch. */}
                                <div className="player-row">
                                    <button
                                        className="icon-btn"
                                        disabled={orderedClips.length < 2}
                                        title="The clip before this one in the list"
                                        onClick={() => step(-1)}
                                    >
                                        <Icon name="previous" />
                                    </button>
                                    <button
                                        className="icon-btn"
                                        disabled={animation === null}
                                        title="Back to the first frame"
                                        onClick={() => seek(0)}
                                    >
                                        <Icon name="rewind" />
                                    </button>
                                    <button
                                        className="icon-btn"
                                        disabled={animation === null}
                                        title={animation === null
                                            ? 'Pick a clip below to play it'
                                            : playing
                                                ? 'Pause'
                                                : atEnd && !animationLoop
                                                    ? 'Play again from the first frame'
                                                    : 'Play'}
                                        onClick={togglePlay}
                                    >
                                        <Icon name={playing ? 'pause' : 'play'} />
                                    </button>
                                    <button
                                        className="icon-btn"
                                        disabled={animation === null}
                                        title="On to the last frame"
                                        onClick={() => seek(playhead.duration)}
                                    >
                                        <Icon name="play" />
                                    </button>
                                    <button
                                        className="icon-btn"
                                        disabled={orderedClips.length < 2}
                                        title="The clip after this one in the list"
                                        onClick={() => step(1)}
                                    >
                                        <Icon name="next" />
                                    </button>
                                    <button
                                        className={'icon-btn' + (animationLoop ? ' active' : '')}
                                        aria-pressed={animationLoop}
                                        disabled={animation === null}
                                        title={animationLoop
                                            ? 'Looping. Press to play the clip once and hold '
                                                + 'its last frame.'
                                            : 'Playing once and holding the last frame. Press '
                                                + 'to loop.'}
                                        onClick={() => setAnimationLoop(current => !current)}
                                    >
                                        <Icon name="loop" />
                                    </button>

                                    <span className="player-time">
                                        {playhead.time.toFixed(2)}
                                        {' / '}
                                        {playhead.duration.toFixed(2)}s
                                    </span>
                                </div>

                                <input
                                    className="player-scrub"
                                    type="range"
                                    disabled={animation === null}
                                    min={0}
                                    max={Math.max(playhead.duration, 0.001)}
                                    step={0.001}
                                    value={playhead.time}
                                    title="Drag to a frame. The clip keeps running if it was
                                        running, and stays held if it was held."
                                    onPointerDown={() => { scrubbingRef.current = true; }}
                                    onPointerUp={() => { scrubbingRef.current = false; }}
                                    onChange={e => {
                                        const time = Number(e.target.value);
                                        viewportRef.current?.seekAnimation(time);
                                        setPlayhead(current => ({ ...current, time }));
                                    }}
                                />

                                <div className="field">
                                    <span className="field-label">
                                        Speed
                                        <span className="section-count">
                                            {animationSpeed.toFixed(2)}x
                                        </span>
                                    </span>
                                    <input
                                        type="range"
                                        disabled={animation === null}
                                        min={0}
                                        max={2}
                                        step={0.05}
                                        value={animationSpeed}
                                        title="A multiple of the rate the clip`s own file
                                            declares, so 1.00x is the speed the game plays it."
                                        onChange={e =>
                                            setAnimationSpeed(Number(e.target.value))}
                                    />
                                </div>
                            </div>
                        </div>
                        )}

                        {mode === 'model' && (levels.alt.length > 1 || levels.lod.length > 1) && (
                            <>
                                {levels.alt.length > 1 && (
                                    <div className="field">
                                        <span className="field-label">
                                            Damage state
                                            <span className="section-count">ALT {alt}</span>
                                        </span>
                                        <select
                                            value={alt}
                                            onChange={e => setAlt(Number(e.target.value))}
                                            title="Which _ALT level is shown. Only the levels this
                                                model defines are offered - the engine allows ten, but
                                                claiming ten on a model with two says the file holds
                                                something it does not."
                                        >
                                            {levels.alt.map(level => (
                                                <option key={level} value={level}>
                                                    {level === 0 ? '0 (undamaged)' : level}
                                                </option>
                                            ))}
                                        </select>
                                    </div>
                                )}

                                {levels.lod.length > 1 && (
                                    <div className="field">
                                        <span className="field-label">
                                            Detail
                                            <span className="section-count">LOD {lod}</span>
                                        </span>
                                        <select
                                            value={lod}
                                            onChange={e => {
                                                lodChosenRef.current = true;
                                                setLod(Number(e.target.value));
                                            }}
                                            title="Which _LOD level is shown. The engine's numbering
                                                runs the opposite way to most: 0 is the DISTANT,
                                                lowest-detail mesh, and the highest level is the
                                                close-up one. Measured across the shipped models -
                                                Ei_trooper is 282 triangles at LOD0 and 1078 at LOD2."
                                        >
                                            {levels.lod.map(level => (
                                                <option key={level} value={level}>
                                                    {level === 0 ? '0 (distant)' : level}
                                                    {level === levels.lod[levels.lod.length - 1]
                                                        ? ' (close-up)'
                                                        : ''}
                                                </option>
                                            ))}
                                        </select>
                                    </div>
                                )}
                            </>
                        )}

                        {(emitters.length > 0 || groups.length > 0) && mode === 'gameplay' && (
                            <>
                                {/* Effect playback, not model playback - and only in Gameplay,
                                    where the effects are the subject. On the model lens a transport
                                    bar and a speed dial that silently meant "particles only" read
                                    as controls for the model, which they never were. */}
                                <div className="field">
                                    <span className="field-label">Effects</span>
                                    <span className="view-row">
                                        <button
                                            className="btn compact"
                                            title={particlesPaused
                                                ? 'Resume the simulation'
                                                : 'Hold the simulation where it is'}
                                            onClick={() => setParticlesPaused(current => !current)}
                                        >
                                            <Icon name={particlesPaused ? 'play' : 'pause'} />
                                        </button>
                                        <button
                                            className="btn compact"
                                            title="Replay from nothing. A burst emitter's whole point
                                                is its first half second, which cannot be rewound to."
                                            onClick={() => viewportRef.current?.restartParticles()}
                                        >
                                            <Icon name="restart" />
                                        </button>
                                    </span>
                                </div>

                                <div className="field">
                                    <span className="field-label">
                                        Effect speed
                                        <span className="section-count">
                                            {particleSpeed.toFixed(2)}x
                                        </span>
                                    </span>
                                    <input
                                        type="range"
                                        min="0"
                                        max="2"
                                        step="0.05"
                                        value={particleSpeed}
                                        onChange={e => setParticleSpeed(Number(e.target.value))}
                                        title="Particle time only. The model's own animation keeps its
                                            own rate, so a slowed plume can still sit on a moving hull."
                                    />
                                </div>
                            </>
                        )}

                    </>}
                />

                {/* What one row IS, beside the row that asked.

                    It used to be a dock section that filled itself in on every selection - so
                    picking a row to aim the viewport also printed a page of text, and the row you
                    picked was pushed off screen by the description of it. Now it takes a deliberate
                    press on the row's own button, describes exactly that row, and closes. Pinned by
                    measurement rather than by CSS, because the row sits inside a scroller: see
                    `placeDetails`. */}
                {detailsRow !== null && (
                    <div
                        className="details-flyout"
                        role="dialog"
                        ref={detailsRef}
                        style={detailsAt === null
                            // Placed after the first layout pass, which needs it rendered to
                            // measure. Invisible until then rather than briefly in the corner.
                            ? { visibility: 'hidden' }
                            : { left: detailsAt.left, top: detailsAt.top }}
                    >
                        <div className="details-head">
                            <span className="details-title">
                                {inspection?.title ?? 'Details'}
                            </span>
                            {inspection?.subtitle !== undefined && (
                                <span className="details-subtitle" title={inspection.subtitle}>
                                    {inspection.subtitle}
                                </span>
                            )}
                            <button
                                type="button"
                                className="icon-btn"
                                title="Close"
                                onClick={() => setDetailsRow(null)}
                            ><Icon name="close" /></button>
                        </div>

                        <div className="details-body">
                            {inspection === null ? (
                                <div className="field-note">
                                    Nothing to describe - this row is gone from the model.
                                </div>
                            ) : <>
                                {inspection.sections.map(section => (
                                <div className="inspect-group" key={section.title}>
                                    <div className="inspect-title">{section.title}</div>

                                    {section.note !== undefined && (
                                        <div className="field-note">{section.note}</div>
                                    )}

                                    <dl className="inspect-rows">
                                        {section.rows.map((row, at) => (
                                            <Fragment key={`${section.title}:${at}`}>
                                                <dt title={row.hint ?? row.label}>
                                                    {row.label}
                                                </dt>
                                                <dd
                                                    className={row.kind}
                                                    title={row.hint ?? row.value}
                                                >
                                                    {row.swatch !== undefined && (
                                                        <span
                                                            className="inspect-swatch"
                                                            style={{ background: row.swatch }}
                                                        />
                                                    )}
                                                    {row.value}
                                                </dd>
                                            </Fragment>
                                        ))}
                                    </dl>
                                </div>
                                ))}

                                {/* The bulk tables, closed until asked for.

                                    This is the only genuinely large thing the preview can fetch,
                                    and it is read a handful of rows at a time - so it is a
                                    deliberate act with a page size, not something that loads with
                                    the panel. */}
                                {inspectedMesh !== undefined && (
                                    <div className="inspect-group">
                                        <div className="inspect-title">Geometry</div>

                                        <div className="mode-group geometry-tables">
                                            {GEOMETRY_TABLES.map(table => (
                                                <button
                                                    key={table.id}
                                                    type="button"
                                                    className={'icon-btn'
                                                        + (geometry?.table === table.id
                                                            ? ' active' : '')}
                                                    title={table.title}
                                                    aria-pressed={geometry?.table === table.id}
                                                    disabled={inspectedMesh.meshIndex === undefined}
                                                    onClick={() => loadGeometry(table.id, 0)}
                                                >
                                                    {table.label}
                                                </button>
                                            ))}
                                        </div>

                                        {geometryError !== null && (
                                            <div className="field-note">{geometryError}</div>
                                        )}

                                        {geometry !== null && (
                                            <GeometryPage page={geometry} onPage={loadGeometry} />
                                        )}
                                    </div>
                                )}
                            </>}
                        </div>
                    </div>
                )}
            </div>
        </Shell>
    );
}

const S3TC_FORMATS = new Set<number>([
    THREE.RGB_S3TC_DXT1_Format,
    THREE.RGBA_S3TC_DXT1_Format,
    THREE.RGBA_S3TC_DXT3_Format,
    THREE.RGBA_S3TC_DXT5_Format,
]);

/**
 * Applies the addressing mode the game's samplers ask for.
 *
 * The rule itself lives in `preview/textures.ts` so it can be unit tested; this only carries the
 * answer onto the texture.
 */
function applyAddressing(
    texture: THREE.Texture, width: number | undefined, height: number | undefined,
): THREE.Texture {
    if (addressingFor(width, height) === 'repeat') {
        texture.wrapS = THREE.RepeatWrapping;
        texture.wrapT = THREE.RepeatWrapping;
    }

    return texture;
}

/**
 * Turns raw texture bytes into a three.js texture.
 *
 * The server sends the file undecoded, so the format picks the decoder. DDS keeps its compressed
 * blocks all the way to the GPU - decoding it would inflate a 2048x2048 texture several times over
 * for no gain - while TGA has to be unpacked, because no GPU samples it directly.
 */
function decodeTexture(result: GetModelTextureResult): THREE.Texture | null {
    const bytes = Uint8Array.from(atob(result.data ?? ''), c => c.charCodeAt(0));

    if (result.format === 'dds') {
        const parsed = new DDSLoader().parse(bytes.buffer as ArrayBuffer, true);

        // Not every .dds is compressed - the loader hands back a plain pixel format for the
        // uncompressed ones, and a CompressedTexture cannot carry those.
        if (S3TC_FORMATS.has(parsed.format as number)) {
            const texture = new THREE.CompressedTexture(
                parsed.mipmaps as THREE.CompressedTextureMipmap[],
                parsed.width, parsed.height, parsed.format as THREE.CompressedPixelFormat);
            texture.needsUpdate = true;
            return applyAddressing(texture, parsed.width, parsed.height);
        }

        const level = parsed.mipmaps[0] as { data: Uint8Array } | undefined;
        if (level === undefined) {
            return null;
        }

        const texture = new THREE.DataTexture(level.data, parsed.width, parsed.height);
        texture.needsUpdate = true;
        return applyAddressing(texture, parsed.width, parsed.height);
    }

    if (result.format === 'tga') {
        const parsed = new TGALoader().parse(bytes.buffer as ArrayBuffer);

        const texture = new THREE.DataTexture(parsed.data, parsed.width, parsed.height);
        texture.needsUpdate = true;
        // TGA rows run bottom-up, which is also how the game stores them.
        texture.flipY = true;
        return applyAddressing(texture, parsed.width, parsed.height);
    }

    return null;
}

createRoot(document.getElementById('root')!).render(<ModelPreview />);
