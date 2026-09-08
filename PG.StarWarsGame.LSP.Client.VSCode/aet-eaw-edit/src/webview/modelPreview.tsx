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
    GetProjectileResult,
    GetShaderSourceResult, PREVIEW_PARTICLE_GATE,
    ModelDetail, PREVIEW_SCENE_KIND, PreviewParticle,
    PreviewProblem, PreviewRgba, PreviewScene,
} from '../protocol/modelPreview';
import { worstSeverity } from './loc/validateState';
import { collectTextureNames } from './preview/materials';
import { type InspectorSubject } from './preview/inspectorSubject';
import { AssetLedger, loadState, type LoadTally } from './preview/loadProgress';
import { anchorFlyout } from './preview/flyoutAnchor';
import { groundRange, snapToZero } from './preview/groundRange';
import { InfoBadge } from './shared/InfoBadge';
import { Button, IconButton } from './shared/Button';
import { DockSection } from './shared/DockSection';
import { Field } from './shared/Field';
import { Icon, type IconName } from './shared/Icon';
import { addressingFor } from './preview/textures';
import {
    DEFAULT_LIGHT_AZIMUTH_DEGREES, DEFAULT_LIGHT_ELEVATION_DEGREES,
} from './preview/lighting';
import {
    ancestorsOf, type BoneAttachment, type FlatBone, type LabelMode,
} from './preview/skeleton';
import {
    boneIndexOfRow, buildTree, defaultCollapsed, filterTree, selectionAfterClick, toggleSelected,
    toggleTargets,
    visibleTreeRows, withDescendants,
    type TreeItem, type TreeKind,
} from './preview/previewTree';
import { PreviewViewport, type PresetView, type ViewportStats } from './preview/viewport';
import {
    dockBodyCss, dockChromeCss, dockHeaderCss, dockOverviewCss, problemsPanelCss, rightDockCss,
    rotarySwitchCss,
} from './shared/dockChrome';
import { ProblemsPanel } from './shared/ProblemsPanel';
import { affiliationColour, colorizationFor, parseHex, toHex } from './preview/colour';
import { reviewFactionColour, type ColourFinding } from './preview/colourUsability';
import { translateEffect } from './preview/fx/effect';
import { lightBearing } from './preview/lightBearing';
import { cameraPose, cameraViewOptions, modelCameraEntries } from './preview/modelCameras';
import { becauseText, setRow } from './preview/visibility';
import { rowEye, type EyeState } from './preview/rowEye';
import {
    luaFor, poseFromPreset, presetFromPose, type CameraPreset,
} from './preview/cameraPresets';
import { bindingFor, type CameraBinding, type PreviewSubject } from './preview/cameraBindings';
import { CAPTURE_SIZES, captureFileName, captureSize } from './preview/capture';
import { BREAKOFF_ATTACHMENT, VIEWPORT_BACKGROUND } from './preview/viewport';
import { parseFxManifest, selectTechnique } from './preview/fx/fxParser';
import { materialStateFrom } from './preview/fx/renderState';
import {
    collisionMeshNames, decalNames, effectPlaysNow,
    hardpointGateAllows, partHidden,
} from './preview/damage';
import { passiveEffectPlaysNow, passiveEffects } from './preview/passiveSubject';
import { stageForHull,
    levelLabel, levelSteps, withDeclaredStages, type DefinedLevels, type LevelCost,
} from './preview/levels';
import {
    hardpointCards, hardpointFacts, healthBar, muzzleLabel, unitWeaponFacts, unitWeapons,
    weaponFacts,
} from './preview/hardpointCards';
import { groupHardpoints, groupOf } from './preview/hardpointGroups';
import {
    countTreeNodes, searchIsOpen, treeFilterSummary, treeMinHeight,
} from './preview/treeSearch';
import type { PreviewBreakoffProp } from '../protocol/modelPreview';

/**
 * Marks a GLB request as WRECKAGE rather than a scene part.
 *
 * The reply echoes `partId` and nothing else identifying, so the key rides on it. A breakoff added
 * as a part would be framed as if it were the subject and would never be cleaned up.
 */
const BREAKOFF_PART = 'breakoff:';

/**
 * The part id a death clone's model is loaded under.
 *
 * Prefixed like the breakoff props so the GLB handler can tell it from a scene part - the reply
 * carries only the id it was asked with, and a clone added as an ordinary part would be framed as
 * though it were the subject.
 */
const DEATH_CLONE_PART = 'deathclone:';

/** Prefix for the persistent effect a piece of wreckage trails, so it can be stopped by key. */
const WRECK_EFFECT = 'wreckfire:';
import {
    describeGate, groupState, particleGroups, replacedByAbility, type GroupState,
    type ParticleGroup,
} from './preview/particleScene';
import {
    defaultMode, drawsAnnotations, modelTouched, otherModeChips, previewModes, type PreviewMode,
} from './preview/previewMode';
import {
    allWeaponIds, weaponTitle, boneRowIndex, fireBoneTitle, visibleArcs, weaponRows,
    type WeaponRow,
} from './preview/weaponRows';
import {
    reticleFor, reticleMarks, reticleScreenSize, type ReticleState,
} from './preview/reticles';
import {
    DEFAULT_ATTACKER, armorFactor, attackerFromProjectile,
    attackerProjectile as attackerProjectileSpec, damageSwitches, fireBlast, poolRows,
    fullPools, poolsFor, projectileChoices, resolveHit, type Attacker, type Pools,
} from './preview/attacker';
import {
    abilityAllows, abilityBarTitle, abilityClaims, abilityFacts, abilityOwnership, abilityProxies,
    abilityRows, clipFor,
    gotoDefinitionTitle, shieldRevealed, stealthed, unboundEffectIds,
} from './preview/abilityRows';
import { problemLook, problemTag, problemWhere } from './shared/problemLook';
import { cloneForDamage, deathCloneRows, turretSweeps } from './preview/deathClone';
import { spinAwayEnd, spinAwaySummary } from './preview/spinAway';
import { BY_HAND, appendShot, damageLine, type DamageLogEntry } from './preview/damageLog';
import { breakoffAnchor, breakoffFor, type BreakoffAnchor } from './preview/breakoff';
import {
    hullPool, shieldGeneratorsDown, unitDestroyed, unitTargetable,
} from './preview/unitPool';
import { blastVictims, candidatesFrom } from './preview/blast';
import {
    clipNamingModel, groupAnimations, playheadLabel, type AnimationAction,
} from './preview/animationNames';
import { pickTake } from './preview/takeRoulette';
import { AnimationTile } from './preview/AnimationTile';
import { type ChoiceOption } from './shared/choice';
import { ModeSelector } from './shared/ModeSelector';
import { RotaryModeSwitch } from './shared/RotaryModeSwitch';
import { subjectStateFrom, type SubjectState } from './preview/subjectState';
import {
    colourFromHex, hexFromColour, viewerSettingsFrom, DEFAULT_LIGHTS, DEFAULT_VIEWER_SETTINGS,
    LIGHT_LABELS, LIGHT_NAMES, type DirectionalName, type DirectionalSetting, type LightRig,
    type BackgroundKind, type Wind,
} from './preview/viewerSettings';
import { projectSettingsFrom, type AttackerPreset } from './preview/projectSettings';
import { shadowTintReach, type ShadowTintReach } from './preview/shadowVolumePass';
import { RightDock } from './shared/RightDock';

import { initPanelLayout } from './shared/panelLayoutBridge';
import { SeverityTag } from './shared/SeverityTag';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };
const vscode = acquireVsCodeApi();

// Reads the dock and drawer sizes the host seeded into the page, and reports
// every drag back to it. Must run before anything measures itself.
initPanelLayout(vscode);

/** Messages the panel host sends in. */
type HostMessage =
    // `refresh` marks a re-read of the same subject after the tree behind it changed, as
    // opposed to a subject being opened. The difference is what the reader already has on
    // screen and must keep - the camera above all.
    | { type: 'scene'; scene: PreviewScene; refresh?: boolean }
    | {
        type: 'glb'; partId: string; attachToPartId?: string | null; attachBone?: string | null;
        result: GetModelGlbResult;
    }
    | { type: 'texture'; name: string; result: GetModelTextureResult }
    | { type: 'modelDetail'; modelReference: string; result: GetModelDetailResult }
    | { type: 'inspectorClosed' }
    | { type: 'particleSystem'; name: string; result: GetParticleSystemResult }
    | { type: 'shader'; name: string; result: GetShaderSourceResult }
    // The room, and this subject's own state from earlier in the session. Both arrive before the
    // scene does, so nothing visibly snaps into place a frame after the model appears.
    | { type: 'viewerSettings'; settings: unknown; project: unknown; subject: unknown }
    | { type: 'projectile'; name: string; result: GetProjectileResult }
    // Which optional mechanics this preview shows. Only the extension host can read configuration,
    // so it pushes - on open and again whenever the setting changes.
    | { type: 'previewFeatures'; features: unknown };

/** What each kind is called in the filter row. */
const KIND_LABELS: Record<TreeKind, string> = {
    bone: 'Bones',
    mesh: 'Meshes',
    particle: 'Effects',
};

/**
 * What a group's switch will DO, which differs by the state it is in.
 *
 * Only the action. What the group is currently doing is on screen already - the switch is drawn in
 * that state - so saying it again bought nothing and pushed the half that matters to the end.
 */
const GROUP_STATE_TITLES: Record<GroupState, string> = {
    all: 'Switch particle systems off',
    none: 'Switch particle systems on',
    some: 'Switch the rest on',
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

/** The eye a row's state draws. Blender's three - see `preview/rowEye.ts`. */
const EYE_ICONS: Record<EyeState, IconName> = {
    shown: 'visible',
    inherited: 'inherited',
    hidden: 'hidden',
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
 * How long the load may go silent before the cover comes off anyway, in milliseconds.
 *
 * Idle, not total - see the effect that uses it.
 */
const LOAD_IDLE_LIMIT = 8000;

/** Said once when a model loads with no mesh reaching the screen. */
/**
 * What the shadow colour control says about itself, per place the tint can land.
 *
 * A disabled control has to say WHY - and the reason it used to give was wrong as often as it was
 * right: "with no floor there is nothing to catch a shadow" is only true when the stencil pass is
 * not casting either, and the same colour reaches the hull through that pass with no ground at all.
 */
const SHADOW_TINT_TITLE: Record<ShadowTintReach, string> = {
    ground: 'What the ground tints a cast shadow',
    model: 'What this hull tints its own shadowing - switch the ground on for the cast shadow too',
    both: 'What the ground and this hull tint a shadow',
    none: 'Nothing can show it: switch the ground on, or the Game renderer on a model that '
        + 'authors a shadow volume',
};

const NOTHING_DRAWN = 'This model has no visible mesh at the current damage and detail level. '
    + 'Every sub-mesh is either tagged for another level, hidden in the file, or on a hidden bone.';


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
    .header-modes { display: inline-flex; flex-direction: column; align-items: center; gap: var(--space-2); }
    .header-handoff { display: inline-flex; gap: var(--space-6); }

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
        padding: var(--space-8) var(--space-8);
        text-align: left;
        border: var(--space-1) solid var(--vscode-panel-border, #444);
        border-radius: var(--radius-3);
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
        border: var(--space-1) solid var(--vscode-panel-border, #444);
        background: var(--vscode-panel-border, #444);
    }
    .model-info::after {
        top: -4px;
        background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
    }

    .model-info-name {
        font-weight: 600;
        word-break: break-all;
        margin-bottom: var(--space-6);
    }

    /* With the tree, not in the foot: the text box and the kind chips narrow the same list, and
       splitting them across two levels of the dock left the box acting on something out of sight. */
/* Room to breathe. Every control in this section had been tuned down to 1-2px of padding to
       win back vertical space for the tree - which stopped being the trade the moment the tree got
       the top of the panel to itself. Squeezed rows are harder to hit and harder to scan, and the
       saving was a dozen pixels. */
    .dock-section { gap: var(--space-8); }
    /* The tree's search: a magnifier and, next to it, either the kind filters or the box.

       One element rather than two blocks with a gap. border-box on the input because the loose one
       was width 100% with its own padding on top, so it reached 10px PAST the section every other
       control in the panel lines up with - which is a good part of why it read as adrift. */
    /* A hardpoint card. A bordered block rather than a list row, because it carries four kinds of
       thing - a name, the game's own words, its measurements and its switches - and a flat row put
       them all on one line where none of them read. */
    .hardpoint-cards { display: flex; flex-direction: column; gap: var(--space-6); margin: 0 0 var(--space-8); }

    /* A type heading over the cards it gathers. Quieter than the section's own title - this is a
       division INSIDE the list, not another list. */
    .card-group-title {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        width: 100%;
        padding: var(--space-2) var(--space-2);
        border: none;
        border-radius: var(--radius-3);
        background: none;
        cursor: pointer;
        text-align: left;
        margin: var(--space-4) 0 var(--space-4);
        font-size: var(--font-size-11);
        letter-spacing: 0.03em;
        text-transform: uppercase;
        opacity: 0.6;
        color: var(--vscode-descriptionForeground, #999);
    }
    .card-group-title .section-count { margin-left: auto; }
    .card-group-title:hover { background: var(--vscode-list-hoverBackground); opacity: 0.9; }

    /* The hardpoint list's search sits in the flow above the cards rather than floating on them:
       there is no list box here for it to float over, and the section already scrolls as one. */
    .hardpoint-search { position: static; margin: var(--space-2) 0 var(--space-6); padding: 0; background: none;
        border: none; }

    /* What the attacker is aimed at, said rather than chosen. */
    /* Pressed. The one control on the stage that FIRES rather than latching, so it says so for a
       moment and then goes quiet - red because that is what it just did to the model. */
    .icon-btn.as-action.fired {
        background: var(--vscode-charts-red, #f14c4c);
        border-color: var(--vscode-charts-red, #f14c4c);
        color: var(--vscode-button-foreground, #fff);
    }

    /* The log. Monospace, because the numbers are the point and a proportional font makes a
       column of them impossible to compare down. Newest first, so the shot you just fired is the
       line under your eye rather than the one you have to scroll to. */
    .damage-log {
        list-style: none;
        margin: 0 0 var(--space-8);
        padding: 0;
        max-height: 220px;
        overflow-y: auto;
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: var(--font-size-smaller);
    }
    .damage-log li { padding: var(--space-2) var(--space-2); border-bottom: var(--space-1) solid var(--vscode-panel-border, #333); }
    .damage-log li:last-child { border-bottom: none; }
    /* The shot that finished something, and the shot that did nothing: the two lines a reader is
       scanning for. Everything between them is ordinary and stays quiet. */
    .damage-log .log-kill { color: var(--vscode-charts-red, #f14c4c); }
    .damage-log .log-nothing { opacity: 0.55; }

    .fire-target { font-family: var(--vscode-editor-font-family, monospace); opacity: 0.85; }
    /* Written as part-list li and not just hardpoint-card.

       A card IS an li of a part list, and that selector - one class plus an element - OUTWEIGHS a
       bare class. So this rule's own padding and gap never applied at all: every card in the panel
       has been rendering at the list's 3px by 6px with a 1px gap, which is what "the health bar and
       muzzle selectors are smashed together" was. The numbers below are the roomier ones the user
       asked to carry over from the story graph's tiles. */
    .part-list li.hardpoint-card {
        display: flex;
        flex-direction: column;
        /* Lines within a group sit close; the GROUPS are pushed apart below. Not blank rows - just
           enough air that the title block, the bar, the muzzles and the buttons read as four
           things rather than one column of text. */
        gap: var(--space-4);
        padding: var(--space-8) var(--space-8);
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.25));
        border-radius: var(--radius-6);
        cursor: pointer;
    }
    .hardpoint-card:hover { background: var(--vscode-list-hoverBackground); }
    /* The selected card is the one the attacker is aimed at, which is the same state the Fire at
       list and the targeting marks show. One fact, three places, one highlight. */
    .hardpoint-card.selected {
        background: var(--vscode-list-activeSelectionBackground);
        border-color: var(--vscode-focusBorder);
    }
    /* Shot away. Dimmed rather than struck through or removed: it is still a card you can press to
       put back, and a row that vanishes when destroyed takes the way back with it. */
    .hardpoint-card.gone .part-name { opacity: 0.5; text-decoration: line-through; }

    /* A death clone card is a READING, not a control: the card itself does nothing and only the
       jump in its corner does. So it drops the hardpoint card's pointer and its hover, both of
       which promise a click that is not there. The SELECTED highlight stays - it says which clone
       the weapon you have built would actually produce, and that is the section's whole question.

       A unit weapon card is the same: pressing a hardpoint card aims the attacker at it, and a unit
       weapon is not a target, so there is nothing for the card body itself to do. Its buttons are
       still buttons. */
    .clone-card, .weapon-card { cursor: default; }
    .clone-card:not(.selected):hover, .weapon-card:hover { background: none; }

    /* No wreck at all: the object is not defined anywhere, or it declares no tactical model. The
       one thing worth saying on a clone card beyond the mapping itself. */
    .card-unresolved { color: var(--vscode-editorWarning-foreground, #cca700); }

    /* Title line: the name, and the jump pushed to the far corner. */
    .card-head { display: flex; align-items: center; gap: var(--space-6); }
    .card-head .part-name { font-weight: 600; }
    .card-head .goto-definition, .card-head .icon-btn { margin-left: auto; flex-shrink: 0; }

    /* The Type, which is game data rather than prose - it picks the reticle and it is what a reader
       greps their own files for. */
    .card-type { font-family: var(--vscode-editor-font-family, monospace); opacity: 0.75; }

    /* Small, and dimmed when the game would not draw it. The card says Is_Targetable in words as
       well; this is the picture of the same fact. */
    .card-reticle {
        width: 16px;
        height: 16px;
        flex-shrink: 0;
        image-rendering: pixelated;
    }

    /* The pool, as a bar with its own numbers on it. A bar alone cannot be checked against a file
       and a number alone cannot be read at a glance, so it carries both. */
    .card-health {
        position: relative;
        display: block;
        height: 14px;
        border-radius: var(--radius-3);
        overflow: hidden;
        background: var(--vscode-input-background, rgba(128, 128, 128, 0.18));
    }
    .card-health-fill { display: block; height: 100%; transition: width 0.15s linear; }
    .card-health-label {
        position: absolute;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: flex-end;
        padding-right: var(--space-4);
        font-size: var(--font-size-smallest);
        font-variant-numeric: tabular-nums;
        /* Over both the filled and the empty half, so it has to carry its own contrast rather than
           relying on whichever colour happens to be under it. */
        color: var(--vscode-foreground);
        text-shadow: 0 0 3px var(--vscode-editor-background);
    }

    /* One row per fire bone, full width: the slot on the left and the bone it names beside it. They
       were inline pills showing the bone alone, which says where a shot leaves from but not which
       of Fire_Bone_A and _B declared it. */
    .bone-picks { display: flex; flex-direction: column; gap: var(--space-4); }
    .muzzle-row {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        width: 100%;
        padding: var(--space-2) var(--space-6);
        border: var(--space-1) solid transparent;
        border-radius: var(--radius-3);
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.14));
        color: var(--vscode-foreground);
        font-size: var(--font-size-smaller);
        cursor: pointer;
        text-align: left;
    }
    .muzzle-row:hover:not(:disabled) { background: var(--vscode-list-hoverBackground); }
    .muzzle-row.selected {
        border-color: var(--vscode-focusBorder);
        background: var(--vscode-list-activeSelectionBackground);
    }
    .muzzle-row:disabled { opacity: 0.45; cursor: default; }
    .muzzle-slot { flex: 0 0 auto; opacity: 0.75; }
    .muzzle-bone {
        font-family: var(--vscode-editor-font-family, monospace);
        overflow: hidden;
        text-overflow: ellipsis;
    }

    /* The measurements, behind the info button. A definition list because that is what it is - a
       label and a value - and it lines the values up in a column the eye can run down. */
    .card-info {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--space-2) var(--space-8);
        margin: 0;
        padding: var(--space-8) var(--space-8);
        font-size: var(--font-size-smaller);
    }
    .card-info dt { opacity: 0.65; }
    .card-info dd { margin: 0; font-variant-numeric: tabular-nums; }
    .card-info dd:only-child, .card-info dd:not(dt + dd) { grid-column: 1 / -1; opacity: 0.8; }

    /* Pushed to the far end: the three on its left DO something to the model, and this one only
       says what the file holds. */
    .card-actions .card-info-btn { margin-left: auto; }

    /* The gaps that group the card. Each of these starts a new group, so the space goes above it. */
    .hardpoint-card .card-health { margin-top: var(--space-8); }
    .hardpoint-card .bone-picks { margin-top: var(--space-8); }
    .card-actions { margin-top: var(--space-8); }

    .tree-pane { position: relative; }

    /* Drawn ON the list, hard against its bottom-right corner.

       Closed, it is four icons hugging the right. Open, it also claims the left edge of the pane -
       which is the whole reason for floating it: sharing a row with the icons, the field measured
       58px of the 214 a docked panel has, and a bone name does not fit in 58px. Nothing moves when
       it opens, because the icons are anchored to the right edge either way. */
    .tree-search {
        position: absolute;
        right: 6px;
        bottom: 6px;
        z-index: 2;
        display: flex;
        align-items: center;
        justify-content: flex-end;
        /* Wraps, and the plate is anchored by its BOTTOM, so a line that does not fit is added
           above the icons rather than below them. The icons therefore never move whatever the dock
           is doing: wide, this is one row with the field to their left, which is the shape it was
           designed as; narrow, the field takes a line of its own on top. Measured at a docked 214px
           the field got 34px sharing the row - not enough to read a bone name in - and the same
           layout at twice the width gives it 135px, which is. */
        flex-wrap: wrap;
        gap: var(--space-4);
        padding: var(--space-2);
        border-radius: var(--radius-6);
        /* Its own plate. Rows run underneath it, and a transparent strip over a list of names is
           unreadable in both directions. */
        background: color-mix(in srgb,
            var(--vscode-sideBar-background, #181818) 88%, transparent);
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.3));
    }
    .tree-search.open { left: 6px; }
    /* The basis is what decides where it wraps: below this it is not worth having on the row, and
       the whole line goes above the icons instead.

       The editor's own input tokens, declared rather than inherited. Every other input in the
       extension picks these up from the rule VS Code injects into a webview, but this one sits on
       a plate of its own over a list, where an input that merely happens to look right is one host
       stylesheet change away from looking wrong. Rounded to match the plate it stands in. */
    .tree-search .tree-filter {
        flex: 1 1 130px;
        min-width: 0;
        box-sizing: border-box;
        /* The ICONS' height, exactly. Left to its own, the field came out 27px against their 22,
           so opening it grew the row from 30 to 35 - and because the plate is anchored by its
           bottom and its items are centred, the whole icon strip rose 2px under the pointer that
           had just pressed it. Padding goes with it: an input centres its own text once it has a
           height, so the vertical padding was only ever there to make one. */
        height: 22px;
        padding: 0 var(--space-8);
        color: var(--vscode-input-foreground);
        background: var(--vscode-input-background);
        border: var(--space-1) solid var(--vscode-input-border, transparent);
        border-radius: var(--radius-3);
        font-family: inherit;
        font-size: inherit;
    }
    /* Inset, so the ring is drawn ON the field rather than growing it - an outline that adds a
       pixel each side would nudge the icons every time the box took focus. */
    .tree-search .tree-filter:focus {
        outline: var(--space-1) solid var(--vscode-focusBorder);
        outline-offset: -1px;
    }
    .tree-search .tree-filter::placeholder {
        color: var(--vscode-input-placeholderForeground, var(--vscode-descriptionForeground));
    }
    /* The one button that never moves. Everything else in this row is what it opens onto, so it
       keeps its width whichever of the two is showing. */
    .tree-search .tree-search-toggle { flex: 0 0 auto; }
    .tree-search-icons { display: flex; align-items: center; gap: var(--space-4); flex: 0 0 auto; }
    /* The empty message keeps the list's own shape rather than the row's - no hover, no pointer,
       and it must not look like something you can click. */
    .bone-tree .tree-empty { list-style: none; cursor: default; }


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
        gap: var(--space-4);
        padding: var(--space-2);
        border-radius: var(--radius-6);
        background: color-mix(in srgb,
            var(--vscode-editorWidget-background, #202020) 82%, transparent);
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
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
        gap: var(--space-8);
        pointer-events: none;
    }
    .stage-row > * { pointer-events: auto; }
    .stage-row .stage-chrome { position: static; }

    .stage-top { top: 8px; align-items: flex-start; }
    .stage-bottom { bottom: 8px; align-items: flex-end; }

/* BOTH edges are three-slot rows now, and the middle slot is what the row is about: the colour
       above, the ability command bar below.

       The bottom edge used to position two plates absolutely - a centred palette and a corner
       switch - because as a flex row or a grid each squeezed the other, and below about 600px there
       is genuinely not enough space for a centred strip AND a corner plate. That reasoning still
       holds and is why the narrow case below still STACKS rather than compressing anything. What
       changed is the count: the edge carries four groups now, and four absolutely-positioned plates
       cannot be made to meet in the middle at all.

       The container query measures the STAGE rather than the window, which is the thing that
       actually varies - this panel is usually docked beside code. */
    @container (max-width: 700px) {
        .stage-row {
            flex-direction: column;
            align-items: center;
            gap: var(--space-6);
        }
        /* Stacked, a slot is one centred line rather than a third of a row. */
        .stage-slot,
        .stage-slot-end,
        .stage-slot-mid {
            flex: 0 0 auto;
            width: auto;
            max-width: 100%;
            justify-content: center;
        }
        /* Read outwards from the model in both directions: the actions nearest the edge, the
           standing choices furthest from it. Reversing the bottom keeps the attack tool closest to
           the bottom of the stage, where it was before the stack. */
        .stage-bottom { flex-direction: column-reverse; }
    }

/* A COLUMN, which is the one arrangement that cannot squash.

       A wrapping flex row's intrinsic width is its largest item, not the sum - measured every way
       round, this box reported 201px around 276px of children, unchanged by width: max-content,
       min-width: max-content or an inline style, while a hard-coded width took effect. So as a row
       it either clipped "Default | Game" into "Dif..|Ga..." or rendered the button outside its own
       background, depending on how the wrapping was forced.

       Stacked, the intrinsic width IS the widest row and there is nothing left to get wrong.

       NOTE: the plate itself is gone - the switch lives in Scene > Effects now, where .field
       already stacks. Kept as the record of why a segmented control cannot be laid out in a row
       inside a max-width plate, in case anything else is ever put on that edge. */

    /* A readout, not a control - so no hover, no pointer, and quieter than the switch above it. */
    .shader-state {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-4);
        font-size: var(--font-size-smaller);
        opacity: 0.7;
        color: var(--vscode-descriptionForeground, #999);
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
        gap: var(--space-8);
        flex-wrap: wrap;
    }
    .stage-slot-end { justify-content: flex-end; }
    /* The middle slot centres its contents; the two side slots being EQUAL is what centres the
       slot itself. */
    .stage-slot-mid { justify-content: center; }
    .stage-bottom .stage-slot { align-items: flex-end; }

    /* Nothing in a side slot shrinks. Letting the renderer switch compress turned "Default | Game"
       into "Dif..|Ga..." - a control you can neither read nor aim at - while the palette in the
       middle wraps to another row perfectly well and stays centred doing it. */
    .stage-slot .stage-chrome { flex: 0 0 auto; }
    /* A mode selector sizes to its CONTENT on the stage, not to its container.

       NOTE: no backticks anywhere in this comment - the whole block is a template literal, and one
       backtick ends it. The error lands twenty lines away as a TS1443 about module declarations.

       The shared rule is written for the dock, where one of these fills a field: full width,
       segments at flex 1, and an ellipsis so a long label gives way when the panel is narrow. That
       is right there and wrong here. A stage plate is one of several sharing a row and has no width
       of its own to fill, so the dock's rule divided the group into equal 41px segments and clipped
       Front to Fr... and Author to A...

       The same trap the shader switch hit from the other side - see the .shader-corner note above,
       which is all that is left of it. A segmented control on this stage needs its own width, or it
       gets cut in half. */
    .stage-chrome .mode-selector { flex-shrink: 0; width: auto; }
    .stage-chrome .mode-selector button {
        flex: 0 0 auto;
        min-width: max-content;
        overflow: visible;
        text-overflow: clip;
        white-space: nowrap;
    }
    .faction-palette { min-width: 0; justify-content: center; }

    /* A labelled control on a stage plate: the label INLINE and small, not above a full-width
       input the way the dock writes one. ALT and LOD are three letters and the number beside them
       is the whole answer, so the pair fits in the 22px box everything else on the stage uses. */
    .stage-field {
        display: flex;
        align-items: center;
        gap: var(--space-4);
        font-size: var(--font-size-smaller);
        white-space: nowrap;
    }
    .stage-field > span { opacity: 0.7; }
    /* A slider says WHERE you are on the axis but never WHAT that is, so the value beside it says
       so - in the health band the stage covers on the damage axis, in meshes and triangles on the
       detail one, and in words (undamaged, distant, close-up) wherever neither number is known.

       Fixed width so the plate does not resize as the thumb moves, which would shove the ability
       bar sideways mid-drag, and sized per axis rather than shared: 92px holds the damage axis's
       longest, "0 (undamaged)" at 88px, and the detail axis needs 165 for a model with four figures
       of meshes. Measured in this font by the harness probe measure-plate.js; do not guess. */
    .stage-field .stage-value {
        min-width: 92px;
        opacity: 0.85;
        font-variant-numeric: tabular-nums;
    }
    .stage-field.detail .stage-value { min-width: 165px; }
    /* Short enough that two of them plus the renderer fit the left slot on a docked panel. */
    .stage-field input[type=range] { width: 84px; }
    /* The tier count sits inline after its word, so it is sized to the two digits it holds rather
       than stretching like the fields above it. */
    .stage-flyout .tier-count { width: 52px; }

    .stage-chrome select {
        height: 22px;
        font-size: 1em;
        color: var(--vscode-dropdown-foreground);
        background: var(--vscode-dropdown-background);
        border: var(--space-1) solid var(--vscode-dropdown-border, transparent);
        border-radius: var(--radius-3);
        padding: 0 var(--space-4);
        max-width: 150px;
    }

    /* The command bar and the unit's pools, stacked. Two plates rather than one, so a subject with
       no abilities still gets its bars and a bare model gets neither. */
    .command-stack {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-4);
        max-width: 100%;
    }

    /* The command bar. Wraps rather than scrolls: a unit with fourteen abilities is rare and a
       second row is cheap, while a bar that scrolls hides the very thing it exists to show.

       The min-width is the user's: room for a SECOND key whether or not the unit has one, so the
       bar does not resize as you move between units - and so the bars beneath it have a width worth
       drawing. Two 30px keys, the details button, and the gaps between them. */
    .ability-bar {
        flex-wrap: wrap;
        /* The keys pack from the LEFT, so ability one is leftmost whatever the unit has and a
           second one appears beside it. Centred, they slid sideways every time the count changed -
           the bar was the right width and the wrong arrangement. */
        justify-content: flex-start;
        max-width: 100%;
        min-width: 148px;
        box-sizing: border-box;
    }
    /* The reading matter, pinned to the far end. It is the one thing in the bar that is not an
       ability, so it holds still while the keys grow towards it. */
    .ability-bar > .icon-btn { margin-left: auto; }

    /* What the unit has left. Bars only - the numbers are on the hover and in the attacker's own
       readout, and three labelled rows under the command bar would be a panel rather than a
       glance. */
    .status-bars {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        width: 100%;
        min-width: 148px;
        box-sizing: border-box;
    }
    .status-bar { display: block; width: 100%; }
    .status-bar-track {
        display: block;
        width: 100%;
        height: 5px;
        border-radius: var(--radius-3);
        overflow: hidden;
        background: var(--vscode-editorWidget-background, rgba(128, 128, 128, 0.25));
    }
    /* No transition on the width. A shot empties a pool in one step and the engine's own bars do
       not slide; animating it would show a number the simulation never held. */
    .status-bar-fill { display: block; height: 100%; }

    /* A KEY, sized to the art rather than to a word. The atlas slot is 26x26 and the game draws it
       at that size; blown up, hand-drawn 26px art looks broken rather than large. */
    .stage-chrome .ability-key {
        width: 30px;
        height: 30px;
        padding: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        border-radius: var(--radius-3);
        border: var(--space-1) solid transparent;
        background: var(--vscode-button-secondaryBackground, rgba(255, 255, 255, 0.08));
        color: var(--vscode-button-secondaryForeground, inherit);
        cursor: pointer;
        overflow: hidden;
    }
    .stage-chrome .ability-key:hover:not(:disabled) {
        border-color: var(--vscode-focusBorder, #007fd4);
    }
    /* Active reads as PRESSED IN, not merely highlighted - it is a state you leave running while
       you look at what it did, so it has to survive the pointer moving away. */
    .stage-chrome .ability-key.active {
        background: var(--vscode-button-background, #0e639c);
        color: var(--vscode-button-foreground, #fff);
        border-color: var(--vscode-focusBorder, #007fd4);
    }
    /* An order - it drives nothing on this model. Dimmed rather than absent, and its tooltip says
       why: what a unit CAN do is worth reading even where the answer is "nothing you can see". */
    .stage-chrome .ability-key:disabled { opacity: 0.4; cursor: default; }

    /* The stand-in where no icon resolved: the first characters of the name, which at 30px is
       enough to tell two keys apart when the tooltip carries the rest. */
    .ability-key-text {
        font-size: var(--font-size-smallest);
        line-height: 1;
        padding: 0 var(--space-2);
        text-align: center;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 100%;
    }

    /* Icon-only buttons are square rather than pill-shaped; the padding that makes room for a word
       beside the glyph just makes them lopsided without one. */
    .stage-chrome.icon-only .icon-btn {
        width: 22px;
        padding: 0;
        border-radius: var(--radius-3);
    }

    /* ONE height for everything on the stage, set here rather than left to each control's
       contents. A glyph and a word are different heights, and the plates ended up stepped: the
       scene globe stood taller than the preset words beside it and the camera button taller again.
       The box is the same and what sits in it is centred. */
    .stage-chrome .icon-btn, .stage-chrome .swatch, .stage-chrome .mode-selector button {
        height: 22px;
        font-size: var(--font-size-smaller);
        padding: 0 var(--space-8);
        /* A two-word label breaking in half makes the button taller than the plate it sits in,
           which then reports a height nothing else expects. */
        white-space: nowrap;
    }

    /* The SHAPE says which kind of control this is, and the two must not borrow each other's.

       A pill is the panel's mark for a toggle - an overlay switch, a kind filter - each one holding
       or releasing on its own. A mode switch is one choice out of several, and it says so by being
       segments that share their borders and their ends: exactly what the Skeleton control in the
       dock looks like. Rounding every segment to a pill made a radiogroup wear the toggles' clothes
       while behaving as a radiogroup, which is the same mismatch as the aria-pressed it just lost.

       The end radii come from the shared rule, which is interpolated above this one; all this has
       to do is stop overriding them. */
    .stage-chrome .icon-btn, .stage-chrome .swatch { border-radius: var(--radius-6); }
    .stage-chrome .mode-selector button { border-radius: 0; }
    .stage-chrome .mode-selector button:first-child { border-radius: var(--radius-3) 0 0 var(--radius-3); }
    .stage-chrome .mode-selector button:last-child { border-radius: 0 var(--radius-3) var(--radius-3) 0; }
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
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.5));
        border-radius: var(--radius-3);
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
        margin-left: var(--space-4);
        border-left: var(--space-1) solid var(--vscode-panel-border, #444);
        padding-left: var(--space-6);
        width: 28px;
        box-sizing: content-box;
    }

    /* One box, two corners. The scene sits at the left of the stage and the camera at the
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
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: var(--radius-6);
        background: var(--vscode-editorWidget-background, #202020);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.45);
    }
    .stage-flyout.on-left { left: 8px; }
    .stage-flyout.on-right { right: 8px; }
    /* Centred on the stage, like the bar it belongs to. */
    .stage-flyout.on-mid { left: 50%; transform: translateX(-50%); }

    /* Opens UPWARDS from the bottom edge, because that is where its button is. A flyout that
       appears at the top of the stage when you pressed something at the bottom reads as a
       different panel opening rather than this one, and the eye has to go and find it.

       The clearance is bigger than the top edge's 40px: the bottom row can be two plates tall once
       the shader corner shows its source line under the switch. */
    .stage-flyout.from-bottom {
        top: auto;
        bottom: 56px;
        max-height: calc(100% - 72px);
    }


    .stage-flyout-head {
        display: flex;
        align-items: center;
        gap: var(--space-4);
        padding: var(--space-6) var(--space-6) var(--space-6) var(--space-12);
        border-bottom: var(--space-1) solid var(--vscode-panel-border);
        font-size: var(--font-size-11);
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground, #999);
    }
    /* The first action floats to the far edge; the rest follow it. */
    /* Whatever comes first after the title takes the space. A count claims it where there is one,
       and the close button then follows it rather than fighting it for a second auto margin -
       two of those split the gap and left the count stranded in the middle. */
    .stage-flyout-head .section-count { margin-left: auto; }
    .stage-flyout-head .icon-btn:first-of-type { margin-left: auto; }
    .stage-flyout-head .section-count ~ .icon-btn { margin-left: var(--space-4); }
    /* The gap here is BETWEEN sections and is deliberately larger than the one inside them
       (see .dock-section-body): that difference is what makes a heading read as the start of a
       group rather than as one more row in a list. */
    .stage-flyout-body {
        display: flex;
        flex-direction: column;
        gap: var(--space-16);
        padding: var(--space-12) var(--space-12) var(--space-16);
        overflow-y: auto;
    }

    /* Everything below is the flyout being LESS CRAMPED, and every rule is scoped to it on purpose.
       The .field and .dock-section rules come from shared/dockChrome.ts, which the story
       graph, the localisation grid and the encyclopedia also wear - loosening them there is a
       different decision from loosening this dialog, and not one this asked for.

       What was measured on the weapon flyout before this: sections 8px apart, a label 5px from its
       control, checkbox rows 4px apart, and inputs on the browser's own padding - so the fields ran
       together into one dense stack, and the projectile list drew its rows with no padding at all. */

    /* A section is a GROUP. At 8px its fields butted into each other and the heading read as one
       more row. */
    .stage-flyout .dock-section { gap: var(--space-12); }
    .stage-flyout .field { gap: var(--space-6); }
    /* A row of switches needs more between them than a flex gap of 4, which put "Shield" and "Hull"
       close enough to read as one control with two boxes. */
    .stage-flyout .view-row { gap: var(--space-8); }
    /* A note is prose and is the one thing here that wraps, so it gets the leading to match. */
    .stage-flyout .field-note { line-height: 1.45; }

    /* The controls themselves. Nothing gave these any padding, so a number sat hard against the
       left edge of its box. Not applied to the stage plates - the stage-chrome select is a 22px
       control on the stage edge and sets its own. */
    .stage-flyout input[type=text],
    .stage-flyout input[type=number],
    .stage-flyout select {
        padding: var(--space-4) var(--space-8);
    }

    /* A list box, where the padding has to go on the ROWS - padding on the select itself indents
       the frame and leaves the rows as tight as they were. This is the control the report was
       most obviously about. */
    .stage-flyout select[size] { padding: var(--space-2); }
    .stage-flyout select[size] option { padding: var(--space-4) var(--space-6); border-radius: var(--radius-3); }

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

    /* The cover over a subject that is still assembling.

       OPAQUE, not a translucent scrim: the whole point is that the half-built model underneath is
       not worth looking at, and a scrim shows exactly the black untextured hull the reader
       complained about, only dimmer. It sits under the stage chrome's z-index so the view controls
       stay reachable - they act on the room, which is ready whether or not the model is. */
    .load-cover {
        position: absolute;
        inset: 0;
        z-index: 2;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--space-8);
        background: var(--vscode-editor-background, #1f1f1f);
        color: var(--vscode-descriptionForeground, #999);
        font-size: var(--font-size-12);
    }

    .load-spinner {
        width: 22px;
        height: 22px;
        border-radius: 50%;
        border: 2px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-top-color: var(--vscode-progressBar-background, #0078d4);
        animation: load-spin 0.9s linear infinite;
    }

    /* A reader who has asked for no motion gets none: the ring holds still and the counts
       underneath are what says the load is moving. */
    @media (prefers-reduced-motion: reduce) {
        .load-spinner { animation: none; }
    }

    @keyframes load-spin {
        to { transform: rotate(360deg); }
    }

    .load-label { letter-spacing: 0.02em; }
    .load-detail { font-variant-numeric: tabular-nums; opacity: 0.7; }

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
           an unlit hull on a dark theme is invisible. Deliberately NOT a themed colour - see the
           token's own note in tokens.ts. */
        background: var(--colour-scene-ground);
    }

    /* Labels are DOM, not sprites: they stay crisp at any zoom, theme themselves, and can be read by
       a screen reader. pointer-events off, or they would swallow the drags that orbit the camera. */
    .bone-labels { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
    .bone-label {
        position: absolute;
        top: 0;
        left: 0;
        margin: -9px 0 0 var(--space-8);
        padding: 0 var(--space-2);
        font-size: var(--font-size-10);
        white-space: nowrap;
        color: var(--vscode-editor-foreground);
        background: color-mix(in srgb, var(--vscode-editor-background) 70%, transparent);
        border-radius: var(--radius-3);
    }
    .bone-label.selected {
        color: var(--vscode-editor-background);
        background: var(--vscode-focusBorder);
    }

    /* The targeting marks. Shares the label layer, so it inherits pointer-events: none and the
       clipping - a reticle must never swallow the drag that orbits the camera. Sized in JS, from
       the fraction of the screen the game declares, so nothing here sets a width. */
    .reticle-mark {
        position: absolute;
        top: 0;
        left: 0;
        /* The ICON is a mask and the colour is the background, so the mark can be tinted by the
           hardpoint's health - the game runs it bright green through yellow and orange to red, and an
           <img> cannot be recoloured. Both properties are set from JS per mark. */
        mask-repeat: no-repeat;
        -webkit-mask-repeat: no-repeat;
        mask-size: contain;
        -webkit-mask-size: contain;
    }

    /* A deep skeleton indents far enough that nowrap rows widened the WHOLE panel: the dock's
       min-content width won over its set width, .body grew past the window, and the canvas stretched
       to 1400px inside an 1100px viewport. min-width: 0 lets the dock hold its size; the tree scrolls
       inside it instead. */
    .right-dock { min-width: 0; }
    .dock-content { min-width: 0; }

    /* Toggle buttons in the icon-btn / .active language the localisation search and the story
       graph's lane switches already use, rather than a checkbox row of their own invention.
       No backticks in here: this is a styled-components template literal.

       .kind-filter and .selection-actions were the two blocks this pass folded away - the kinds
       into the search element, Reset into the section title - so only the overlay strip is left. */
    .view-toggles { display: flex; gap: var(--space-4); flex-wrap: wrap; }
    .tree-search .icon-btn, .view-toggles .icon-btn {
        font-size: var(--font-size-smaller);
        padding: var(--space-2) var(--space-8);
        border-radius: var(--radius-6);
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
    /* kind-particle, which is what TreeKind calls it and what the row actually writes. This rule
       said kind-emitter and so had never matched anything: every effect row drew its icon in the
       body colour while the bones around it were blue, which is part of why a filtered tree read as
       bones with some grey text among them. */
    .row-kind.kind-particle { color: var(--vscode-charts-orange); }


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
        margin: 0 0 var(--space-2);
        /* The foot pad is the floating search strip's height. Without it the last row can never be
           scrolled out from under the strip, and on a model whose last row is the one you want -
           the tree is ordered, so that happens - it is simply unreachable. */
        padding: var(--space-2) 0 var(--space-24);
        overflow-y: auto;
        overflow-x: hidden;
        max-height: 48vh;
    }
    .bone-row {
        display: flex;
        align-items: center;
        gap: var(--space-4);
        padding: var(--space-2) var(--space-6);
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

    /* A row the filter kept only to reach a match below it. The WHOLE row recedes - name, kind
       icon and controls together - because the complaint was about weight, not about text: an
       ancestor bone drew its icon in full colour beside the greyed, italic effect rows that were
       actually being looked for, so the scaffolding was the loudest thing in a filtered tree.

       Recessive, never removed. These rows are the only thing holding the hierarchy together, and
       they are still real rows worth hiding or inspecting - they just are not the answer. Hover
       brings the row back to full weight, so acting on one is never a fight. */
    .bone-row.scaffold { opacity: 0.42; }
    .bone-row.scaffold:hover,
    .bone-row.scaffold.selected,
    .bone-row.scaffold:focus-within { opacity: 1; }
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
    .bone-row .bone-attach { color: var(--vscode-descriptionForeground); font-size: var(--font-size-smaller); }

    /* The eye, and the pair of buttons it heads. Pushed to the far edge so they line up in a
       column whatever the names do - the eye first, then the row's details, which is the order the
       reader asked for.

       The eye does NOT dim with its neighbour. It is the row's state as well as its control, so a
       tree of dimmed eyes would be a tree that will not say what it is showing; the details button
       beside it only ever offers an action. */
    .bone-row .row-eye {
        margin-left: auto;
        flex-shrink: 0;
        min-width: 18px;
        height: 18px;
        padding: 0 var(--space-2);
        opacity: 0.85;
    }
    .bone-row .row-eye:hover { opacity: 1; }

    /* An open eye is the ordinary state and stays quiet. A CLOSED one is the reader's own doing
       nine times in ten, so it keeps full weight - a hidden row you cannot find the eye on is the
       tick that "did nothing" all over again. */
    .bone-row .row-eye.eye-shown { opacity: 0.7; }
    .bone-row:hover .row-eye.eye-shown { opacity: 1; }

    /* Inherited: dimmed and inert, the way Blender greys an eye whose collection is hidden. The
       title says which ancestor, because a dead control that cannot explain itself is worse than
       no control. */
    .bone-row .row-eye.eye-inherited { opacity: 0.4; }
    .bone-row .row-eye.eye-inherited:disabled { cursor: default; }

    /* Dim until the row is under the pointer - a deep tree has to read as a list of names, not a
       column of controls. Never hidden outright: a button that only exists on hover is one nobody
       finds, which is what the reader reported about this one. Raised from 0.25, where it was
       invisible against the default dark theme until hovered. */
    .bone-row .row-details {
        flex-shrink: 0;
        opacity: 0.45;
        /* An explicit box rather than one the glyph gives it. A button sized by its icon has no
           size at all if the icon font has not loaded, which makes it unclickable rather than
           merely unlabelled. */
        min-width: 18px;
        height: 18px;
        padding: 0 var(--space-2);
    }
    .bone-row:hover .row-details,
    .bone-row .row-details:focus-visible,
    .bone-row .row-details.active { opacity: 1; }

    /* Pinned against the WINDOW, because the row it belongs to lives inside a scroller that would
       clip it. placeInfo and placeCardInfo supply the corner; everything here is the box itself. */
    .details-flyout {
        position: fixed;
        z-index: 6;
        width: 320px;
        max-height: 62vh;
        display: flex;
        flex-direction: column;
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: var(--radius-6);
        background: var(--vscode-editorWidget-background, #202020);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.45);
    }
    .details-head {
        display: flex;
        align-items: baseline;
        gap: var(--space-6);
        padding: var(--space-4) var(--space-4) var(--space-4) var(--space-8);
        border-bottom: var(--space-1) solid var(--vscode-panel-border);
        font-size: var(--font-size-11);
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
        gap: var(--space-8);
        padding: var(--space-8) var(--space-8) var(--space-8);
        overflow-y: auto;
    }

    /* Word for word what the localisation and encyclopedia problem bars are, because it is the
       same bar in the same place. The relative position is the one that matters most: the shared
       panel renders its drag handle absolutely positioned, so without it the handle anchors to
       some ancestor and the panel cannot be resized at all. */
    .preview-problems {
        position: relative;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        min-height: 0;
        border-top: var(--space-1) solid var(--vscode-panel-border, #444);
        background: var(--vscode-sideBar-background, #252526);
        font-size: var(--font-size-12);
    }

    /* This editor's findings are PROSE, not the short entry labels the loc grids report, so they
       wrap instead of being clamped to one line - the shared rule's ellipsis would hide most of
       every message. The same override the encyclopedia already makes, for the same reason. */
    .preview-problems .problem-row { align-items: flex-start; padding: var(--space-2) var(--space-6); }
    .preview-problems .problem-msg {
        white-space: normal;
        overflow: visible;
        line-height: 1.35;
        /* A bone or file name has no break opportunity in it, so without this it runs off. */
        overflow-wrap: anywhere;
    }
    .preview-problems .problem-row .codicon { margin-top: var(--space-2); }

    /* At the end of the row and out of the way: an id is what you look up AFTER reading the
       finding, so it must not compete with the sentence for the reader's eye. Monospaced because
       it is a literal string to be copied, and tabular so a column of them lines up. */
    .preview-problems .problem-id {
        flex: 0 0 auto;
        margin-left: auto;
        padding-left: var(--space-8);
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: var(--font-size-smaller);
        font-variant-numeric: tabular-nums;
        opacity: 0.55;
        white-space: nowrap;
    }

    /* Dimmer still, and italic: not an identifier, so it must not look like one that could be
       copied into a suppression comment. */
    .preview-problems .problem-id.render-time {
        font-family: inherit;
        font-style: italic;
        opacity: 0.4;
    }

    /* Which hardpoint, when the sentence does not already say so - see problemWhere. */
    .problem-where {
        flex-shrink: 0;
        padding: 0 var(--space-4);
        border-radius: var(--radius-3);
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: var(--font-size-smaller);
        background: var(--vscode-badge-background, rgba(128, 128, 128, 0.2));
        color: var(--vscode-badge-foreground, inherit);
    }


    /* A readout, not a menu: no hover state and no pointer, because nothing here is clickable yet.
       A row that looks pressable is a promise of interaction. */
    .part-list { list-style: none; margin: 0; padding: 0; }
    .part-list li {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
        padding: var(--space-2) var(--space-6);
        min-width: 0;
        cursor: default;
    }
    .part-list li .field-label { min-width: 0; align-items: flex-start; }
    .part-list li .field-label input[type=checkbox] { margin-top: var(--space-2); flex-shrink: 0; }
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
    /* Indented to sit under the name rather than under its checkbox, and clipped to one line: a
       hardpoint's detail is a bone and a hitpoint count, which fits. */
    .part-list li .detail {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        padding-left: var(--space-24);
        font-size: var(--font-size-smaller);
        color: var(--vscode-descriptionForeground);
    }
    /* A weapon's is four separate facts - reach, cone, cadence, damage - and clipping them at the
       dock's width ended the row on "3 sho...", which says nothing about the cadence it is there
       to state. Wrapping costs a line; clipping costs the answer. */
    .part-list li .detail.wraps {
        white-space: normal;
        overflow: visible;
    }
    /* The command bar's own icon, at the size the atlas holds it. Blowing a 26x26 slot up makes
       hand-drawn art look broken; the game draws it at native size and so does this. */
    .ability-row-head { display: flex; align-items: flex-start; gap: var(--space-6); min-width: 0; }
    .ability-row-head .field-label, .ability-row-head .part-name { flex: 1; }
    .ability-icon {
        width: 26px;
        height: 26px;
        flex-shrink: 0;
        image-rendering: pixelated;
        display: block;
    }
    /* The tooltip text the game shows. Wraps - it is a sentence, and clipping it to the dock's
       width would end most of them mid-word. */
    .part-list li .ability-description { font-style: italic; }

    /* An ability row carries four things now - art, name, what it drives, what the game calls it -
       where the list this styling comes from carries two. At the 1px gap and 3px padding the rest
       of .part-list uses, those four ran together into a paragraph and the rows stopped reading as
       separate abilities at all. Relaxed here rather than on .part-list itself, which the weapon
       and hardpoint lists share and which is correctly tight for a name and one line under it.

       NOTE: no backticks in this block - it lives inside a styled.div template literal, and one
       terminates it. That mistake reports itself as "Shell cannot be used as a JSX component". */
    .ability-list li {
        gap: var(--space-4);
        padding: var(--space-8) var(--space-6);
    }
    .ability-list li + li {
        border-top: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.25));
    }
    /* How a jump LOOKS. Where it goes is the card head's business, and this used to decide that
       too: align-self flex-start and a 21px left margin put it under the text, indented past the
       icon, which is where it sat before the rows became cards.
       NOTE: no backticks in this comment - one ends the template literal the stylesheet lives in.

       Those two lines outlived the layout they were written for, and being the more specific rule
       they beat the head's own auto margin - so the jump on BOTH cards sat 21px after the name
       instead of at the right edge. Position removed; appearance kept. */
    .part-list li .goto-definition {
        padding: 0;
        border: none;
        background: none;
        font: inherit;
        font-size: var(--font-size-smaller);
        color: var(--vscode-textLink-foreground);
        cursor: pointer;
    }
    .part-list li .goto-definition:hover:not(:disabled) { text-decoration: underline; }
    .part-list li .goto-definition:disabled {
        color: var(--vscode-disabledForeground);
        cursor: default;
    }
    /* Full width now, one row per fire bone - the 21px indent it carried lined it up with a jump
       that no longer sits under the name. */
    .part-list li .bone-picks { margin-top: var(--space-2); }
    /* A count that can be pressed. Same type as the count beside it so the header does not jump
       when a selection appears, but with an affordance so it reads as the way out. */
    /* Pushed to the far end of the title, past the count. The class is the dock's own name for
       this position - the problems button uses it - so the two corners agree.
       NOTE: no backticks in this comment; one would end the template literal the whole stylesheet
       lives in, and the error surfaces far away as a TS1005. */
    .dock-section-title .header-right {
        margin-left: var(--space-6);
        flex: 0 0 auto;
        min-width: 20px;
        height: 18px;
        padding: 0 var(--space-2);
    }
    .dock-section-title .header-right:disabled { opacity: 0.35; }

    .section-count.as-button {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.4));
        border-radius: var(--radius-6);
        padding: 0 var(--space-4);
        background: none;
        color: inherit;
        font: inherit;
        cursor: pointer;
    }
    .section-count.as-button:hover { background: var(--vscode-list-hoverBackground); }
    /* A picked bone is boxed on the model, so its button carries the same weight as the active
       segment of a mode selector - it is a current state, not an action waiting to be taken. */
    .btn.selected {
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
        border-color: var(--vscode-button-background);
    }

    .stat-row {
        display: flex;
        justify-content: space-between;
        gap: var(--space-12);
        padding: var(--space-1) var(--space-6);
        font-variant-numeric: tabular-nums;
    }
    .stat-row .value { color: var(--vscode-descriptionForeground); }

    .view-row { display: flex; align-items: center; gap: var(--space-4); flex-wrap: wrap; }

    /* The clip library. Wide rows that look pressable, because they ARE the play control - there is
       no separate play button and no dropdown to open first. */
    .player { display: flex; flex-direction: column; gap: var(--space-6); }
    .player-row { display: flex; align-items: center; gap: var(--space-1); flex-wrap: wrap; }
    /* Six controls and a clock have to share one dock-wide row. The toolbar default leaves them
       ~30px too wide at the dock's opening size, which wrapped the clock onto a line of its own. */
    .player-row .icon-btn { min-width: 20px; padding: var(--space-4) var(--space-4); }
    .player-row .player-time {
        margin-left: auto;
        padding-right: var(--space-4);
        font-variant-numeric: tabular-nums;
        font-size: var(--font-size-smaller);
        color: var(--vscode-descriptionForeground);
    }
    /* The travel direction is explicit on every slider in this panel. A range input takes it from
       the inherited writing direction, so anything upstream that flips that - a host laying the
       webview out right-to-left, a stray rule - silently runs the playhead backwards. These read a
       timeline and a magnitude; both only make sense left to right.
       (No backticks in here: this is a styled-components template literal.) */
    .player-scrub, .player input[type=range], .field input[type=range] { direction: ltr; }
    .player-scrub { width: 100%; }
    .player-section { margin-bottom: var(--space-8); }

    /* No padding of its own now that it lives in the foot - the foot lays its children out with a
       gap. The old top padding was there to hold it off the tree it used to sit under. */

    .anim-list { display: flex; flex-direction: column; gap: var(--space-2); }
    /* A family, folded or open. The heading is the whole hit area, not just the chevron - a 13px
       target beside a word that obviously names the thing being folded is a target people miss. */
    .anim-family { gap: var(--space-4); margin-bottom: var(--space-8); }
    .anim-family:last-child { margin-bottom: 0; }
    .dock-section-title.as-fold { cursor: pointer; user-select: none; }
    .dock-section-title.as-fold:hover { color: var(--vscode-foreground, #ccc); }

    /* An ACTION, as a full-width card down a list.

       It was a tile on the dock's shared grid. The user's verdict: the idea "was good on paper, in
       reality we have a lot of animations where that works and some where the number of alternate
       indexes escalates so drastically that it is not usable that way". A 128px tile held about
       four take chips and put the rest behind a sideways scrollbar - which is precisely the case
       worth looking at. Full width, and the chips WRAP.

       The shape is the user's: play state left and vertically centred, name on the top row left
       aligned, takes on the bottom row left aligned and filling right. */
    .anim-list { display: flex; flex-direction: column; gap: var(--space-4); }

    .anim-card {
        display: flex;
        align-items: center;
        gap: var(--space-8);
        padding: var(--space-8) var(--space-8);
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.25));
        border-radius: var(--radius-6);
        background: var(--vscode-editorWidget-background, rgba(128, 128, 128, 0.08));
    }
    .anim-card:hover { background: var(--vscode-list-hoverBackground); }
    .anim-card.active {
        border-color: var(--vscode-button-background);
        background: color-mix(in srgb, var(--vscode-button-background) 22%, transparent);
    }

    /* Its own column, so it is centred across BOTH rows rather than sitting beside the name. */
    .anim-play {
        flex: 0 0 auto;
        display: flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
        padding: 0;
        border: none;
        border-radius: var(--radius-3);
        background: none;
        color: inherit;
        cursor: pointer;
    }
    .anim-play:hover { background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.2)); }
    .anim-play svg { flex-shrink: 0; opacity: 0.85; }

    /* min-width 0, or a long action name refuses to wrap and pushes the card wider than the dock. */
    .anim-card-body {
        display: flex;
        flex: 1 1 auto;
        flex-direction: column;
        gap: var(--space-2);
        min-width: 0;
    }
    .anim-name { text-align: left; overflow-wrap: anywhere; }

    /* WRAPS, where the tile scrolled. Nine idle takes fit on one line at full width and a model
       with thirty gets a second - which is readable, and a scrollbar over thirty was not. */
    .anim-takes {
        display: flex;
        flex-wrap: wrap;
        justify-content: flex-start;
        gap: var(--space-2);
        width: 100%;
    }
    .anim-take {
        flex: 0 0 auto;
        min-width: 20px;
        padding: var(--space-1) var(--space-4);
        border: var(--space-1) solid transparent;
        border-radius: var(--radius-3);
        background: none;
        color: var(--vscode-descriptionForeground, #999);
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: var(--font-size-smallest);
        cursor: pointer;
    }
    .anim-take:hover { background: var(--vscode-list-hoverBackground); }
    .anim-take.active {
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
    }
    .anim-row {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        width: 100%;
        padding: var(--space-4) var(--space-8);
        border: var(--space-1) solid transparent;
        border-radius: var(--radius-3);
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
    { view: 'threeQuarter', label: '3/4', title: 'Look from three-quarters on' },
    { view: 'front', label: 'Front', title: 'Look from the front' },
    { view: 'side', label: 'Side', title: 'Look from the side' },
    { view: 'top', label: 'Top', title: 'Look from above' },
];


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

    /** Presets and the model's own cameras as the one choice they are. */
    const cameraViews = useMemo(
        () => cameraViewOptions(PRESETS, scene?.cameras ?? []), [scene?.cameras]);

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
     *
     * The weapon bench is left alone for the same reason, and this changed when it moved tiers: it
     * belongs to the PROJECT now, so throwing away a mod's saved weapons is not something "reset
     * the room" should ever do.
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
    };

    /**
     * The reader's own word about how to tint the hull, recorded as well as applied.
     *
     * The refs hold what they CHOSE; the state holds what this subject could honour. They have to be
     * two things: opening a unit whose roster lacks the chosen faction falls back to no tint, and
     * writing that fallback into the ref would throw the choice away on the way past. Which is the
     * half that was missing - the ref was only ever set from stored settings, so a faction picked
     * mid-session was lost at the next subject and the STARTUP value came back instead.
     */
    const chooseTint = (name: string, colour: string | null): void => {
        storedFactionRef.current = name === '' ? null : name;
        storedCustomColourRef.current = colour;
        setFaction(name);
        setCustomColour(colour);
    };

    /** Changes one directional, leaving the other two and the global terms alone. */
    const setLight = (name: DirectionalName, change: Partial<DirectionalSetting>): void =>
        setLights(current => ({ ...current, [name]: { ...current[name], ...change } }));

    // How much further than the subject the camera can see. Fitted to the model AND its effects, so
    // 1 already clears a flamethrower's throw; the multiplier is for the trails no fit anticipates.
    const [drawDistance, setDrawDistance] = useState(DEFAULT_VIEWER_SETTINGS.drawDistance);

    /** The faction the reader last chose, by name, until this subject's own list arrives. */
    const storedFactionRef = useRef<string | null>(null);
    /** The colour they picked by hand instead, which every subject can wear. */
    const storedCustomColourRef = useRef<string | null>(null);

    /** Nothing is written back until the stored room has been applied, or we would save defaults. */
    const restoredRef = useRef(false);

    /**
     * This subject's state from earlier in the session, until the scene arrives to apply it to.
     *
     * Held rather than applied on arrival: it lands before the scene, and the scene handler resets
     * every one of these fields on its way in.
     */
    const storedSubjectRef = useRef<SubjectState | null>(null);

    /**
     * Whether the scene being handled is a re-read rather than an opening.
     *
     * A ref and not state: it is read inside the message handler while parts stream in, and a state
     * update would not be visible to the closure that is already running.
     */
    const refreshingRef = useRef(false);
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
    /* No `selectedBone` state. It was a single index kept beside `selected`, which is a set of
       rows - two pieces of state for one fact, and the smaller one won: picking a second fire bone
       drew its axes and silently took the first one's away. The indices are DERIVED below. */
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
    /* The two Gameplay tools on the bottom edge. Kept apart from the camera and scene flyouts
       above: those describe the view and may be open together with anything, while these describe
       the subject and sit on the opposite edge. */
    const [abilitiesOpen, setAbilitiesOpen] = useState(false);
    const [attackOpen, setAttackOpen] = useState(false);

    /**
     * What every shot did, newest first.
     *
     * TIER 2 - it describes what has been done to THIS subject, so it clears with the scene. A log
     * carried across subjects would read as damage to a unit that never took any.
     */
    const [damageLog, setDamageLog] = useState<DamageLogEntry[]>([]);
    const [logOpen, setLogOpen] = useState(false);

    /**
     * A moment of red on the Fire button, so a press is visibly a press.
     *
     * The button never latches, and a shot at a full capital ship moves a bar by a percent or two -
     * so without this the only evidence anything happened was a number you had to go looking for.
     */
    const [justFired, setJustFired] = useState(false);

    /* Clears itself. A timer per press rather than a CSS animation, because the flash has to
       restart on a SECOND press - an animation on a class that is already there does not replay,
       so firing twice quickly would have flashed once. */
    useEffect(() => {
        if (!justFired) {
            return;
        }

        const handle = setTimeout(() => setJustFired(false), 180);

        return () => clearTimeout(handle);
    }, [justFired]);
    const cameraRef = useRef<HTMLDivElement | null>(null);
    const abilitiesRef = useRef<HTMLDivElement | null>(null);
    const attackRef = useRef<HTMLDivElement | null>(null);
    const logRef = useRef<HTMLDivElement | null>(null);

    /** Whether the model's identity flyout is showing. Closed on open - it is a check, not a tool. */
    const [infoOpen, setInfoOpen] = useState(false);
    const infoRef = useRef<HTMLDivElement | null>(null);

    /* Whether the reader has pressed the tree's magnifier. NOT whether the box is showing - see
       `searchIsOpen`, which keeps it open while a pattern would otherwise be hidden behind it. */
    const [searchPressed, setSearchPressed] = useState(false);

    /* Which hardpoint card has its measurements open. One at a time: two of them open turns the
       list back into the wall of prose that putting them behind a button was meant to end. */
    const [infoCard, setInfoCard] = useState<string | null>(null);
    const cardInfoRef = useRef<HTMLDivElement | null>(null);
    const [cardInfoAt, setCardInfoAt] = useState<{ left: number; top: number } | null>(null);

    /* The same, for an ability card. Its own slot rather than sharing `infoCard`: the two lists are
       open at once - the abilities are a stage flyout - and one id could not say which was meant. */
    const [infoAbility, setInfoAbility] = useState<string | null>(null);
    const abilityInfoRef = useRef<HTMLDivElement | null>(null);
    const [abilityInfoAt, setAbilityInfoAt] = useState<{ left: number; top: number } | null>(null);

    /* Type headings the reader has folded away, by type. A hull with ten laser hardpoints is ten
       cards you scroll past on the way to its engines. */
    const [foldedGroups, setFoldedGroups] = useState<ReadonlySet<string>>(new Set());

    /* The hardpoint list's own filter, in the tree's shape - see `searchIsOpen` for why a pattern
       keeps its own box open whatever the button was left saying. */
    const [hardpointFilter, setHardpointFilter] = useState('');
    const [hardpointSearchPressed, setHardpointSearchPressed] = useState(false);
    const hardpointSearchRef = useRef<HTMLDivElement | null>(null);

    /**
     * Attachments that currently have nowhere to hang, as of the last part to arrive.
     *
     * Its own slot rather than a push into `problems`, because it is a SNAPSHOT of the scene as it
     * stands - every entry has to be able to go away again when the part it names turns up.
     */
    const [attachmentIssues, setAttachmentIssues] = useState<string[]>([]);

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


    /**
     * The scene flyout's box.
     *
     * Escape closes it, but a click OUTSIDE deliberately does not: these are settings you adjust
     * while watching what they do to the model, and dismissing the panel the moment you touch the
     * viewport would make the two impossible to use together. It has its own close button instead.
     */
    const worldRef = useRef<HTMLDivElement | null>(null);

    /**
     * The tree's search element, for the click-outside that folds it away again.
     *
     * Unlike the stage flyouts above, an outside click DOES close this one: it is a control you
     * open, type in and leave, not a set of dials you work while watching the model. It closes only
     * when empty, though - `searchIsOpen` would reopen it on the next render otherwise, and the
     * press would read as a control that does nothing.
     */
    const searchRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        if (!hardpointSearchPressed) {
            return;
        }

        const onDown = (event: MouseEvent): void => {
            if (!hardpointSearchRef.current?.contains(event.target as Node)) {
                setHardpointSearchPressed(false);
            }
        };

        document.addEventListener('mousedown', onDown);
        return () => document.removeEventListener('mousedown', onDown);
    }, [hardpointSearchPressed]);

    useEffect(() => {
        if (!searchPressed) {
            return;
        }

        const onDown = (event: MouseEvent): void => {
            if (!searchRef.current?.contains(event.target as Node)) {
                setSearchPressed(false);
            }
        };

        document.addEventListener('mousedown', onDown);
        return () => document.removeEventListener('mousedown', onDown);
    }, [searchPressed]);

    // Both stage flyouts, one rule. Escape closes both; an outside click deliberately closes
    // neither - see above. They can be open TOGETHER on purpose: setting up a shot means moving the
    // light and the camera against each other, and a panel that shuts the other one turns that into
    // a trip back to the button every time. Opposite corners, so they never overlap.
    useEffect(() => {
        if (!worldOpen && !cameraOpen && !abilitiesOpen && !attackOpen) {
            return;
        }

        const onKey = (event: KeyboardEvent): void => {
            if (event.key === 'Escape') {
                setWorldOpen(false);
                setCameraOpen(false);
                setAbilitiesOpen(false);
                setAttackOpen(false);
            }
        };

        document.addEventListener('keydown', onKey);

        return () => document.removeEventListener('keydown', onKey);
    }, [worldOpen, cameraOpen, abilitiesOpen, attackOpen]);

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

    /**
     * Each system's owning object `Scale_Factor`, keyed like {@link systemsRef}.
     *
     * A uniform render scale on the whole system. Only a `<Particle>` GAME OBJECT can carry one - a
     * model's proxy names the asset directly and gets 1 - so it is keyed by the name that was
     * ASKED for, which is the object's where there is one.
     */
    const systemScalesRef = useRef(new Map<string, number>());
    const pendingRef = useRef<PreviewParticle[]>([]);
    const requestedSystemsRef = useRef(new Set<string>());

    /**
     * The effects belonging to a PASSIVE subject, by id.
     *
     * They answer to their own model rather than to the subject's opening and damage rules - see
     * `passiveEffectPlaysNow` - and this is how the one place that decides whether an effect draws
     * tells them apart.
     */
    const passiveEffectIdsRef = useRef(new Set<string>());

    /**
     * What has been asked for and what has come back, per kind.
     *
     * They dedupe - a ten-part unit must not request its shared hull texture ten times - and they
     * are also what the loading cover counts. Two ledgers rather than one so a texture and an
     * effect that happen to share a name cannot settle each other.
     */
    const textureLedger = useRef(new AssetLedger());
    const shaderLedger = useRef(new AssetLedger());

    /**
     * How far into loading this subject the preview is.
     *
     * The viewport is covered while it runs - see `loadProgress` for why the load is worth hiding
     * rather than reordering.
     */
    const [tally, setTally] = useState<LoadTally>({
        expectedParts: null,
        arrivedParts: 0,
        requestedAssets: 0,
        settledAssets: 0,
        timedOut: false,
    });

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

    /**
     * Effects asked for but not yet delivered, to play the moment they arrive.
     *
     * The attachment travels with the request because the two callers want different ones: a
     * hardpoint's death explosion goes off at its bone on the hull, and a wreck's goes off at the
     * WRECK, which by then has drifted a long way from the hardpoint it left.
     */
    const explosionsRef = useRef<{
        id: string;
        system: string;
        /** Absent for an effect that belongs to the scene rather than to any one part. */
        partId: string | undefined;
        bone?: string;
        /** A trailing fire lasts as long as the debris; a blast plays once. */
        once: boolean;
        /** Where to put it when it belongs to no part. See `ParticlePlacement.at`. */
        at?: { x: number; y: number; z: number };
    }[]>([]);

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

    const [reticlesOn, setReticlesOn] = useState(false);

    /**
     * The weapon the reader has built, and the ones they have saved.
     *
     * Tier 1 - it describes their testing habits, not this model - so both are restored from the
     * stored room and written back by the one effect that owns Tier 1.
     */
    /**
     * The abilities the reader has switched on.
     *
     * Tier 2 - it describes what you are looking at right now, so it resets with the subject. The
     * proxies it holds off ride the ordinary effect gate, which means a held burst does NOT burn
     * its life away and come back exhausted.
     */
    const [activeAbilities, setActiveAbilities] = useState<ReadonlySet<string>>(new Set());

    /** Whether the turrets are swinging through the traverse their XML declares. */
    /**
     * Which hardpoints are swinging, by id.
     *
     * A set rather than the one boolean this was: the button lived on the panel and swung every
     * turret at once, which on a hull whose hardpoints declare different extents is a control that
     * cannot say what it will do. It belongs to the hardpoint that declares the traverse.
     */
    const [sweeping, setSweeping] = useState<ReadonlySet<string>>(new Set());

    const [attacker, setAttacker] = useState<Attacker>(DEFAULT_ATTACKER);
    const [attackerPresets, setAttackerPresets] =
        useState<AttackerPreset[]>([]);
    const [presetName, setPresetName] = useState('');
    const [projectileSearch, setProjectileSearch] = useState('');

    /** A projectile picked from the catalogue whose values are not on the wire yet. */
    const [projectileNote, setProjectileNote] = useState<string | null>(null);

    /**
     * The projectile the built weapon was filled FROM, if any.
     *
     * Remembered separately from the weapon's own numbers because the blast area belongs to the
     * projectile and has no field in the panel: damage and damage type are things the reader edits,
     * a blast radius is a property of the bolt they picked. Cleared when the subject changes.
     */
    const [attackerProjectile, setAttackerProjectile] = useState<string | null>(null);

    /**
     * What is left of the target, and of each hardpoint.
     *
     * Tier 2 at most: a fresh preview opens undamaged, because the opening rules beat persistence.
     * `null` means the scene has said nothing yet.
     */
    const [pools, setPools] = useState<Pools | null>(null);

    /**
     * Whether the energy pool is shown at all.
     *
     * Off until the host says otherwise. The mechanic works in the engine but the shipped game
     * disables it and offers no interface, so a modder turns it on deliberately - see the
     * `aet-eaw-edit.features.preview.energyPool` setting, which says as much.
     */
    const [energyPool, setEnergyPool] = useState(false);

    /**
     * Whether the shield has been shot off the ship rather than shot down.
     *
     * The user's rule: every hardpoint of type shield generator destroyed means no shield at all,
     * whatever the pool holds - the way the engines go out with theirs.
     */
    const shieldsDown = useMemo(
        () => shieldGeneratorsDown(scene?.hardpoints ?? [], destroyed), [scene, destroyed]);

    /** What the pool readout and the status bars both read. */
    const poolOptions = useMemo(
        () => ({ energy: energyPool, shieldsDown }), [energyPool, shieldsDown]);
    const [hardpointHealth, setHardpointHealth] = useState<Record<string, number | null>>({});
    const [fireTarget, setFireTarget] = useState('hull');

    /**
     * Which of the seven targeting states the marks are drawn in.
     *
     * Enemy is where it opens: it is what you see looking at somebody else's ship, which is the
     * case a modder is checking. Tier 2 at most - it describes what you are inspecting right now,
     * not a habit - so it resets with the subject rather than being stored.
     */
    // Fixed. The tracked twin comes from HOVERING a mark, which is what the game does, so there is
    // no state to choose - see the note in the Hardpoints section. The other four states
    // (friendly, repairing, disabled and its tracked form) have no way in at present.
    const reticleState: ReticleState = 'enemy';

    /**
     * Weapon weapons the reader has switched off.
     *
     * The weapons that are OFF rather than the ones that are on, so a subject with a hundred hardpoints
     * opens showing all of them without the panel having to enumerate a hundred ids first - and so
     * the master pill can be flipped without losing which weapons were picked.
     */
    const [hiddenWeapons, setHiddenWeapons] = useState<ReadonlySet<string>>(new Set());

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

    /* Slider positions over the levels this model DEFINES. Rebuilt only when the level list
       changes, which is once per subject. */
    const altSteps = useMemo(() => levelSteps(levels.alt), [levels.alt]);
    const lodSteps = useMemo(() => levelSteps(levels.lod), [levels.lod]);

    /**
     * What each detail level costs, for the LOD slider's own label.
     *
     * Depends on `stats` for the reason the arcs and the reticles do - it is this component's
     * signal that geometry has arrived - and on `alt`, because a mesh tagged for another damage
     * stage is not part of what the level draws.
     */
    /**
     * Where the shadow colour can be seen, which decides whether its control is live.
     *
     * Depends on `translatedOn` because the stencil pass follows the renderer, and on `stats`
     * because a model with no authored volume casts nothing through it - and whether one is loaded
     * is not known until geometry arrives.
     */
    const shadowReach = useMemo(
        () => shadowTintReach(floor, viewportRef.current?.castsStencilShadows() ?? false),
        [floor, translatedOn, stats]);

    const lodCost = useMemo(
        () => viewportRef.current?.costByLod(levels.lod) ?? new Map<number, LevelCost>(),
        [levels.lod, alt, stats]);

    /**
     * Read inside the message handler, which is not re-created when `destroyed` changes.
     *
     * Geometry and effects arrive asynchronously, so a part or system landing after a hardpoint was
     * destroyed has to see the current state rather than the one captured when the handler was made.
     */
    /**
     * Wreckage waiting on its model to arrive.
     *
     * Same shape as the death-explosion queue beside it, and for the same reason: the GLB is asked
     * for at the moment of destruction, and the debris is spawned when it lands.
     */
    const breakoffsRef = useRef<{
        id: string; prop: PreviewBreakoffProp; at: BreakoffAnchor;
    }[]>([]);

    const destroyedRef = useRef<ReadonlySet<string>>(destroyed);
    destroyedRef.current = destroyed;

    /** Same reason as {@link destroyedRef}: the message handler outlives any one scene. */
    const sceneRef = useRef<PreviewScene | null>(scene);
    sceneRef.current = scene;

    /**
     * Same reason again, for the ability decider.
     *
     * A system attaching after an ability was switched on has to see the CURRENT set, or it lands
     * with the visibility the scene had when the handler was made.
     */
    const abilityProxyIdsRef = useRef<ReadonlyMap<string, string[]>>(new Map());

    const activeAbilitiesRef = useRef<ReadonlySet<string>>(new Set());

    /**
     * Effects this unit can never show. Read through a ref for the same reason as the two above:
     * a system attaches long after the scene lands, and it has to meet the current answer.
     */
    const unboundEffectsRef = useRef<ReadonlySet<string>>(new Set());
    /** What an active ability is standing in for. See {@link replacedByAbility}. */
    const replacedEffectsRef = useRef<ReadonlySet<string>>(new Set());

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

    /**
     * Everything the inspector tab needs to describe the row, or null when no row is being
     * inspected.
     *
     * Built here rather than there because only this side has the scene: `inspectionOf` reads the
     * loaded model, and the tab has no three.js of its own. It is the raw facts, not the rendered
     * panels - the tab runs the same `inspectPanels` over them.
     */
    const inspectorSubject = useMemo<InspectorSubject | null>(
        () => detailsRow === null
            ? null
            : {
                rowId: detailsRow,
                sources: inspectSources,
                modelDetail,
                modelReference: detailWantedRef.current,
            },
        [detailsRow, inspectSources, modelDetail]);

    /**
     * Keeps an open inspector on the row this preview is showing.
     *
     * `updateInspector` retargets a tab that is open and does nothing at all if none is - so
     * closing the tab keeps it closed, rather than any change here springing it open again.
     */
    useEffect(() => {
        if (inspectorSubject === null) {
            return;
        }

        vscode.postMessage({ type: 'updateInspector', subject: inspectorSubject });
    }, [inspectorSubject]);

    /**
     * Opens the inspector tab on one row.
     *
     * Carries the subject rather than leaving it to the effect above: pressing the button on the
     * row that is ALREADY being inspected changes no state, so the effect would not fire - and a
     * reader who closed the tab and pressed the same row again would get an empty one back.
     */
    const openInspector = useCallback((rowId: string): void => {
        setDetailsRow(rowId);

        vscode.postMessage({
            type: 'openInspector',
            subject: {
                rowId,
                sources: viewportRef.current?.inspectionOf(rowId) ?? [],
                modelDetail,
                modelReference: detailWantedRef.current,
            } satisfies InspectorSubject,
        });
    }, [modelDetail]);

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

        // The end of a piece of wreckage. `Death_Explosions` fires where it FINISHED - the
        // viewport keeps an empty marker there for exactly this - and the fire it was trailing
        // stops with it, which is the one thing an explosion does not do on its own.
        viewport.onBreakoffExpired = (key, prop): void => {
            viewport.removeParticleSystem(`${WRECK_EFFECT}${key}`);
            dropPassiveEffects(`${BREAKOFF_ATTACHMENT}${key}`);

            playEffect(
                `explosion:${key}:${Date.now()}`, prop.deathExplosions ?? '',
                `${BREAKOFF_ATTACHMENT}${key}`, undefined, true);
        };

        // Clicking a joint selects it in the tree; selecting in the tree highlights the joint. One
        // selection, two ways in, so neither view can disagree with the other about what is chosen.
        viewport.onBoneSelected = index => {
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
        viewport.onReticleClicked = id => selectHardpoint(id);

        const observer = new ResizeObserver(() => viewport.resize());
        observer.observe(canvasRef.current);

        return () => {
            observer.disconnect();
            viewport.dispose();
            viewportRef.current = null;
        };
    }, []);

    const loading = loadState(tally);

    /**
     * Gives up on a load that has stopped moving.
     *
     * An IDLE deadline, not a total one: a capital ship on a cold index legitimately takes a while,
     * and a fixed budget would either cut that short or be so generous it never fires. What is
     * actually wrong is nothing arriving - a request the server never answers - and eight seconds
     * of silence says that whether the subject is a trooper or a Star Destroyer.
     *
     * The cover coming off early is the mild failure. Leaving it down over a model that is
     * perfectly visible underneath is the bad one.
     */
    useEffect(() => {
        if (!loading.covered) {
            return;
        }

        const timer = setTimeout(
            () => setTally(current => ({ ...current, timedOut: true })), LOAD_IDLE_LIMIT);

        return () => clearTimeout(timer);
    }, [loading.covered, tally.arrivedParts, tally.settledAssets, tally.expectedParts]);

    /** Publishes what the ledgers now hold, which is what the cover reads. */
    const syncTally = useCallback((): void => {
        setTally(current => ({
            ...current,
            requestedAssets: textureLedger.current.requested + shaderLedger.current.requested,
            settledAssets: textureLedger.current.settled + shaderLedger.current.settled,
        }));
    }, []);

    /**
     * Asks for one texture, once.
     *
     * The gate is the ledger's rather than a bare Set because the count and the deduplication have
     * to agree: the host answers a repeat with nothing, so a name posted twice and counted twice
     * would leave the cover waiting on a reply that is never coming.
     */
    const askForTexture = useCallback((name: string): void => {
        if (!textureLedger.current.request(name)) {
            return;
        }

        vscode.postMessage({ type: 'requestTexture', name });
        syncTally();
    }, [syncTally]);

    /** The same, for an effect or one of its headers. */
    const askForShader = useCallback((name: string): void => {
        if (!shaderLedger.current.request(name)) {
            return;
        }

        vscode.postMessage({ type: 'requestShader', name });
        syncTally();
    }, [syncTally]);

    const refreshStats = useCallback(() => {
        const viewport = viewportRef.current;
        setStats(viewport?.stats() ?? null);
        setBones(viewport?.skeleton() ?? []);
        setTreeItems(viewport?.treeItems() ?? []);
        setAttachments(viewport?.attachmentsByBone() ?? new Map());
        // The geometry's own tags, WIDENED by what the object declares. A damage stage may touch
        // no geometry at all - see `withDeclaredStages` - so the model alone under-reports it.
        const defined = withDeclaredStages(
            viewport?.definedLevels() ?? { alt: [0], lod: [0] },
            sceneRef.current?.damageStages ?? []);
        setLevels(defined);

        // Geometry loaded and not one mesh reaching the screen is worth saying out loud. It is a
        // real authoring state - every sub-mesh tagged for a damage or detail level this model
        // never selects, or the whole hull attached to a bone that ships hidden - and it is
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
     * Damage smoke starts off: the hardpoint is intact, and a ship that smokes from every hardpoint the
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
                askForShader(missing);
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

    /**
     * Whether one effect should be drawing right now.
     *
     * THE one answer, because there are two callers - the pass below and `attachPending`, which
     * runs later when a system's data finally lands. They disagreed once: `attachPending` set
     * visibility from the damage rules alone and clobbered the ability decision made moments
     * earlier, so an ability's proxy was drawing before anyone switched it on. Two writers of one
     * flag is the same defect the effect-row work already paid for.
     */
    const effectDrawsNow = useCallback((particle: PreviewParticle): boolean => {
        const proxies = abilityProxyIdsRef.current;

        // A passive subject's effect answers to its own model and to nothing here. Neither of the
        // rules below is about it: the opening rule is about the first moment of a preview, and a
        // death clone has none - it exists because the ship died, and its explosions ARE the death.
        if (passiveEffectIdsRef.current.has(particle.id)) {
            return passiveEffectPlaysNow(particle);
        }

        // Before every other rule, because it is the only VETO among them: an effect whose name
        // claims an ability this unit does not declare has nothing that could ever switch it on,
        // and the game simply never shows it. Everything below is a DEFAULT or a gate - the
        // distinction three separate defects were spent learning. `Tartan_Patrol_Cruiser` opened
        // with its TURBO engines burning because an unbound proxy fell through to the ordinary
        // path, where an engine-named effect is exempt from the quiet-on-open rule.
        if (unboundEffectsRef.current.has(particle.id)) {
            return false;
        }

        // A cloaked unit shows its stealth shell and nothing else - not its engines, not its
        // damage smoke, not the proxies of the ability doing the cloaking. The meshes are swapped
        // by the row chain in the viewport; this is the same rule reaching the effects, which
        // answer to their own decider.
        // The engine glow going out with its hardpoint is NOT decided here. It arrives already gated:
        // the server joins the glow proxy to the engine hardpoint and marks it `HardpointAlive`,
        // and `hardpointGateAllows` below is what reads that. A second rule here, matching the
        // hardpoint's `Engine_Particles` bone against the proxy's own bone name, was the same
        // one-flag-two-gates mistake as the shield mesh - and it never matched anything on any
        // shipped ship, because the join is on the proxy's PARENT.

        // An ability REPLACES the quiet-on-open rule for the proxies it claims, rather than being
        // ANDed with it. The opening rules hold every non-engine effect back - rightly, on open -
        // but they are about the first moment, not a permanent veto, and ANDing them left an
        // activated ability changing nothing at all. The damage rules still apply either way.
        if (abilityClaims(particle.id, proxies)) {
            return hardpointGateAllows(particle, destroyedRef.current)
                && abilityAllows(particle.id, proxies, activeAbilitiesRef.current);
        }

        // An active ability REPLACING this effect's family, which is a different statement from
        // claiming it - see `replacedByAbility`. Below the claim branch on purpose: a proxy an
        // ability actually drives answers to that ability, not to this.
        if (replacedEffectsRef.current.has(particle.id)) {
            return false;
        }

        return effectPlaysNow(particle, destroyedRef.current);
    }, []);

    /**
     * Queues a passive subject's own effects, once its geometry is on the way.
     *
     * Through the SAME pending queue every other effect uses, which is what stage 3 of the
     * multi-subject work bought: the descriptor is re-homed onto the part that was loaded and then
     * there is nothing special about it. A death clone's explosions and a burning wreck's fire
     * trail could not be shown at all before, because nothing on the wire described them.
     */
    const queuePassiveEffects = useCallback(
        (particles: readonly PreviewParticle[], partId: string): void => {
            if (particles.length === 0) {
                return;
            }

            const placed = passiveEffects(particles, partId);

            for (const particle of placed) {
                passiveEffectIdsRef.current.add(particle.id);
            }

            pendingRef.current = [...pendingRef.current, ...placed];

            // One request per distinct system, however many proxies call for it - and a wreck's
            // are usually the ship's own, so most come back off the cache immediately.
            for (const name of new Set(placed.map(particle => particle.systemRef))) {
                if (requestedSystemsRef.current.has(name.toLowerCase())) {
                    continue;
                }

                requestedSystemsRef.current.add(name.toLowerCase());
                vscode.postMessage({ type: 'requestParticleSystem', name });
            }
        }, []);

    /**
     * Takes a passive subject's effects away with it.
     *
     * A wreck reaching the end of its lifetime and a clone removed by a repair both take their
     * geometry, and an effect left behind would go on burning at a part that no longer exists.
     */
    const dropPassiveEffects = useCallback((partId: string): void => {
        const mine = `${partId}#`;

        for (const id of [...passiveEffectIdsRef.current]) {
            if (!id.startsWith(mine)) {
                continue;
            }

            passiveEffectIdsRef.current.delete(id);
            viewportRef.current?.removeParticleSystem(id);
        }

        pendingRef.current = pendingRef.current.filter(
            particle => !particle.id.startsWith(mine));
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

            // The MODEL's own: it is in the scene's proxy list, it names a bone, and it is part of
            // what the asset is. This is the one call site that says so.
            viewport.addParticleSystem(particle.id, system, {
                origin: 'model',
                attachToPartId: particle.partId,
                attachBone: particle.bone,
                attachBoneIndex: particle.boneIndex,
                levels: {
                    alt: particle.alt ?? null,
                    lod: particle.lod ?? null,
                    altDecreaseStayHidden: particle.altDecreaseStayHidden ?? false,
                },
                scaleFactor: systemScalesRef.current.get(particle.systemRef.toLowerCase()) ?? 1,
            });

            viewport.setParticleSystemVisible(particle.id, effectDrawsNow(particle));
        }

        pendingRef.current = stillWaiting;

        // Everything the dock reads, not just the tree. Systems attach well after the geometry
        // does, and a model can declare its damage states ENTIRELY on proxies - `Rv_mptl-2a.alo`
        // has eleven ALT-tagged smoke and static effects and not one ALT-tagged mesh. Refreshing
        // only the tree left `levels` at what the meshes alone implied, so the Damage state control
        // never appeared and ALT looked unimplemented on exactly the models that lean on it hardest.
        refreshStats();

        for (const name of viewport.particleTextureNames()) {
            askForTexture(name);
        }
    }, [refreshStats, effectDrawsNow]);

    /**
     * Switches one weapon's arc on or off.
     *
     * Stores the weapons that are OFF, so the set is empty on a fresh subject however many hardpoints it
     * carries, and an id the reader never touched needs no entry at all.
     */
    const setWeaponArcs = useCallback((id: string, on: boolean): void => {
        // Switching ONE cone on opens the master gate if it is shut. Without this the button did
        // nothing visible - the weapon left the hidden set and the stage pill still vetoed every
        // cone on the model - which reads as a broken control rather than as a gate.
        //
        // Only in this direction. Pressing the master itself is still all-or-nothing, and switching
        // a single cone OFF must not take the gate down with it: the others are still drawn.
        if (on) {
            setFireArcs(true);
        }

        setHiddenWeapons(current => {
            if (current.has(id) !== on) {
                return current;
            }

            const next = new Set(current);
            if (on) {
                next.delete(id);
            } else {
                next.add(id);
            }

            return next;
        });
    }, []);

    /**
     * Points at a bone from somewhere that is not the tree.
     *
     * Sets exactly what a tree click sets - the row selection that draws the box, and the bone the
     * labels highlight - so a muzzle picked off a weapon row and the same bone picked in the tree
     * are one state rather than two that can disagree.
     */
    /**
     * Asks the host for everything the geometry now loaded needs in order to be DRAWN.
     *
     * Textures are asked for only once the geometry that samples them exists, so nothing is fetched
     * for a part that failed to load. Effects are fetched once each; a missing one is normal and
     * simply leaves the archetype material in place.
     *
     * Shared by every path that puts geometry in the scene rather than written out at the one that
     * came first. A wreck arriving is the same question as a hardpoint arriving, and the breakoff
     * path having its own early return - and so no textures - was only visible as a black shape.
     */
    const requestMaterialAssets = useCallback((viewport: PreviewViewport): void => {
        for (const name of collectTextureNames(viewport.materialExtras())) {
            askForTexture(name);
        }

        for (const shader of viewport.shaderNames()) {
            askForShader(shader);
        }
    }, [askForTexture, askForShader]);

    /**
     * Picks the hardpoint a targeting mark stands for, or lets it go.
     *
     * Aims the attacker at it, which is what the marks are FOR - they show what can be shot - and
     * selects its attachment bone so the viewport boxes it too.
     *
     * A second click on the same mark clears both again, exactly as a second click on a tree row or
     * a bone label does. Anything that can be selected has to be UN-selectable from the same
     * control: the first build only ever selected, so once a mark had been clicked there was no way
     * back to an unselected ship without going hunting in the tree.
     */
    const selectHardpoint = useCallback((hardpointId: string): void => {
        const bone = sceneRef.current?.hardpoints.find(h => h.id === hardpointId)?.attachBone;
        const rowId = bone === null || bone === undefined
            ? undefined
            : boneRowIndex(viewportRef.current?.treeItems() ?? []).get(bone.toLowerCase());

        setFireTarget(current => {
            const isSame = current === hardpointId;

            // The bone selection travels WITH the target - one press, one meaning - so it is
            // settled here rather than by a second reducer that could disagree about which hardpoint
            // is current.
            if (isSame) {
                setSelected(new Set());
                setAnchor(null);
            } else if (rowId !== undefined) {
                setSelected(new Set([rowId]));
                setAnchor(rowId);
            }

            return isSame ? 'hull' : hardpointId;
        });
    }, []);

    const selectBoneRow = useCallback((rowId: string | undefined): void => {
        if (rowId === undefined) {
            return;
        }

        // TOGGLES into the selection rather than replacing it. Replacing meant a weapon with two
        // fire points could never have both boxed at once - press the second and the first let go -
        // and comparing two fire points is the whole reason for pressing either.
        setSelected(current => {
            const next = toggleSelected(current, rowId);

            setAnchor(next.has(rowId) ? rowId : null);

            return next;
        });
    }, []);

    /**
     * Plays a named effect at an attachment, fetching it first if it is not in hand.
     *
     * The single route to a gameplay effect. Both callers used to have their own copy of the
     * have-it / ask-for-it dance and only one of them had the "play it when it lands" half, which
     * is why a wreck named its trailing fire and its death blast and showed neither.
     */
    const playEffect = useCallback((
        id: string, name: string, partId: string | undefined, bone: string | undefined,
        once: boolean,
        /**
         * Where to put it when it hangs on no part.
         *
         * A part-less effect otherwise sits at the model root, which is right for a ship's own death
         * blast - it goes off where the ship was - and wrong for a wreck that flew away first.
         */
        at?: { x: number; y: number; z: number },
    ): void => {
        if (name === '') {
            return;
        }

        const system = systemsRef.current.get(name.toLowerCase());

        if (system === undefined) {
            // Fetched now and played on arrival; an effect nothing referenced until this moment is
            // not worth loading up front for every hardpoint on a capital ship.
            explosionsRef.current.push({ id, system: name, partId, bone, once, at });
            vscode.postMessage({ type: 'requestParticleSystem', name });
            return;
        }

        const viewport = viewportRef.current;

        const scale = systemScalesRef.current.get(name.toLowerCase()) ?? 1;

        if (once) {
            viewport?.playOnce(id, system, partId, bone, scale, at);
        } else {
            // The Gameplay lens's: asked for BY NAME at the moment it is needed - a wreck's
            // trailing fire - so it is in no proxy list and belongs to no bone. Out of the model
            // tree, which is a list of what the asset is made of.
            viewport?.addParticleSystem(id, system, {
                origin: 'gameplay', attachToPartId: partId, attachBone: bone, scaleFactor: scale,
                at,
            });
        }
    }, []);

    /**
     * Destroys or repairs one hardpoint.
     *
     * The death explosion fires here rather than in the effect above, because it is an event: it
     * belongs to the moment of destruction, not to the state of being destroyed, and replaying it
     * every time the scene re-renders would leave a ship permanently exploding.
     */
    const setHardpointDestroyed = useCallback((
        id: string,
        isDestroyed: boolean,
        /**
         * Whether to write a line for it.
         *
         * False from the attacker, which logs its own shot with the real numbers - it knows the
         * damage type and how much actually landed, and this cannot. Everything else reaching this
         * is a HAND destroying a hardpoint, and that is exactly what wants a line.
         */
        log = true,
    ): void => {
        // Nothing to do, and that includes the EXPLOSION below. The guard used to live inside the
        // updater alone, so the set was left correct while everything after it ran again: firing at
        // a hardpoint that was already gone replayed its death blast and dropped a second wreck.
        // Reported. `destroyAll` leaned on it too, over every hardpoint at once.
        if (destroyedRef.current.has(id) === isDestroyed) {
            return;
        }

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

        const hardpoint = scene?.hardpoints.find(h => h.id === id);

        if (!isDestroyed) {
            // Repairing has to put back everything destroying took, or the two buttons are not
            // opposites. It used to remove the id from the destroyed set and stop there - so a
            // hardpoint shot to nothing came back with its bar still reading 0, and its wreckage
            // still tumbling away beside the hull it had just been restored to.
            //
            // ONE hardpoint's worth. The attacker's Repair target refills the SHIP's pools as well,
            // and those are not this hardpoint's to give back.
            setHardpointHealth(current => ({ ...current, [id]: hardpoint?.health ?? null }));

            viewportRef.current?.clearBreakoff(id);
            breakoffsRef.current = breakoffsRef.current.filter(entry => entry.id !== id);
            return;
        }


        // The wreckage the hardpoint sheds. 167 of foc's 355 hardpoints name one; the server has
        // resolved them since H1 and nothing read them, so a destroyed hardpoint used to just vanish.
        const prop = hardpoint === undefined
            ? null
            : breakoffFor(hardpoint, scene?.breakoffProps ?? []);

        if (hardpoint !== undefined && prop !== null
            && !(viewportRef.current?.hasBreakoff(id) ?? false)) {
            // Fetched on demand: a capital ship names a dozen distinct props and almost none of
            // them is ever dropped in a given session.
            // Resolved HERE, where the hardpoint is in hand, and carried whole: which node a wreck
            // is dropped on is a decision about the hardpoint, not about the scene graph.
            breakoffsRef.current.push({
                id, prop,
                at: breakoffAnchor('hull', hardpoint),
            });

            // The key rides on `partId`, which the host echoes back verbatim. The reply carries no
            // model name, and a breakoff must not be mistaken for a scene part - see the handler.
            vscode.postMessage({
                type: 'requestGlb',
                modelReference: prop.modelRef,
                partId: `${BREAKOFF_PART}${id}`,
                // ITS OWN clips, not the subject's. A prop's model shares nothing with the hull's
                // name, so the scene's list would never have held one of them.
                animations: prop.animations ?? [],
            });
        }

        playEffect(
            `explosion:${id}:${Date.now()}`, hardpoint?.deathExplosionParticles ?? '',
            'hull', hardpoint?.attachBone ?? undefined, true);

        // Destroying one by hand went unlogged, which made the log a record of SHOTS rather than of
        // what happened to the unit. There is no damage type and no armour factor here - the switch
        // is not a weapon, it just decides the hardpoint is gone - so it says so.
        if (log) {
            setDamageLog(current => appendShot(current, [{
                source: BY_HAND,
                amount: Number.POSITIVE_INFINITY,
                target: id,
                armor: null,
                pool: 'hull',
                destroyed: true,
            }]));
        }
    }, [scene, playEffect]);

    /** Destroys or repairs every hardpoint the XML allows to be destroyed. */
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
                refreshingRef.current = message.refresh === true;
                viewport.clear();
                setScene(message.scene);
                setProblems(message.scene.problems);
                setAnimation(null);
                setBoneFilter('');
                setCollapsed(new Set());
                setEmitters([]);
                setHiddenEmitters(new Set());
                setHiddenWeapons(new Set());
                setActiveAbilities(new Set());
                setProjectileSearch('');
                setProjectileNote(null);
                setSweeping(new Set());
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

                // The custom colour survives too, and used to be thrown away here on every subject.
                // It needs no "if this subject has it" guard: a hex is not a name out of the
                // roster, so it applies to anything. Storing it was dead code while this stood -
                // written on every save, read back on open, and nulled by the next scene message.
                setCustomColour(storedCustomColourRef.current);
                foldedRef.current = false;
                setMode(defaultMode(message.scene.kind, {
                    animations: 0,
                    hardpoints: message.scene.hardpoints.length,
                    particles: message.scene.particles.length,
                    weapons: message.scene.weapons.length,
                    abilities: message.scene.abilities?.length ?? 0,
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
                systemScalesRef.current.clear();
                requestedSystemsRef.current.clear();
                textureLedger.current.clear();
                shaderLedger.current.clear();

                // Cover the viewport again for the new subject. The expected count is the parts
                // that RESOLVED, because those are the only ones a GLB is asked for - counting the
                // rest would leave the cover waiting on geometry nobody requested.
                setTally({
                    expectedParts: message.scene.parts.filter(part => part.resolved).length,
                    arrivedParts: 0,
                    requestedAssets: 0,
                    settledAssets: 0,
                    timedOut: false,
                });
                // Defensive: a server older than this client sends no particles at all, and
                // spreading undefined throws inside the handler - which loses the whole scene,
                // geometry included, for the sake of an effects list.
                const particles = message.scene.particles ?? [];
                pendingRef.current = [...particles];
                // The old subject's wrecks are gone with it, and so are the ids they were keyed by.
                passiveEffectIdsRef.current.clear();

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

                // What is inside the HULL, for the inspector. Only the hull: an attached turret is a
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

                    // A part that failed is a part that is not coming. It still has to be counted
                    // or the cover would sit over the rest of the model until the wait timed out -
                    // and one unreadable turret is exactly the case where a reader most wants to
                    // see what DID load.
                    if (!message.partId.startsWith(BREAKOFF_PART)
                        && !message.partId.startsWith(DEATH_CLONE_PART)) {
                        setTally(current =>
                            ({ ...current, arrivedParts: current.arrivedParts + 1 }));
                    }

                    return;
                }

                // Wreckage waiting on this model, if any. Handled BEFORE the part path, because a
                // breakoff GLB carries no partId and would otherwise be added as a scene part
                // called `undefined` and framed as if it were the subject.
                if (message.partId.startsWith(BREAKOFF_PART)) {
                    const key = message.partId.slice(BREAKOFF_PART.length);
                    const pending = breakoffsRef.current.find(entry => entry.id === key);

                    breakoffsRef.current = breakoffsRef.current.filter(
                        entry => entry.id !== key);

                    if (pending !== undefined) {
                        await viewport.addBreakoff(pending.id, glb, pending.at, pending.prop);

                        // The fire the piece trails. Attached to the WRECK, not to the hardpoint it
                        // came off: 86 of the 90 shipped props name one, and every one of them was
                        // being ignored, so debris tumbled away cold.
                        playEffect(
                            `${WRECK_EFFECT}${pending.id}`,
                            pending.prop.attachedParticle ?? '',
                            `${BREAKOFF_ATTACHMENT}${pending.id}`, undefined, false);

                        // And the effects the prop's MODEL carries, which are a separate thing
                        // from `Debris_Attached_Particle` above - the XML names one, the ALO
                        // carries however many the artist pinned to its bones.
                        queuePassiveEffects(pending.prop.particles ?? [],
                            `${BREAKOFF_ATTACHMENT}${pending.id}`);

                        // Debris needs its textures and its shaders like any other geometry. The
                        // first build returned here instead, so a wreck was drawn on a bare
                        // archetype material with no map bound at all - a black shape tumbling out
                        // of the gap, which is exactly how it looked. A prop model is usually a
                        // piece of the hull it fell off, so most of these are already decoded and
                        // arrive back immediately.
                        requestMaterialAssets(viewport);
                    }

                    return;
                }

                // The wreck the ship leaves behind. Loaded as its own part; the hull it replaces
                // was already hidden by the death watch that asked for this, since the ship dies
                // whether or not a clone answers.
                if (message.partId.startsWith(DEATH_CLONE_PART)) {
                    // Its own PASSIVE SUBJECT - a wreck is a replacement subject, not another piece
                    // of the ship being previewed, and the active subject's hardpoint answers must
                    // not reach it by mesh name.
                    await viewport.addPart(message.partId, glb, undefined, undefined,
                        { subjectId: message.partId });

                    // The explosions, the fire smoke and the debris trails the clone's own model
                    // carries - eight of them on the Star Destroyer's wreck, and not one reached
                    // the client until its descriptor carried them.
                    queuePassiveEffects(
                        sceneRef.current?.deathClones?.find(
                            c => `${DEATH_CLONE_PART}${c.objectId}` === message.partId)
                            ?.particles ?? [],
                        message.partId);

                    requestMaterialAssets(viewport);

                    // FETCHED EARLY, held hidden. Asking for it at the moment of death left a
                    // visible gap between the ship going and the wreck arriving - a capital ship's
                    // clone is three megabytes. Shown and started by the death watch.
                    if (deadRef.current) {
                        viewport.startDeathClip(message.partId);
                    } else {
                        viewport.setPartHidden(message.partId, true);
                    }

                    return;
                }

                await viewport.addPart(
                    message.partId, glb,
                    message.attachToPartId ?? undefined, message.attachBone ?? undefined);

                // Counted here rather than at every `glb`: a wreck, a death clone and a breakoff
                // prop all arrive down this same message and have already returned above. Only a
                // scene part is one of the parts the cover is waiting for.
                setTally(current => ({ ...current, arrivedParts: current.arrivedParts + 1 }));

                // The bound shot, if this subject has one, INSTEAD of the default framing - not
                // after it, so the model does not visibly jump from one to the other on open.
                // Always overridable by hand afterwards: a rule is a default, not a cage.
                //
                // Skipped entirely on a refresh. Framing is an OPENING rule: it answers "I have
                // never seen this before, show me all of it". A reader who edited a file while
                // looking closely at a turret has already answered that question, and re-framing
                // would throw the answer away every time they saved.
                if (!refreshingRef.current && !applyBoundCamera(viewport)) {
                    viewport.frameAll();
                }

                refreshStats();

                requestMaterialAssets(viewport);

                // This part's bones now exist, so anything waiting to attach to them can.
                attachPending();

                // And whatever STILL has nowhere to hang. `attachmentFor` draws a bone it cannot
                // find at the owning model's origin and says nothing, which hid two separate
                // misplacement bugs.
                //
                // REPLACED, not appended. The list is recomputed per arriving part and a part that
                // lands later does drop off it - but the panel was accumulating every answer it
                // had ever been given, so a subject whose parts arrive one at a time reported all
                // the ones that had not arrived YET and never took them back. Measured on the
                // Executor, whose 30 parts produced 70 warnings about parts that were all present
                // by the time anyone read them, against a live count of zero.
                setAttachmentIssues(viewport.unresolvedAttachments());

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

            // The inspector tab was closed, so no row is being inspected any more - the mark on
            // the row it was showing comes off. Told rather than inferred: the tab is its own
            // editor and the reader can close it from the tab bar, which this side cannot see.
            if (message.type === 'inspectorClosed') {
                setDetailsRow(null);
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

                // A particle file opened directly is its own subject and attaches to nothing.
                //
                // Anything WAITING on this system says it is not that: a death explosion and a
                // wreck's trailing fire are both asked for by name at the moment they are needed,
                // and neither is in the scene's proxy list. Without the third test they took this
                // branch - so the blast was parented to the scene root instead of to the thing
                // that blew up, and it called `frameAll`, yanking the camera off the subject.
                const awaited = explosionsRef.current.some(
                    pending => pending.system.toLowerCase() === message.name.toLowerCase());

                if (!awaited && pendingRef.current.length === 0 && !requestedSystemsRef.current.has(
                    message.name.toLowerCase())) {
                    // A particle FILE opened on its own IS the subject, so it is model-owned - and
                    // it has no hull, so it reaches no model tree either way.
                    viewport.addParticleSystem(message.name, system, { origin: 'model' });
                    viewport.frameAll();

                    // Heat emitters are labelled rather than hidden: they draw as a distortion of
                    // the frame behind them, which is easy to miss and easy to mistake for a bug
                    // in the effect if the row does not say what it is.
                    setEmitters(system.emitters.map(emitter => emitter.name
                        + (emitter.properties.isHeatParticle ? ' [heat]' : '')));

                    // Asked for only now, because the system is what names them.
                    for (const name of viewport.particleTextureNames()) {
                        askForTexture(name);
                    }

                    return;
                }

                systemsRef.current.set(message.name.toLowerCase(), system);
                systemScalesRef.current.set(
                    message.name.toLowerCase(), message.result.scaleFactor ?? 1);

                // A death explosion asked for at the moment of destruction plays as soon as it lands.
                const waiting = explosionsRef.current
                    .filter(pending => pending.system.toLowerCase() === message.name.toLowerCase());

                if (waiting.length > 0) {
                    explosionsRef.current = explosionsRef.current.filter(
                        pending => !waiting.includes(pending));

                    // The scale that arrived with THIS system, not a lookup: an effect asked for
                    // before its system landed takes the same route as one asked for after, and
                    // the two disagreeing is exactly how a wreck's fire came back unscaled.
                    const scale = message.result.scaleFactor ?? 1;

                    for (const pending of waiting) {
                        if (pending.once) {
                            viewport.playOnce(
                                pending.id, system, pending.partId, pending.bone, scale,
                                pending.at);
                        } else {
                            viewport.addParticleSystem(pending.id, system, {
                                origin: 'gameplay',
                                attachToPartId: pending.partId,
                                attachBone: pending.bone,
                                scaleFactor: scale,
                                at: pending.at,
                            });
                        }
                    }
                }

                attachPending();

                return;
            }

            if (message.type === 'previewFeatures') {
                // Energy DEFAULTS OFF and every read goes through this, so a panel that never
                // receives the message - an older host, or one lost on startup - stays in the state
                // the shipped game is in rather than offering a mechanic no player will ever see.
                const features = message.features as { energyPool?: unknown } | null | undefined;

                setEnergyPool(features?.energyPool === true);
                return;
            }

            if (message.type === 'viewerSettings') {
                // The room the reader left behind. Applied before any geometry arrives, so nothing
                // visibly snaps into place a frame later.
                const room = viewerSettingsFrom(message.settings);

                // And what THIS PROJECT remembers, which travels beside the room rather than
                // inside it - the two are at different tiers. See `projectSettings.ts`.
                const project = projectSettingsFrom(message.project);

                setAttacker(project.attacker);
                setAttackerPresets(project.presets);
                setCustomColour(project.customColour);

                // By NAME, and only if this subject has it: "I am reviewing the Rebel roster"
                // should survive the next unit and fall away quietly when it does not apply.
                storedFactionRef.current = project.faction;
                storedCustomColourRef.current = project.customColour;

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

                storedSubjectRef.current = message.subject === null
                    ? null
                    : subjectStateFrom(message.subject);

                restoredRef.current = true;
                return;
            }

            if (message.type === 'projectile') {
                // The answer to a "fill from a projectile" pick the scene could not resolve.
                //
                // Applied through `attackerFromProjectile`, the SAME reader the resolved ones go
                // through, so a bolt fetched on demand and one that came with the scene fill the
                // panel identically. The bolt itself is not kept in `scene.projectiles`: the fill is
                // a copy and the reader's edits afterwards have to stick.
                const bolt = message.result.projectile ?? null;

                if (bolt === null) {
                    setProjectileNote(message.result.error
                        ?? `${message.name} could not be read.`);
                } else {
                    setAttacker(current => attackerFromProjectile(bolt, current));
                    setAttackerProjectile(bolt.id);
                    setProjectileNote(null);
                }
                return;
            }

            if (message.type === 'shader') {
                const source = message.result.source ?? null;
                shaderSourcesRef.current.set(message.name.toLowerCase(), source);

                // Settled before any of the branches below, several of which return early. An
                // effect that is simply not there is still an answer, and the wait is over.
                shaderLedger.current.settle(message.name);
                syncTally();

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

                // Eleven of the shipped effects are render state and nothing else - `MeshAlpha.fx`
                // among them, whose two programmable techniques are commented out in the file. That
                // used to be reported as an info problem per effect. It is a true statement about
                // the DATA that the reader can do nothing with, and eleven of them crowd out the
                // problems that mean something; the translated count in the dock already says how
                // much of the model draws with its own shaders.

                // A header may have arrived after the effect that wanted it, so every effect still
                // waiting is retried whenever anything new lands.
                translatePending();

                return;
            }

            if (message.type === 'texture') {
                // Before the decode, and regardless of how it goes: a texture that did not resolve
                // is an ANSWER. The problems bar is what tells the reader it is missing; the cover
                // only needs to know it is no longer waiting.
                textureLedger.current.settle(message.name);
                syncTally();

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

        // A clip the loaded model does not carry must not go on being shown as the one playing.
        // The transport, the playhead and the readout all key off this one string, so a name the
        // mixer refused made the panel report an animation that was never running - which is what
        // an ability's deploy did on every model until `clipFor` stopped handing over a file name.
        // Rare now, and worth keeping: a mod can name a clip its model does not ship.
        //
        // Only once the subject HAS clips, which is the whole guard. A refresh restores the clip
        // that was playing at scene time, before any geometry has arrived - so an unguarded clear
        // would throw away the restored name every time, on the strength of a mixer that had
        // nothing loaded to refuse it with.
        const loaded = viewportRef.current?.animationNames ?? [];
        const started = viewportRef.current?.play(animation) ?? false;

        if (animation !== null && !started && loaded.length > 0) {
            setAnimation(null);
        }

        setTreeItems(viewportRef.current?.treeItems() ?? []);
        // `levels` is read but deliberately NOT a dependency: this fires when the CLIP changes, and
        // adding it would reset the model every time the defined levels were recomputed - which
        // happens on every load and every level change.
    }, [animation]);

    useEffect(() => {
        viewportRef.current?.setSkeletonVisible(skeletonOn);
    }, [skeletonOn]);

    /**
     * Which particle ids each ability drives.
     *
     * Resolved from proxy BONE NAMES to ids here, once: the server binds by name, and a name is not
     * an identity - a Star Destroyer carries twenty proxies sharing one.
     */
    const abilityProxyIds = useMemo(
        () => abilityProxies(scene?.abilities ?? [], scene?.particles ?? []),
        [scene?.abilities, scene?.particles]);

    const abilities = useMemo(
        () => abilityRows(scene?.abilities ?? [], abilityProxyIds),
        [scene?.abilities, abilityProxyIds]);

    /**
     * The effects whose ability this unit never declares, which nothing can switch on.
     *
     * Not the complement of {@link abilityProxyIds}: that map holds only the BOUND proxies, so an
     * unbound one is simply absent from it and absence is what let it fall through to the ordinary
     * path and light up.
     */
    const unboundEffects = useMemo(
        () => unboundEffectIds(scene?.abilities ?? [], scene?.particles ?? []),
        [scene?.abilities, scene?.particles]);

    /**
     * The effects an ACTIVE ability replaces - turbo engines standing in for the plain ones.
     *
     * Recomputed with the active set rather than held, because it is a statement about right now.
     */
    const replacedEffects = useMemo(
        () => replacedByAbility(scene?.particles ?? [], abilityProxyIds, activeAbilities),
        [scene?.particles, abilityProxyIds, activeAbilities]);

    // Kept in step for the decider, which the message handler reads through refs.
    abilityProxyIdsRef.current = abilityProxyIds;
    activeAbilitiesRef.current = activeAbilities;
    unboundEffectsRef.current = unboundEffects;
    replacedEffectsRef.current = replacedEffects;

    /**
     * Switches one ability on or off.
     *
     * Its proxies follow through the effect pass above; the CLIP is fired here, because it is an
     * event rather than a state - replaying the deploy on every re-render would leave a model
     * permanently deploying, which is the same shape as the death-explosion rule beside it.
     */
    const setAbilityActive = useCallback((type: string, on: boolean): void => {
        // Before the state changes, so the chain re-runs once with both the override gone and the
        // new gate in place.
        //
        // Every channel an ability drives - its proxies' gate, the shield mesh, the stealth shell -
        // enters the visibility chain at the `file` link, four below the reader. So one tick on such
        // a row in Model mode outranked this switch for good: it fired, the chain ignored it, and
        // only a whole-model Reset could undo it. Switching the ability takes ITS OWN rows back,
        // which is the rule a starting clip already follows - scoped, because a mesh hidden for an
        // unrelated reason has to survive it.
        viewportRef.current?.releaseAbilityRows(
            abilityOwnership(type, abilityProxyIdsRef.current));

        setActiveAbilities(current => {
            if (current.has(type) === on) {
                return current;
            }

            const next = new Set(current);
            if (on) {
                next.add(type);
            } else {
                next.delete(type);
            }

            return next;
        });

        const declared = scene?.abilities?.find(a => a.type === type);
        const clip = declared === undefined ? null : clipFor(declared, on);

        // 25 models ship a deploy and only 23 the matching undeploy, so a missing clip is ordinary:
        // the ability still switches, it simply has no animation for that direction.
        if (clip !== null) {
            setAnimation(clip);
        }
    }, [scene]);


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

        // Never shown in either damage state, so this takes no `destroyed` and does not change with
        // it - it is set here because this is the pass that owns hardpoint-driven visibility.
        viewport.setCollisionMeshes(collisionMeshNames(scene.hardpoints));


        // The ordinary effect rules AND the ability decider. Through the same call as every other
        // effect deliberately: a held proxy then rides the emission gate, so its burst does not
        // burn away unseen and come back exhausted the moment the ability is switched on.
        // The shield mesh is the one piece of GEOMETRY an ability reveals - DEFEND shows it, and
        // the mesh ships hidden, so nothing else would ever bring it up.
        viewport.setShieldRevealed(shieldRevealed(activeAbilities));

        // The cloak's other half. The shell swaps in here and the effects go dark below, because
        // a cloaked unit shows the shell and nothing else at all.
        viewport.setStealthed(stealthed(activeAbilities));

        for (const particle of scene.particles ?? []) {
            viewport.setParticleSystemVisible(particle.id, effectDrawsNow(particle));
        }

        // Both the tree and the group switches read out of the systems that just changed, so this
        // is all it takes to bring the dock along. Destroying a hardpoint lights its smoke and every row
        // that names it says so.
        setTreeItems(viewport.treeItems() ?? []);
    }, [destroyed, scene, abilityProxyIds, activeAbilities, effectDrawsNow]);

    /**
     * Everything the target has left, rebuilt whenever the scene changes.
     *
     * TIER 2 - it resets on open, because the opening rules beat persistence and a fresh preview
     * opens undamaged. Rebuilt from the scene rather than carried, so a subject that declares no
     * pools at all reads as zero rather than as the last ship's numbers.
     */
    useEffect(() => {
        setPools(fullPools(scene));

        setHardpointHealth(Object.fromEntries(
            (scene?.hardpoints ?? []).map(h => [h.id, h.health ?? null])));

        setFireTarget('hull');
        setAttackerProjectile(null);
        setDamageLog([]);
        requestedClonesRef.current.clear();
    }, [scene]);

    /** Whether the subject has been finished off, and by which clone. */
    const [unitDead, setUnitDead] = useState(false);

    /**
     * Puts the scene back the way it was before the subject died.
     *
     * The inverse of the death watch, in ONE place: the ship's parts come back, the wreck and
     * everything attached to it goes, and the clone is forgotten so it is fetched again. Forgetting
     * it is what lets the death play a second time - the request is guarded against asking twice,
     * and that guard outlived the wreck it was about.
     *
     * The re-fetch is not a cost at the moment of death: the prefetch runs on `unitDead` going
     * false, so the wreck is back on the shelf long before anything can kill the ship again.
     */
    const restoreFromDeath = useCallback((): void => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        // The wreck comes home BEFORE the parts are shown again. A unit that spun away is sitting
        // wherever it finished, and un-hiding it there would put the ship back some distance off
        // the grid, still rolled - which is what every route back would have looked like.
        viewport.clearSpinAway();

        for (const part of sceneRef.current?.parts ?? []) {
            viewport.setPartHidden(part.id, false);
        }

        for (const clone of sceneRef.current?.deathClones ?? []) {
            dropPassiveEffects(`${DEATH_CLONE_PART}${clone.objectId}`);
            viewport.removePart(`${DEATH_CLONE_PART}${clone.objectId}`);
        }

        requestedClonesRef.current.clear();
    }, [dropPassiveEffects]);

    /** Read by the glb handler, which has to know whether a clone arriving is already needed. */
    const deadRef = useRef(false);

    useEffect(() => {
        deadRef.current = unitDead;
    }, [unitDead]);

    /** Clone models already asked for, so a damage-type change does not re-fetch one. */
    const requestedClonesRef = useRef(new Set<string>());

    /**
     * Fetches the wreck this weapon would leave, BEFORE it is needed.
     *
     * The death clone is the one piece of geometry whose arrival is watched - the ship vanishes and
     * the reader waits for the wreck. A capital ship's clone is three megabytes, so asking for it at
     * the moment of death shows as a gap. Asked for as soon as the damage type says which one it
     * would be, and held hidden by the glb handler until the death watch wants it.
     *
     * Re-runs on a damage-type change, because that changes the answer; each model is asked once.
     */
    useEffect(() => {
        // `unitDead` is a dependency so this runs again when the ship comes BACK: the restore has
        // just forgotten the clone, and this is what puts it on the shelf ready for the next death.
        const clone = cloneForDamage(scene?.deathClones ?? [], attacker.damageType);
        const model = clone?.modelFile ?? '';

        if (clone === null || model === '') {
            return;
        }

        const partId = `${DEATH_CLONE_PART}${clone.objectId}`;

        if (requestedClonesRef.current.has(partId)) {
            return;
        }

        requestedClonesRef.current.add(partId);
        vscode.postMessage({
            type: 'requestGlb', modelReference: model, partId,
            // ITS OWN clips. That a clone ever played at all was an accident of naming: its model
            // is conventionally the hull's name plus a suffix, so its `_die_00.ala` matched the
            // hull's stem and rode along in the subject's list.
            animations: clone.animations ?? [],
        });
    }, [scene, attacker.damageType, unitDead]);

    /**
     * The pools, but only while they still describe the subject on stage.
     *
     * `setPools` runs from an effect and `scene` changes during a render, so there is always one
     * commit where the two disagree - and at MOUNT the effect leaves every pool at 0, which read
     * against the first real subject's declared health is a unit dead on arrival. That is what
     * "loading a unit without hardpoints immediately plays the death explosion" was. Everything
     * that READS a pool goes through here; the writers below run from a click, by which time the
     * two have long since agreed.
     */
    const livePools = useMemo(() => poolsFor(scene, pools), [scene, pools]);

    /**
     * The unit's hull bar.
     *
     * Derived rather than stored: it is a VIEW of the hardpoint healths, and a second copy would
     * drift from them the first time anything else changed a hardpoint.
     *
     * Declared ABOVE the death watch because the watch reads it - a unit with no hardpoints dies
     * when this empties, and there is nothing else that could tell it so.
     */
    const hull = useMemo(
        // `pools.hull` is the LIVE number for a unit with no hardpoints - the one the attacker
        // panel depletes. Without it the bar drew Tactical_Health forever and a reader could shoot
        // such a unit all day against a full bar.
        () => hullPool(
            scene?.defence, scene?.hardpoints ?? [], hardpointHealth, destroyed, livePools?.hull),
        [scene, hardpointHealth, destroyed, livePools?.hull]);

    /**
     * Whether the HULL is choosing the damage stage rather than the reader.
     *
     * Only when there is a table to choose with: an object that declares none keeps the manual
     * slider in Gameplay too, which is better than a dead control that explains nothing.
     */
    const altDrivenByHull = mode === 'gameplay' && (scene?.damageTable ?? []).length > 0;

    /**
     * Drives the damage STAGE from the hull, which is what the damage table is for.
     *
     * `Land_Damage_Thresholds` was read by nothing at all before this: the table was parsed for
     * which stages EXIST and its thresholds thrown away, and the ALT slider was hidden in Gameplay
     * on the stated grounds that "the damage state follows from which hardpoints have been
     * destroyed". Nothing made it follow. The stages were simply unreachable there.
     *
     * Gameplay only. In Model mode the slider IS the control, and a hull that has not been shot at
     * would drag it back to zero the moment the reader moved it.
     */
    useEffect(() => {
        if (mode !== 'gameplay') {
            return;
        }

        const table = scene?.damageTable ?? [];
        if (table.length === 0) {
            return;
        }

        setAlt(stageForHull(table, hull.max > 0 ? hull.current / hull.max : 1));
    }, [mode, scene, hull.current, hull.max]);

    /**
     * Kills the unit when its hardpoints are gone, or when its own hull pool empties.
     *
     * The rule as the user gave it: a unit with hardpoints cannot be targeted itself, and it dies
     * when ALL of them are dead - untargetable ones included. The game warns about untargetable
     * but destructible, and at least two mods use the combination deliberately, so a hardpoint the
     * reticles never offered still has to die before the ship does.
     *
     * An EVENT, like the hardpoint death explosion: it belongs to the moment the last hardpoint goes,
     * not to the state of being dead, so it fires from the transition rather than from a render.
     */
    useEffect(() => {
        // The hull pool as well as the hardpoints. A unit with no destructible hardpoints dies by
        // its own health, and until that was passed in it could not die at all - which is most
        // units: 188 carry their weapons as WEAPON behaviour and no hardpoints, against 68 with.
        const dead = unitDestroyed(scene?.hardpoints ?? [], destroyed, hull);

        if (!dead) {
            // Coming BACK from death, and only on the transition. This is the exact inverse of what
            // the branch below does, and it lives here so that EVERY route back runs it - the
            // attacker panel's Repair, the Hardpoints section's `Repair all`, or the reader simply
            // un-ticking one hardpoint. `Repair all` is `destroyAll(false)` and nothing else, so with
            // the restore living in `repairTarget` it put the hardpoints back and left the ship hidden
            // under its own wreck, with the wreck's clip clamped at its last frame - after which no
            // death ever played again.
            if (unitDead) {
                restoreFromDeath();
            }

            setUnitDead(false);
            return;
        }

        if (unitDead) {
            return;
        }

        setUnitDead(true);

        // What the SHIP itself sets off. Its own `Death_Explosions`, which is a third thing from a
        // hardpoint's and a breakoff prop's - it goes off where the ship was, so it attaches to the
        // scene rather than to any part. It has to: every part is about to be hidden, and three
        // prunes a hidden subtree, effects included.
        playEffect(
            `deathblast:${Date.now()}`, scene?.deathExplosions ?? '',
            undefined, undefined, true);

        // Which wreck it leaves depends on what KILLED it, which is the weapon in the attacker
        // panel - so the clone follows the damage type set there.
        const clone = cloneForDamage(scene?.deathClones ?? [], attacker.damageType);

        // SPINNING AWAY: the automated death clone, for a unit that declares none. Measured over
        // both trees, not one of the 34 objects that declare `Spin_Away_On_Death` also declares a
        // `Death_Clone`, so the two are alternatives rather than things that stack - and a clone,
        // where there is one, wins.
        //
        // The hull STAYS on screen for this, which is the whole point: it flies on along its own
        // forward axis, corkscrewing, and comes apart at the end of `Spin_Away_On_Death_Time`.
        const spin = scene?.spinAway ?? null;
        const hullPart = (scene?.parts ?? []).find(part => part.origin === 'Hull')
            ?? (scene?.parts ?? [])[0];

        if (clone === null && spin !== null && hullPart !== undefined) {
            viewportRef.current?.startSpinAway(hullPart.id, spin, () => {
                // Its OWN explosion - `Spin_Away_On_Death_Explosion`, a different tag from
                // `Death_Explosions` - fired where the wreck finished rather than where it died.
                playEffect(
                    `spinaway:${Date.now()}`, spin.explosion ?? scene?.deathExplosions ?? '',
                    // Where the wreck FINISHED, not where it died. With no position it goes to the
                    // model root, so a fighter that flew 270 units away blew up back at the origin.
                    undefined, undefined, true, spinAwayEnd(spin));

                for (const part of scene?.parts ?? []) {
                    viewportRef.current?.setPartHidden(part.id, true);
                }
            });

            return;
        }

        // The ship is GONE, and that does not wait on a wreck being authored for this damage type.
        // The engine swaps the death clone in for the hull, so the hull goes either way - a clone
        // that does not match the damage type means nothing replaces it, not that it survives.
        for (const part of scene?.parts ?? []) {
            viewportRef.current?.setPartHidden(part.id, true);
        }

        if (clone?.modelFile === null || clone?.modelFile === undefined) {
            return;
        }

        // Usually already here and waiting: the prefetch below asks for it as soon as the damage
        // type says which one it would be. Showing it is all that is left.
        const partId = `${DEATH_CLONE_PART}${clone.objectId}`;

        viewportRef.current?.setPartHidden(partId, false);
        viewportRef.current?.startDeathClip(partId);
    }, [scene, destroyed, hull, unitDead, attacker.damageType, playEffect, restoreFromDeath]);

    /**
     * Whether the ship itself is a legal target.
     *
     * False as soon as it has a destructible hardpoint: the engine offers the hardpoints and nothing else,
     * so "The ship" would be aiming at something no weapon can reach. Disabled rather than removed
     * - the reader needs to see that the choice exists and why it is not available.
     */
    const shipTargetable = useMemo(
        () => unitTargetable(scene?.hardpoints ?? []), [scene]);

    /**
     * The target's defence as it stands NOW.
     *
     * A ship whose generators are gone is unshielded for every purpose a hit cares about: the
     * shield does not absorb, it does not stop a shield-only bolt, and a hitpoint bolt has nothing
     * to bypass. Applied here rather than at each of the four places that resolve a hit, so they
     * cannot come to disagree about it.
     */
    const liveDefence = useMemo(
        () => scene?.defence === null || scene?.defence === undefined
            ? scene?.defence
            : shieldsDown ? { ...scene.defence, isShielded: false } : scene.defence,
        [scene, shieldsDown]);

    /**
     * Fires the configured weapon at whatever is selected.
     *
     * The arithmetic lives in `attacker.ts`; this only routes the answer. A hardpoint reaching zero is
     * handed to the destruction path that already exists, so a shot that kills a hardpoint hides its
     * model, shows its decal, plays its explosion once and drops its breakoff prop - none of which
     * is new here.
     */
    const fire = useCallback(() => {
        const defence = liveDefence;
        const current = livePools;

        if (defence === null || defence === undefined || current === null) {
            return;
        }

        // Off first, so a second press inside the flash restarts it rather than
        // extending the first - the timer below is keyed on the flag going true.
        setJustFired(false);
        requestAnimationFrame(() => setJustFired(true));

        // What the shot is CALLED in the log: the projectile where one was picked, otherwise the
        // damage type, which is the only name a hand-built weapon has.
        const source = (scene?.projectiles ?? [])
            .find(p => p.id === attackerProjectile)?.id ?? attacker.damageType;

        if (fireTarget === 'hull') {
            const after = resolveHit(attacker, defence, current);

            setPools(after);

            // MEASURED, not recomputed. The difference the shot made to each pool is what actually
            // happened; working the numbers out a second time here would be a second copy of the
            // damage rules, free to disagree with the one that did the work.
            setDamageLog(log => appendShot(log, [
                { pool: 'shield' as const, took: current.shield - after.shield },
                { pool: 'energy' as const, took: current.energy - after.energy },
                { pool: 'hull' as const, took: current.hull - after.hull },
            ]
                .filter(hit => hit.took > 0 || hit.pool === 'hull')
                .map(hit => ({
                    source,
                    amount: hit.took,
                    target: scene?.subject ?? 'the unit',
                    armor: hit.pool === 'shield'
                        ? defence.shieldArmorType ?? null
                        : hit.pool === 'energy' ? null : defence.armorType ?? null,
                    pool: hit.pool,
                    destroyed: hit.pool === 'hull' && after.hull <= 0 && current.hull > 0,
                }))));

            return;
        }

        // Who this shot reaches. ONE path, whether or not a projectile was picked: the picker fills
        // the attacker's blast fields and the FIELDS are what fires. It used to branch, and the
        // no-projectile branch hand-built a single hit with no blast - so a reader who typed a
        // blast radius by hand was simulating a bolt that did not go off.
        const positions = Object.fromEntries((scene?.hardpoints ?? []).map(hardpoint => [
            hardpoint.id,
            viewportRef.current?.bonePosition(
                hardpoint.partId ?? undefined, hardpoint.attachBone ?? undefined) ?? null,
        ]));

        const hits = blastVictims(
            attackerProjectileSpec(attacker),
            fireTarget, candidatesFrom(positions, fireTarget), destroyed);

        const result = fireBlast(attacker, defence, current, hits, hardpointHealth);

        setPools(result.pools);
        setHardpointHealth(result.hardpointHealth);

        // A hardpoint declares no armour of its own - measured, 0 of them do - so it is hull
        // geometry and takes the HULL's column. Saying so on every line is what makes the log
        // answer "why did that number come out like that".
        setDamageLog(log => appendShot(log, [
            // The shield first where it took anything: it is a different event from the hull hit
            // that followed, and one line saying both would hide the bypass rule entirely.
            ...(current.shield - result.pools.shield > 0
                ? [{
                    source,
                    amount: current.shield - result.pools.shield,
                    target: scene?.subject ?? 'the unit',
                    armor: defence.shieldArmorType ?? null,
                    pool: 'shield' as const,
                    destroyed: false,
                }]
                : []),
            // Then each victim, nearest out - the order the blast reached them, which is the order
            // the damage was applied in.
            ...hits.map(hit => ({
                source,
                amount: (hardpointHealth[hit.id] ?? 0)
                    - (result.hardpointHealth[hit.id] ?? 0),
                target: hit.id,
                armor: defence.armorType ?? null,
                pool: 'hull' as const,
                destroyed: result.destroyed.has(hit.id),
            })),
        ]));

        for (const id of result.destroyed) {
            // Already logged above, with the real damage type and the number that actually landed.
            setHardpointDestroyed(id, true, false);
        }

        // The target is GONE, so stop aiming at it. Firing again at a hardpoint that is already off
        // logged a "did no damage" line and replayed its death blast - reported. The blast is fixed
        // at its root in `setHardpointDestroyed`; this is the other half, and the useful one: the
        // engine offers no such target either, so the panel should not keep it under the crosshair.
        if (result.destroyed.has(fireTarget)) {
            setFireTarget('hull');
        }
    }, [attacker, attackerProjectile, scene, liveDefence, livePools, hardpointHealth, fireTarget,
        destroyed, setHardpointDestroyed]);

    /** Puts the target back together without touching the weapon you built. */
    const repairTarget = useCallback(() => {
        // The hardpoints are back, so the wreckage they shed goes with them.
        viewportRef.current?.clearBreakoffs();
        breakoffsRef.current = [];

        // The wreck and the hidden ship are NOT this function's to put back. The death watch owns
        // that, because `Repair all` never comes through here at all - see `restoreFromDeath`.

        setPools(fullPools(scene));
        setHardpointHealth(Object.fromEntries(
            (scene?.hardpoints ?? []).map(h => [h.id, h.health ?? null])));
    }, [scene]);

    /**
     * The weapon weapons, as both the dock and the viewport see them.
     *
     * One derivation feeding both. Building the dock's rows and the viewport's cones separately is
     * what let a hardpoint's arc keep hanging in the air after the hardpoint it belongs to had been
     * blown off - two readings of the same damage state, and only one of them updated.
     */
    const weapons = useMemo(
        () => weaponRows(
            { weapons: scene?.weapons ?? [], hardpoints: scene?.hardpoints ?? [] }, destroyed),
        [scene?.weapons, scene?.hardpoints, destroyed]);

    /**
     * Every turret that can actually be swung, from BOTH places one can be declared.
     *
     * The AT-AA's is on its unit WEAPON and it has no hardpoints at all, so reading hardpoints alone
     * found nothing to sweep on the very unit this exists for.
     */
    const sweepable = useMemo(
        () => turretSweeps(
            (scene?.hardpoints ?? []).map(h => ({
                id: h.id, partId: h.partId ?? 'hull', turret: h.turret,
            })),
            weapons.map(w => ({ id: w.id, partId: w.partId, turret: w.turret }))),
        [scene, weapons]);

    /* One card per hardpoint, each carrying the weapon on it, and whatever weapons are left over. */
    const cards = useMemo(
        () => hardpointCards(
            { weapons: scene?.weapons ?? [], hardpoints: scene?.hardpoints ?? [] }, destroyed),
        [scene, destroyed]);

    const looseWeapons = useMemo(() => unitWeapons(weapons), [weapons]);

    const hardpointSearchOpen = searchIsOpen(hardpointSearchPressed, hardpointFilter);



    /* Gathered by type, and narrowed by the filter. A Star Destroyer's eight laser hardpoints are
       eight cards that differ only by which corner they sit on. */
    const hardpointGroups = useMemo(
        () => groupHardpoints(cards, hardpointFilter), [cards, hardpointFilter]);

    /* A flyout is anchored to a card, so it cannot outlive the card being on screen - filtering it
       away or folding its group leaves a panel pinned to nothing, describing something the reader
       can no longer see. The same rule the tree's details flyout follows.

       BOTH kinds of card, since the unit weapons got the info button too. Asking the hardpoint
       groups alone shut a weapon card's flyout the instant it opened: a unit weapon is in no
       hardpoint group by definition, so the check read as "filtered away" every time. */
    useEffect(() => {
        if (infoCard === null) {
            return;
        }

        const onScreen = groupOf(hardpointGroups, infoCard) !== null
            || looseWeapons.some(row => row.id === infoCard);

        if (!onScreen) {
            setInfoCard(null);
        }
    }, [infoCard, hardpointGroups, looseWeapons]);

    /**
     * Brings the aimed-at hardpoint's card into view.
     *
     * Clicking a targeting mark on the model picks that hardpoint, and on a hull with thirty of
     * them the card saying so was as likely as not somewhere below the fold - so the mark lit up
     * and the panel appeared to do nothing. Its group is unfolded first, because a card inside a
     * folded heading is not scrollable to at all.
     *
     * `nearest` rather than `center`: a card already on screen must not be yanked about, and this
     * runs for a click on the card itself just as much as for a click on the mark.
     */
    useEffect(() => {
        if (fireTarget === 'hull') {
            return;
        }

        const group = groupOf(hardpointGroups, fireTarget);
        if (group !== null) {
            setFoldedGroups(current => {
                if (!current.has(group)) {
                    return current;
                }

                const next = new Set(current);
                next.delete(group);
                return next;
            });
        }

        // After the fold has been taken off, or the card is still not laid out.
        const at = requestAnimationFrame(() => {
            document.querySelector(`[data-card="${CSS.escape(fireTarget)}"]`)
                ?.scrollIntoView({ block: 'nearest' });
        });

        return () => cancelAnimationFrame(at);
    }, [fireTarget, hardpointGroups]);

    /** Shield meshes the model names but does not shade as shields. Recomputed with the rows. */
    const shieldOffShader = useMemo(
        // Keyed on the ROWS: they are rebuilt whenever a part loads or the chain re-runs, which is
        // exactly when the answer can change. The viewport ref is deliberately not a dependency -
        // it never changes identity.
        () => viewportRef.current?.shieldMeshesOffShader() ?? [],
        [treeItems]);

    /**
     * Every weapon starts OFF, and the stage pill switches the lot together.
     *
     * The user's rule. The measured reason: the Nebulon B's four hardpoints each declare 175 by 160
     * degrees, so drawing them at once fills the viewport however right the geometry is.
     *
     * Keyed on the SUBJECT, not on the weapon rows: those are rebuilt whenever `destroyed` changes
     * too, so keying on them meant shooting a hardpoint silently switched every arc off.
     */
    useEffect(() => {
        setHiddenWeapons(allWeaponIds(weapons));
    }, [scene]);

    /** Which tree row each of the hull's own bones is, for the fire-bone buttons on a row. */
    const boneRows = useMemo(() => boneRowIndex(treeItems), [treeItems]);

    /**
     * A weapon's own switches: draw its cone.
     *
     * An icon button rather than a tick box. Every other switch in this panel is one, and a tick
     * box beside them read as a form field rather than as something you press - which is exactly
     * what the reader said about it.
     */
    const weaponToggles = (row: WeaponRow): React.JSX.Element => {
        const drawable = row.arcs.length > 0;

        return (
            <IconButton
                icon="arcs"
                className={drawable && !row.destroyed && !hiddenWeapons.has(row.id)
                    ? 'active' : undefined}
                pressed={drawable && !hiddenWeapons.has(row.id)}
                title={weaponTitle(row)}
                disabled={!drawable || row.destroyed}
                // weaponTitle already phrases the state, including the two that disable this.
                disabledReason={weaponTitle(row)}
                onClick={event => {
                    event.stopPropagation();
                    setWeaponArcs(row.id, hiddenWeapons.has(row.id));
                }}
            />
        );
    };

    /**
     * The fire bones, as buttons that point at them in the model.
     *
     * They TOGGLE into the selection rather than replacing it, which is what was wrong before: a
     * weapon with two fire points could never have both boxed at once, and seeing the pair is the
     * one thing looking at two fire points is for.
     */
    const fireBoneButtons = (row: WeaponRow): React.JSX.Element => (
        <span className="bone-picks">
            {row.fireBones.map((bone, index) => {
                const rowId = boneRows.get(bone.toLowerCase());

                return (
                    <button
                        key={`${bone}#${index}`}
                        className={'muzzle-row'
                            + (rowId !== undefined && selected.has(rowId) ? ' selected' : '')}
                        disabled={rowId === undefined}
                        title={fireBoneTitle(bone, rowId !== undefined)}
                        onClick={event => {
                            event.stopPropagation();
                            selectBoneRow(rowId);
                        }}
                    >
                        <Icon name="skeleton" size={13} />
                        <span className="muzzle-slot">{muzzleLabel(index)}</span>
                        <span className="muzzle-bone">{bone}</span>
                    </button>
                );
            })}
        </span>
    );



    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        // Which cones exist is decided in one place, `weaponRows`, off the same rows the dock is
        // showing - so a weapon switched off in the dock, and a hardpoint that has been shot away, are
        // the same answer in both. Weapons live on the scene rather than on the hardpoint, so a
        // unit-attached weapon draws through exactly this path too.
        //
        // ANDed with the lens: a cone is an annotation over the model rather than part of it, and
        // only Gameplay carries the pill that switches it off. Left ungated, arcs latched on in
        // Gameplay went on drawing in Model mode with nothing to press. The reader's own `fireArcs`
        // and `hiddenWeapons` are untouched, so leaving the lens and coming back finds the arcs
        // exactly as they were left.
        const annotate = drawsAnnotations(mode);

        viewport.setFireArcs(annotate ? visibleArcs(weapons, fireArcs, hiddenWeapons) : []);

        // Still set, though the list above is already empty when the master is off: the capture
        // path hides the arcs for a screenshot and puts them back afterwards, and it restores
        // through this flag.
        viewport.setFireArcsVisible(annotate && fireArcs);

        // Turrets sit where the XML says they rest, rather than wherever the model was exported.
        // Only the turrets that declare a traverse, and only while the reader has asked. A turret
        // with no extents is left at its rest angle - inventing a swing would claim a reach the
        // unit has not got.
        // Every bone a weapon fires from, so the selected-bone triad can arrow its aim axis.
        viewport.setFireBones(new Set(
            weapons.flatMap(row => row.fireBones).map(bone => bone.toLowerCase())));

        viewport.setTurretSweep(sweepable.filter(sweep => sweeping.has(sweep.id)));

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
    }, [scene, stats, mode, fireArcs, hiddenWeapons, weapons, sweeping, sweepable]);

    /**
     * The targeting marks, and how big the game would draw them.
     *
     * Depends on `stats` for the same reason the arcs do: the parts a mark is centred on are not
     * there until the geometry has loaded, and a mark anchored on a part that does not exist yet is
     * silently dropped.
     */
    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        // The hardpoint health goes in so each mark can be tinted by how worn its hardpoint is.
        //
        // ANDed with the lens, exactly as the arcs are: a targeting mark is a drawing over the
        // model saying what the game can shoot at, not a part of the asset, and only Gameplay
        // carries the pill that puts it away.
        const marks = reticlesOn && drawsAnnotations(mode)
            ? reticleMarks(
                scene?.hardpoints ?? [], scene?.reticles, reticleState, destroyed, hardpointHealth)
            : [];

        viewport.setReticles(marks, reticleScreenSize(scene?.reticles, reticleState));
    }, [scene, stats, mode, reticlesOn, reticleState, destroyed, hardpointHealth]);

    useEffect(() => {
        const chosen = customColour !== null
            ? parseHex(customColour)
            : scene?.factions.find(f => f.name === faction)?.color ?? null;

        // With no faction picked the subject wears its OWN uncoloured colour, and failing that its
        // faction's: team colour is a skirmish thing, so this is what the unit looks like most of
        // the time. Only 24 of the 772 objects that name an affiliation carry a colour of their
        // own, so the fallback is what actually reaches most of them.
        viewportRef.current?.setColorization(colorizationFor(
            chosen,
            scene?.noColorizationColor,
            affiliationColour(scene?.factions ?? [], scene?.affiliation)));
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
     *
     * Not on the first rows to ARRIVE, though - on the first rows once the subject has finished
     * assembling. Parts and their effects land one at a time, so the first non-empty tree is a bare
     * skeleton whose damage bones carry nothing yet, and `defaultCollapsed` folds a limb precisely
     * because it carries nothing. Every effect that had not landed was then buried: a Star
     * Destroyer opened with 20 of its 22 proxies behind twisties, and filtering the tree to effects
     * showed 2 of them. The load cover coming off is the signal that the tree is the whole tree.
     */
    useEffect(() => {
        if (foldedRef.current || loading.covered || treeItems.length === 0) {
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
    }, [treeItems, loading.covered]);

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
        translatedOn, particlesOn, particleSpeed]);

    /**
     * Records what belongs to the PROJECT rather than to the person.
     *
     * Its own message and its own effect for that reason: the room goes to `globalState` and this
     * goes to `workspaceState`, so writing them together would put one of them at the wrong tier.
     * Every field here names something out of the mod's own tree - a damage type, a faction - and a
     * damage type typed against one mod arriving in the next is the fault that moved them.
     */
    useEffect(() => {
        // The same guard the room's own write uses: before the stored bench has been applied the
        // first frame would overwrite it with defaults.
        if (!restoredRef.current) {
            return;
        }

        vscode.postMessage({
            type: 'setProjectSettings',
            project: {
                attacker,
                presets: attackerPresets,
                faction: faction === '' ? null : faction,
                customColour,
            },
        });
    }, [attacker, attackerPresets, faction, customColour]);

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

    /* The bones the selected ROWS stand for. Derived rather than stored, so a bone cannot be lit
       while its row is not - which is exactly what the old single index kept doing. */
    const selectedBones = useMemo(
        () => new Set([...selected]
            .map(boneIndexOfRow)
            .filter((index): index is number => index !== null)),
        [selected]);

    useEffect(() => {
        viewportRef.current?.setSelectedBones(selectedBones);
    }, [selectedBones]);

    // Depends on `treeItems` as well as on the selection, because the rows a box spans are only
    // known once the tree has been built - a selection restored before the model arrives would
    // otherwise box nothing and look broken.
    useEffect(() => {
        viewportRef.current?.setSelectedRows([...selected]);

        // A selected HARDPOINT outlines its own model rather than its attach bone's subtree. The
        // viewport decides whether it can - the part may not have arrived yet - see `boxTargetFor`.
        const aimed = sceneRef.current?.hardpoints.find(h => h.id === fireTarget);
        const aimedBone = aimed?.attachBone ?? null;

        viewportRef.current?.setSelectedHardpoint(
            aimed?.partId ?? null,
            aimedBone === null
                ? null
                : boneRowIndex(viewportRef.current?.treeItems() ?? [])
                    .get(aimedBone.toLowerCase()) ?? null);
    // fireTarget is in the list because the effect READS it. Left out, the effect closed over
    // whichever hardpoint was aimed at last render - so picking one drew the box for the previous
    // one, or for none at all on the first pick.
    }, [selected, treeItems, fireTarget]);

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
            if (event.key === 'Escape' && !worldOpen && !cameraOpen && !abilitiesOpen
                && !attackOpen && detailsRow === null) {
                clearSelection();
            }
        };

        document.addEventListener('keydown', onKey);

        return () => document.removeEventListener('keydown', onKey);
    }, [selected, worldOpen, cameraOpen, abilitiesOpen, attackOpen, detailsRow,
        clearSelection]);

    /**
     * Pins the details flyout beside the row it belongs to.
     *
     * Measured rather than positioned in CSS, because the row lives inside a scroller: a flyout
     * parented to it would be clipped by the list, and one parented outside has no idea where the
     * row ended up. Both are read at the moment of placing, so a resized dock or a scrolled tree
     * moves it rather than leaving it pointing at nothing.
     */
    /**
     * Where a flyout anchored to a row in the dock goes.
     *
     * Shared by the tree row's details and the hardpoint card's info, because they are the same
     * shape answering the same question about different rows - and a second copy of this would be
     * a second thing to keep in step with `anchorFlyout`.
     */
    const placeBesideRow = useCallback((
        selector: string, box: HTMLElement | null,
    ): { left: number; top: number } | null => {
        const button = document.querySelector(selector);
        const dock = document.querySelector('.right-dock');

        if (button === null || dock === null || box === null) {
            return null;
        }

        const row = button.getBoundingClientRect();

        return anchorFlyout({
            row: { top: row.top, bottom: row.bottom },
            dockLeft: dock.getBoundingClientRect().left,
            window: { width: window.innerWidth, height: window.innerHeight },
            size: { width: box.offsetWidth, height: box.offsetHeight },
        });
    }, []);

    const placeInfo = useCallback(() => {
        if (infoCard === null) {
            return;
        }

        const at = placeBesideRow(
            `[data-card="${CSS.escape(infoCard)}"] .card-info-btn`, cardInfoRef.current);

        if (at !== null) {
            setCardInfoAt(at);
        }
    }, [infoCard, placeBesideRow]);

    const placeAbilityInfo = useCallback(() => {
        if (infoAbility === null) {
            return;
        }

        const at = placeBesideRow(
            `[data-ability="${CSS.escape(infoAbility)}"] .card-info-btn`, abilityInfoRef.current);

        if (at !== null) {
            setAbilityInfoAt(at);
        }
    }, [infoAbility, placeBesideRow]);

    useLayoutEffect(() => { placeAbilityInfo(); }, [placeAbilityInfo]);

    // The abilities flyout is closed by Escape and by its own button; when it goes, so does this.
    useEffect(() => {
        if (!abilitiesOpen) {
            setInfoAbility(null);
        }
    }, [abilitiesOpen]);

    // Same terms as the details flyout: before paint, and again while the dock scrolls under it.
    useLayoutEffect(() => { placeInfo(); }, [placeInfo]);

    useEffect(() => {
        if (infoCard === null) {
            return;
        }

        const onMove = (): void => placeInfo();

        window.addEventListener('resize', onMove);
        document.querySelector('.dock-content')?.addEventListener('scroll', onMove);

        return () => {
            window.removeEventListener('resize', onMove);
            document.querySelector('.dock-content')?.removeEventListener('scroll', onMove);
        };
    }, [infoCard, placeInfo]);

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

        const fromAttachments = attachmentIssues.map(message => ({
            severity: 'warning',
            message,
            hardpointId: null,
        }));

        // The shader gap is a limitation of the preview rather than a fault in the mod, so it is
        // info. The corner button is the way OUT of it; this is the explanation of it.
        const fromShaders = translatedOn && !anyShaderSource
            ? [{
                severity: 'info',
                message: 'No shader sources are reachable, so nothing can be translated. A '
                    + 'game install ships compiled .fxo; the .fx sources are a separate '
                    + 'Petroglyph download.',
                hardpointId: null,
            }]
            : [];

        // A shield bubble the model names but does not draw with a shield shader. The NAME is what
        // decides which mesh a shield ability reveals - the author's word - so this corrects
        // nothing; it just says once that the thing will read as solid geometry rather than a
        // field.
        const fromShield = shieldOffShader.length === 0
            ? []
            : [{
                severity: 'warning',
                message: `${shieldOffShader.join(', ')} ${shieldOffShader.length === 1
                    ? 'is' : 'are'} named as a shield mesh but not drawn with a shield shader `
                    + '- draws as solid geometry, not a field.',
                hardpointId: null,
            }];

        return [...problems, ...fromAttachments, ...fromColour, ...fromShaders, ...fromShield];
    }, [problems, attachmentIssues, colourFindings, translatedOn, anyShaderSource, shieldOffShader]);

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
            title: 'Show ground grid and world axes',
        },
        {
            id: 'floor', label: 'Ground', icon: 'ground', on: floor, set: setFloor,
            title: 'Show lit floor',
        },
        {
            // With the view toggles rather than in the scene panel: this is flipped constantly
            // while reading geometry, the same way the grid is, and it describes how the model is
            // DRAWN rather than what the room is like.
            id: 'wireframe', label: 'Wireframe', icon: 'geometry',
            on: wireframe, set: setWireframe,
            title: 'Draw meshes as edges',
        },
    ];

    // Never removed. A control that is absent on one model and present on the next reads as a
    // feature that comes and goes; disabled says "this exists, and here is why it is not available".
    const anyEffect = emitters.length > 0 || groups.length > 0;

    overlays.push({
        id: 'particles', label: 'Effects', icon: 'effects',
        on: particlesOn && anyEffect, set: setParticlesOn,
        disabled: !anyEffect,
        title: !anyEffect
            ? 'No particle systems on this model'
            : 'Show every particle system',
    });
    // Arcs LAST, and deliberately so. It is the one pill here that still comes and goes - it
    // belongs to the Gameplay lens, which is a statement about which lens you are in rather than
    // about this model - so it sits at the end of the strip where arriving and leaving cannot move
    // the three permanent pills beside it.
    if (mode === 'gameplay') {
        // Whether anything can be DRAWN, not whether the subject is armed. A weapon that declares
        // no reach has no cone, so a pill that lit up for it would switch on nothing at all.
        const drawable = weapons.filter(row => row.arcs.length > 0);
        const anyArc = drawable.length > 0;
        const shown = drawable.filter(row => !row.destroyed && !hiddenWeapons.has(row.id)).length;

        overlays.push({
            // All or nothing: the pill IS the weapons, so pressing it writes every one of them and
            // the list below is for refining afterwards. It does not preserve a previous selection
            // across a press - that is the point of an all-or-nothing master.
            id: 'arcs', label: 'Arcs', icon: 'arcs', on: fireArcs && anyArc,
            set: (on: boolean) => {
                setFireArcs(on);
                setHiddenWeapons(on ? new Set() : allWeaponIds(weapons));
            },
            disabled: !anyArc,
            title: !anyArc
                ? 'No firing arcs on this model'
                : shown === drawable.length
                    ? 'Show every firing arc'
                    : `Show firing arcs - ${shown} of ${drawable.length} weapons on`,
        });

        // Whether a mark can be DRAWN, which is a different question from whether the subject has
        // targetable hardpoints: the art resolves out of the game's texture directory, so a workspace
        // with no game directory configured gets the map and no images. Saying so on the pill beats
        // a switch that lights up and changes nothing.
        const targetable = (scene?.hardpoints ?? []).filter(h => h.isTargetable).length;
        const anyReticle = reticleMarks(
            scene?.hardpoints ?? [], scene?.reticles, reticleState, new Set()).length > 0;

        overlays.push({
            id: 'reticles', label: 'Reticles', icon: 'target',
            on: reticlesOn && anyReticle, set: setReticlesOn,
            disabled: !anyReticle,
            title: anyReticle
                ? 'Show targeting marks'
                : targetable === 0
                    ? 'Nothing here can be targeted'
                    : 'Reticle art unreadable - set your game directory',
        });
    }

    const animations = stats?.animations ?? [];

    /**
     * The model the clips are named after - see `clipNamingModel`.
     *
     * Never the subject, which for a game object is its XML name (`Generic_Star_Destroyer`), and not
     * always the hull either: a unit borrowing another model's animation set has clips carrying the
     * SOURCE model's name.
     */
    const animationModel = useMemo(() => clipNamingModel(scene), [scene]);

    const animationGroups = useMemo(
        () => groupAnimations(animationModel, animations), [animationModel, animations]);

    /**
     * Every clip in the order the library shows them, which is the order the track buttons step
     * through. Grouped order, not file order - stepping past the end of Idle should land on the
     * first Movement clip, the way the eye reads the list.
     */
    const orderedClips = animationGroups.flatMap(
        group => group.actions.flatMap(action => action.takes.map(take => take.name)));
    const clipAt = animation === null ? -1 : orderedClips.indexOf(animation);

    /** Which action each clip belongs to, so a clip picked any way at all can find its loop rule. */
    const actionOfClip = useMemo(() => {
        const byClip = new Map<string, AnimationAction>();

        for (const group of animationGroups) {
            for (const action of group.actions) {
                for (const take of action.takes) {
                    byClip.set(take.name, action);
                }
            }
        }

        return byClip;
    }, [animationGroups]);

    /**
     * Repeat follows the CLIP, not the reader.
     *
     * An idle is meant to run on a loop and a death is meant to end; opening every clip looping was
     * right for one of those and wrong for the other, and the reader had to notice and correct it
     * each time. `animationNames` decides which is which - see its loop rule. Only when the clip
     * CHANGES, so pressing the latch during a clip still holds.
     */
    useEffect(() => {
        if (animation === null) {
            return;
        }

        setAnimationLoop(actionOfClip.get(animation)?.loops ?? false);
    }, [animation, actionOfClip]);

    /**
     * The action whose takes are being drawn at random, or null when a clip was picked by hand.
     *
     * STATE, not a ref. The playhead readout says "random" while this is set, and a ref read during
     * render only happens to be right when something else re-renders in the same breath - which is
     * exactly the bug the probe caught: picking a take by hand cleared the ref and the readout went
     * on saying random.
     */
    const [roulette, setRoulette] = useState<AnimationAction | null>(null);

    /** Draws a take of one action and plays it. The game's own behaviour - see `takeRoulette`. */
    const playAction = useCallback((action: AnimationAction): void => {
        // Only where there is something to draw from. A one-take action played from its tile is
        // just that clip, and saying "random" over it would be a claim about nothing.
        setRoulette(action.takes.length > 1 ? action : null);
        setAnimation(pickTake(action.takes, Math.random()).name);
        setAnimationPaused(false);
    }, []);

    /** Picking a named take by hand stops the roulette - the reader asked for that recording. */
    const pickClip = useCallback((clip: string): void => {
        setRoulette(null);
        setAnimation(clip);
        setAnimationPaused(false);
    }, []);

    /**
     * Draws again each time a rouletting clip comes round.
     *
     * The wrap is read off the playhead going BACKWARDS, which is the only thing on this side that
     * knows a looping clip restarted - the mixer does it internally and announces nothing. Watched
     * at the same 50ms the scrubber already ticks at, so it costs no extra timer.
     */
    const lastTimeRef = useRef(0);

    useEffect(() => {
        const wrapped = playhead.time < lastTimeRef.current - 1e-4;

        lastTimeRef.current = playhead.time;

        if (roulette !== null && animationLoop && wrapped) {
            setAnimation(pickTake(roulette.takes, Math.random()).name);
        }
    }, [playhead.time, animationLoop, roulette]);

    /** Families the reader has folded away, by family id. */
    const [foldedFamilies, setFoldedFamilies] = useState<ReadonlySet<string>>(new Set());

    const toggleFamily = (family: string): void => setFoldedFamilies(current => {
        const next = new Set(current);
        if (!next.delete(family)) {
            next.add(family);
        }
        return next;
    });

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

    const wholeTree = buildTree(treeItems);
    const filteredTree = filterTree(wholeTree, { text: boneFilter, kinds });
    const treeRows = visibleTreeRows(filteredTree, collapsed);

    /* What the tree would show with no filter at all, which is the height it is held at. Folding a
       branch still changes it - that is the reader asking for less tree - but filtering cannot. */
    const unfilteredRowCount = visibleTreeRows(wholeTree, collapsed).length;

    /* Showing, which is not the same as pressed: a pattern keeps its own box open. */
    const searchOpen = searchIsOpen(searchPressed, boneFilter);

    /* What the header says when the tree is showing less than the model has. The count alone
       cannot carry it - "40" over a Star Destroyer reads as a small model, and once the search
       element is collapsed neither the pattern nor a switched-off kind is on screen at all.

       Measured on the FILTERED tree rather than on `treeRows`, which has already had every
       collapsed branch dropped from it. Folding is not filtering, and counting the rendered rows
       had a fresh model announcing itself as narrowed the moment it opened. */
    const filterSummary = treeFilterSummary(
        countTreeNodes(filteredTree), treeItems.length, kinds.size);

    // Deliberately no longer cleared when the row scrolls out of the tree.
    //
    // The flyout had to be: it was pinned to the row, so filtering the row away or collapsing its
    // parent left a panel hanging beside nothing. A tab is not pinned to anything, and dropping the
    // row on a filter keystroke would mean typing in the tree search silently stopped the inspector
    // tracking what it is showing. The row is still in `treeItems` either way, so the facts stay
    // right; only its visibility in this list changed, which is not a fact about the row.

    /**
     * Whether the model has been moved off the state it opened in.
     *
     * Reads the same three things Reset puts back, so the button cannot claim there is nothing to
     * do while something plainly is - and cannot offer to reset a model already at rest.
     */
    const touched = modelTouched({
        alt,
        lodIsHighest: levels.lod.length === 0 || lod === levels.lod[levels.lod.length - 1],
        hiddenEmitters: hiddenEmitters.size,
        rowOverrides: viewportRef.current?.rowOverrideEntries().length ?? 0,
    });

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
     *
     * The GAMEPLAY lens is left alone too, and for the stronger reason. This button sits in the
     * Model tree's header and puts the MODEL back; a destroyed hardpoint and a hidden firing arc
     * belong to the lens that can undo them. Clearing `hiddenWeapons` from here revealed every
     * firing arc on the hull the moment it was pressed - in a lens that draws no arcs and offers no
     * pill to put them away. See `modelTouched`, which is the same boundary drawn once.
     */
    const resetModel = useCallback(() => {
        viewportRef.current?.clearRowOverrides();
        setAlt(0);
        setLod(current => levels.lod.length > 0 ? levels.lod[levels.lod.length - 1] : current);
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
        // Hiding pushes down the subtree; showing clears what is under them, so switching a bone
        // back on does not drag its collision hull into view.
        //
        // Each row's own `authored` goes in - `gatedOff` IS `!authored`, straight off the chain -
        // because `setRow` stores the reader's word only where it disagrees with the model. Per row
        // and not once for the click: a multi-select spans meshes the model draws and shadow
        // volumes it does not, and one answer for the lot would be wrong for half of them.
        const authoredOf = new Map(treeItems.map(item => [item.id, !item.gatedOff]));

        const changes = clicked.flatMap(row => setRow(
            row,
            withDescendants(roots, [row]).filter(under => under !== row),
            visible,
            authoredOf.get(row) ?? true));

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

                        {/* Over the canvas while the subject assembles itself.

                            The parts arrive one at a time and a part's textures cannot be asked for
                            until its geometry exists, so what a reader saw was a black hull, then
                            black turrets, then textures landing one by one. All of it is the load
                            order showing through, and none of it is worth watching. The counts are
                            here because a cover that says nothing turns a slow load into a hang. */}
                        {loading.covered && (
                            <div className="load-cover" role="status" aria-live="polite">
                                <div className="load-spinner" />
                                <span className="load-label">{loading.label}</span>
                                {loading.detail !== null && (
                                    <span className="load-detail">{loading.detail}</span>
                                )}
                            </div>
                        )}

                        {/* The SCENE's settings, not the model's - so they sit on the viewport
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
                            <IconButton
                                icon="scene"
                                className={'world-globe' + (worldOpen ? ' active' : '')}
                                expanded={worldOpen}
                                title="Scene settings"
                                onClick={() => setWorldOpen(open => !open)}
                            />
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
                                <IconButton
                                    key={overlay.id}
                                    icon={overlay.icon}
                                    className={overlay.on ? 'active' : undefined}
                                    pressed={overlay.on}
                                    label={overlay.label}
                                    title={overlay.title}
                                    disabled={overlay.disabled ?? false}
                                    // The overlay's own title already says why when it is off:
                                    // each one phrases its state, so there is nothing to add here.
                                    disabledReason={overlay.title}
                                    onClick={() => overlay.set(!overlay.on)}
                                />
                            ))}
                        </div>
                        </span>

                        {/* The faction palette, centred over the model.

                            It sits with the room and the camera because all three answer HOW YOU
                            ARE LOOKING at the subject rather than what the subject is - a tint is
                            a way of viewing a hull, not a property of it. The bottom edge is for
                            what the model is DOING, which is why it moved.

                            The colour IS the button - a swatch says what it will do far better
                            than its faction's name does, and the name is one hover away. The
                            custom well is always the RIGHTMOST, so the one control that is not a
                            faction never moves as the roster changes length.

                            Not gated on the lens. It is stage chrome, and a corner that empties
                            when you change lens makes the stage feel like it is coming apart. */}
                        <span className="stage-slot stage-slot-mid">
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
                                    title="No team colour"
                                    onClick={() => chooseTint('', null)}
                                >
                                    <Icon name="none" />
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
                                        // Picking a faction drops a custom colour, or the strip
                                        // would highlight one and the hull show another.
                                        onClick={() => chooseTint(entry.name, null)}
                                    />
                                ))}

                                <input
                                    type="color"
                                    className={'swatch swatch-custom'
                                        + (customColour !== null ? ' active' : '')}
                                    value={customColour ?? toHex(
                                        scene?.factions.find(f => f.name === faction)?.color
                                        ?? NEUTRAL_COLOUR)}
                                    title="Pick a custom colour"
                                    onChange={e => chooseTint(faction, e.target.value)}
                                />
                            </div>
                        )}
                        </span>


                        {/* Where you are looking FROM is a property of the view, not of the model,
                            so it sits on the stage like the scene controls opposite.

                            The presets read as words rather than pictograms, because no icon says
                            "three-quarter view". They carried a small camera glyph to say what the
                            row was for; the camera BUTTON beside them now says it, and two camera
                            icons in a row said it twice.

                            ONE radiogroup, presets and the model's own cameras together. They were
                            two runs of `aria-pressed` buttons, which is the markup for switches
                            that hold and release independently - and these cannot, because the
                            camera is in one place at a time. Choosing any of them releases
                            whichever was chosen before, which is what a radiogroup says and a row
                            of toggles denies. */}
                        <span className="stage-slot stage-slot-end">
                        <div className="stage-chrome">
                            <ModeSelector
                                label="Where the camera looks from"
                                value={cameraView}
                                options={cameraViews}
                                onSelect={id => {
                                    // The model's own cameras are the ones this panel did not
                                    // invent, and only they can refuse: a bone can be missing from
                                    // the skeleton that was loaded, and a view that did not take
                                    // must not be marked as the one you are looking from.
                                    const authored = authorCameras.find(entry => entry.id === id);

                                    if (authored === undefined) {
                                        setCameraView(id as PresetView);
                                        viewportRef.current?.frameAll(id as PresetView);
                                        return;
                                    }

                                    // The pose the SERVER resolved, turned into the scene's axes.
                                    //
                                    // It used to ask the viewport to find the pair by name -
                                    // Camera01 and Camera01.Target - and the bone in the GLB is
                                    // Camera01Target, the dot having not survived the export. So
                                    // the lookup failed, applyModelCamera returned false, and the
                                    // entry did nothing at all, silently, on every model with one.
                                    //
                                    // The turn matters as much as the numbers: those are Alamo
                                    // Z-up and the scene is glTF Y-up. See `cameraPose`.
                                    const pose = cameraPose(authored);

                                    if (pose === null) {
                                        return;
                                    }

                                    viewportRef.current?.applyCameraPose(pose.position, pose.target);
                                    setCameraView(id);
                                }}
                            />
                        </div>

                        {/* The mirror of the scene button opposite: the presets are the switches,
                            this OPENS something, and the two are not the same kind of control. Last
                            on the right as the scene is first on the left, so the stage reads
                            outwards from the model in both directions. */}
                        <div className="stage-chrome">
                            <IconButton
                                icon="camera"
                                className={cameraOpen ? 'active' : undefined}
                                expanded={cameraOpen}
                                title="Camera settings"
                                onClick={() => setCameraOpen(open => !open)}
                            />
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
                                    <IconButton
                                        icon="reset"
                                        title={'Put the backdrop, lights, wind and draw distance '
                                            + 'back to their defaults. The model`s own state is '
                                            + 'left as it is.'}
                                        onClick={resetRoom}
                                    />
                                    <IconButton
                                        icon="close"
                                        title="Close"
                                        onClick={() => setWorldOpen(false)}
                                    />
                                </div>
                                <div className="stage-flyout-body">

        <DockSection
            id="scene.room"
            title="Room"
            collapsed={folded.has('scene.room')}
            onToggle={toggleSection}
        >
        <Field
            label="Backdrop"
            info={<>
                Never lit and never in the depth buffer, so a backdrop cannot be
                mistaken for part of the model.
            </>}
        >
            <ModeSelector
                label="Backdrop"
                value={background}
                options={[
                    { id: 'flat', label: 'Flat', title: 'A plain, neutral field' },
                    { id: 'starfield', label: 'Stars', title: 'A starfield' },
                    { id: 'sky', label: 'Sky', title: 'A sky with a horizon' },
                ] satisfies ChoiceOption<BackgroundKind>[]}
                onSelect={setBackground}
            />
        </Field>

        <Field
            label="Ground height"
            value={floorLevel}
            info={<>
                Zero is the middle of the track - the plane the model itself stands on. The ends are
                one model radius either way, so the control means the same thing on a trooper and
                on a Star Destroyer. The grid moves with it: it is the ruler for the ground, not for
                the origin.
            </>}
        >
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
        </Field>
        </DockSection>

        <DockSection
            id="scene.light"
            title="Light"
            collapsed={folded.has('scene.light')}
            onToggle={toggleSection}
        >
        <Field
            label="Light"
            info={<>
                The engine's own rig: a sun that casts the shadow and two fills that
                do not. All three reach the translated shaders.
            </>}
        >
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
        </Field>

        <Field
            label="Light around"
            value={<>{lights[editing].azimuth} deg, from {bearing.from}</>}
            info={<>
                Turns round the model the way the stage does, from the same zero: at 0 the light
                stands where you stand in the Front view, and it moves clockwise seen from above.
            </>}
        >
            <input
                type="range"
                min={0}
                max={360}
                step={5}
                value={lights[editing].azimuth}
                onChange={e => setLight(editing, { azimuth: Number(e.target.value) })}
            />
        </Field>

        <Field
            label="Light height"
            infoSeverity={bearing.belowGround ? 'warning' : 'info'}
            info={<>
                {bearing.belowGround
                    ? (editing === 'sun'
                        ? 'Under the ground plane. The floor is between the sun and the model, so '
                          + 'it lights the hull from beneath and casts nothing onto the ground.'
                        : 'Under the ground plane, which is where the engine puts both of its own '
                          + 'fills. They cast no shadow, so nothing is hidden by it.')
                    : 'The sun drives the shadow. Straight overhead flattens the hull and hides '
                      + 'the shadow under it; raking is what makes panel lines read.'}
            </>}
            value={<>{lights[editing].elevation} deg, {bearing.height}</>}
        >
            <input
                type="range"
                min={-90}
                max={90}
                step={5}
                value={lights[editing].elevation}
                onChange={e =>
                    setLight(editing, { elevation: Number(e.target.value) })}
            />
        </Field>

        <Field label="Light colour" value={<>{lights[editing].intensity.toFixed(2)}x</>}>
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
        </Field>

        <Field
            label="Ambient"
            value={<>{lights.ambient.intensity.toFixed(2)}x</>}
            info={<>
                What reaches the parts no light does. Too much and the model reads
                flat; none at all and its shadowed side is black.
            </>}
        >
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
        </Field>

        {/* The swatch rides IN the label, not under it.

            A colour input carries `margin-left: auto` so it sits at the far edge of its row - which
            is what the swatch beside a slider wants. Under a `.field`, though, the row is a COLUMN,
            and an auto margin there aligns on the cross axis instead: the swatch landed on a line of
            its own, hard right, with a gap above it and nothing beside it. The two fields with no
            slider were the only ones shaped like that.

            Beside the label in a row, NOT inside it: the label carries opacity 0.7, and opacity
            composites its whole subtree as a group, so a swatch in there renders a colour that is
            not the one stored - measured, a stored #ffffff drew as #bdbdbd. A sibling keeps its
            own colour and still reaches the far edge, because the well carries the auto margin. */}
        <Field
            label="Shadow colour"
            inline
            info={<>
                Alamo`s one shadow colour, and it MULTIPLIES: 0.5 grey means half as bright.
                In Game mode it tints the stencil volume, so it reaches the hull`s own
                self-shadowing as well as the ground. In Default mode only the ground can
                catch it.
            </>}
        >
            <input
                type="color"
                value={hexFromColour(lights.shadow)}
                /* Dead ONLY when nothing can show it. It was gated on the floor alone, which
                   disabled a working control: in Game mode the stencil darken tints the hull's
                   own self-shadowing with no ground under it at all. */
                disabled={shadowReach === 'none'}
                title={SHADOW_TINT_TITLE[shadowReach]}
                onChange={e => setLights(current => ({
                    ...current, shadow: colourFromHex(e.target.value),
                }))}
            />
        </Field>

        <Field
            label="Highlight colour"
            inline
            info={<>
                One global specular for all three lights, as the engine keeps it.
                Only the translated effect shaders read it.
            </>}
        >
            <input
                type="color"
                value={hexFromColour(lights.specular)}
                onChange={e => setLights(current => ({
                    ...current, specular: colourFromHex(e.target.value),
                }))}
            />
        </Field>
        </DockSection>

        <DockSection
            id="scene.weather"
            title="Weather"
            collapsed={folded.has('scene.weather')}
            onToggle={toggleSection}
        >
        <Field label="Wind from" value={<>{wind.heading} deg</>}>
            <input
                type="range"
                min={0}
                max={360}
                step={5}
                value={wind.heading}
                onChange={e =>
                    setWind(current => ({ ...current, heading: Number(e.target.value) }))}
            />
        </Field>

        <Field
            label="Wind speed"
            value={wind.speed.toFixed(1)}
            info={<>
                Bends the trees, leans the grass, and carries the 484 emitters that
                declare themselves affected by it.
            </>}
        >
            <input
                type="range"
                min={0}
                max={20}
                step={0.5}
                value={wind.speed}
                onChange={e =>
                    setWind(current => ({ ...current, speed: Number(e.target.value) }))}
            />
        </Field>
        </DockSection>

        <DockSection
            id="scene.effects"
            title="Effects"
            collapsed={folded.has('scene.effects')}
            onToggle={toggleSection}
        >
        {/* Which renderer draws the model - one or the other, never both, so a segmented
            control rather than a tick box.

            First in the section because it decides how every SURFACE is drawn, and the two below
            it are passes over the finished frame. All three are Tier 1: they describe the picture,
            not the model, so they follow the reader from one file to the next. */}
        {scene !== null && scene.kind !== 'Particle' && (
            <Field
                label="Renderer"
                info={<>
                    Default reads each sub-mesh`s shader NAME and draws the archetype it names -
                    never wrong about geometry, only about how a surface is lit. Game draws the
                    model the way the engine does: its own effect shaders, translated, and
                    stencil shadow volumes cast by its authored shadow mesh. Only the shaders
                    need the .fx sources; the shadows work without them.
                </>}
            >
                <ModeSelector
                    label="Which renderer draws the model"
                    /* What is actually RUNNING, not what could be fully delivered. The stencil
                       shadow pass follows this switch alone and needs no .fx sources, so a reader
                       with none was shown Default while the engine's own shadows were being cast -
                       and told by the problems list, in the same breath, that they were in Game
                       mode with nothing to translate. */
                    value={translatedOn ? 'game' : 'default'}
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
                            // Selectable with no sources, because the mode still
                            // DOES something without them: the stencil shadows
                            // follow this switch and need no .fx at all. It was
                            // disabled here, which - once the value above began
                            // reporting the real mode - would have left a reader
                            // who stepped to Default unable to return to the mode
                            // the panel opens in.
                            title: anyShaderSource
                                ? 'The model`s own effect shaders, translated, and '
                                    + 'the engine`s stencil shadows. '
                                    + translatedCount.translated + ' of '
                                    + translatedCount.total + ' sub-meshes are '
                                    + 'drawn this way.'
                                : 'The engine`s stencil shadows, which need no '
                                    + 'sources. For its effect shaders as well, '
                                    + 'set the .fx sources up.',
                        },
                    ]}
                    onSelect={id => setTranslatedOn(id === 'game')}
                />

                {/* Always here, in one of two states - never absent.

                    The setup flow is a command, and a command nobody can find is not a path out of
                    "nothing translated"; but a button that appears only while something is wrong is
                    a control the reader never sees working. So the row always says where the
                    sources stand, and only the wording changes. */}
                {anyShaderSource ? (
                    <span className="shader-state">
                        <Icon name="check" />
                        Shader sources ready
                    </span>
                ) : (
                    <Button
                        title="Fetch the .fx shader sources"
                        onClick={() => vscode.postMessage({ type: 'obtainShaders' })}
                    >
                        <Icon name="download" />
                        Set up shader sources
                    </Button>
                )}
            </Field>
        )}

        <Field
            label="Heat distortion"
            info={<>
                Debug draws the heat BUFFER instead of the bent picture - the only way to see which
                pixels a distortion covers, since its whole effect is a displacement. Red and green
                carry the direction, blue the strength, and ALL BLACK means nothing on this model is
                distorting anything. It keeps the buffers alive while it is on.
            </>}
        >
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
        </Field>

        <Field
            // The checkbox and its word are one <label>, so pressing the word toggles it -
            // and the mark stays OUTSIDE that label, or pressing the mark would toggle it too.
            label={
                <label className="field-label">
                    <input
                        type="checkbox"
                        checked={bloom}
                        onChange={e => setBloom(e.target.checked)}
                    />
                    Bloom
                </label>
            }
            info={<>
                Off by default, as it is in the game. It runs after the heat pass, so the two
                compose in that order rather than fighting over the frame.
            </>}
        />
        </DockSection>

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
                                    <IconButton
                                        icon="close"
                                        title="Close"
                                        onClick={() => setCameraOpen(false)}
                                    />
                                </div>
                                <div className="stage-flyout-body">

        <DockSection
            id="camera.shots"
            title="Saved shots" count={cameraPresets.length}
            collapsed={folded.has('camera.shots')}
            onToggle={toggleSection}
        >
        <Field
            label="Camera presets"
            value={cameraPresets.length}
            info={<>
                Saved shots are kept in RADII rather than in game units, so one preset frames a
                trooper and a Star Destroyer the same way. Copy as Lua works the distance back out
                for whatever is on screen.
            </>}
        >

            {cameraPresets.map(saved => (
                <span className="view-row" key={saved.id}>
                    <Button
                        title={`Frame this model the way ${saved.name} does: `
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
                    </Button>
                    <Button
                        title={'Copy this shot as a Set_Cinematic_Camera_Key line, with the '
                            + 'distance worked out for the model on screen'}
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
                    </Button>
                    <IconButton
                        icon="remove"
                        title={`Delete ${saved.name}`}
                        onClick={() => setCameraPresets(
                            current => current.filter(p => p.id !== saved.id))}
                    />
                </span>
            ))}

            <Button
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
            </Button>

        </Field>

        <Field
            label="Opens with"
            infoSeverity={bindTargets.length === 0 ? 'warning' : 'info'}
            info={<>
                {bindTargets.length === 0
                    ? 'A model opened on its own has no object, type or category to bind to. Open '
                      + 'it through a game object to bind a shot to a whole roster.'
                    : 'A bound shot is applied when the model opens, most specific rule first: '
                      + 'this object beats its type, and its type beats a category. Moving the '
                      + 'camera afterwards is always allowed.'}
            </>}
            value={boundRule === null
                ? 'nothing'
                : cameraPresets.find(p => p.id === boundRule.presetId)?.name ?? 'a lost shot'}
        >

            {/* Bind the LAST saved shot: with no preset there is nothing to bind, and picking
                which one belongs in a list rather than in three buttons. */}
            {bindTargets.map(target => (
                <Button
                    key={target.kind}
                    title={`Open every ${target.what} with ${
                        cameraPresets.length === 0
                            ? 'a saved shot'
                            : cameraPresets[cameraPresets.length - 1].name}`}
                    disabled={cameraPresets.length === 0}
                    disabledReason="Save a shot first - there is nothing to bind yet"
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
                </Button>
            ))}

            {boundRule !== null && (
                <Button
                    title={`Stop opening ${boundRule.kind === 'object' ? 'this object' : `every ${
                        boundRule.value}`} with a saved shot`}
                    onClick={() => setCameraBindings(
                        current => current.filter(b => b.id !== boundRule.id))}
                >
                    <Icon name="remove" />
                    Unbind
                </Button>
            )}

        </Field>
        </DockSection>

        <DockSection
            id="camera.view"
            title="View"
            collapsed={folded.has('camera.view')}
            onToggle={toggleSection}
        >
        <Field
            label="Draw distance"
            info={<>
                The fit already reaches the end of the effects, not just the hull.
                Reach further for a long trail; a huge model may z-fight if you do.
            </>}
        >
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
        </Field>
        </DockSection>

        <DockSection
            id="camera.capture"
            title="Capture"
            collapsed={folded.has('camera.capture')}
            onToggle={toggleSection}
        >
        <Field
            label="Capture"
            value={<>{captureSizePx}px</>}
            info={<>
                Renders the shot on screen at the chosen size and asks where to put it. Off by
                default the grid, the floor, the effects and every annotation stay out of it, which
                is what an icon wants - tick the box to capture the room exactly as you see it.
            </>}
        >

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

            <Button
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
            </Button>

        </Field>
        </DockSection>

                                </div>
                            </div>
                        )}

                        {/* The bottom edge: what the subject is DOING.

                            Three slots like the top edge, and for the reason recorded on
                            `.stage-slot` - two equal side slots are the whole of what centres the
                            middle one. It carried two absolutely-positioned plates before, which
                            was enough for two and is not enough for four.

                            The renderer used to sit here and does not any more: it is a standing
                            choice about how the PICTURE is drawn, not about what the subject is
                            doing, so it lives in Scene > Effects beside heat and bloom. What is
                            left on this edge is the subject's own state. */}
                        <div className="stage-row stage-bottom">
                        <span className="stage-slot">
                        {/* Which damage state and which detail level the subject is shown at.

                            On the stage rather than in the dock because it is the SUBJECT'S state,
                            and the bottom edge is what the subject is doing.

                            SHOWN IN BOTH MODES. It used to be gated to Model on the grounds that in
                            Gameplay the damage state followed from play - which nothing made true,
                            so the stages were simply unreachable there and the control's absence
                            was the only trace. In Gameplay the hull now drives it through the
                            damage table, and the slider is DISABLED rather than removed: the reader
                            needs to see the stage tracking, and to see that the choice exists.

                            Re-authored compactly rather than moved as it stood - the dock's
                            `.field` is a label above a full-width control, which is a shape a 22px
                            stage plate does not have. */}
                        {(levels.alt.length > 1 || levels.lod.length > 1) && (
                            <div
                                className="stage-chrome subject-state"
                                role="group"
                                aria-label="Model state"
                            >
                                {levels.alt.length > 1 && (
                                    <label
                                        className="stage-field"
                                        title={altDrivenByHull
                                            ? 'Damage state follows the hull in Gameplay'
                                            : 'Set damage state - 0 is undamaged'}
                                    >
                                        <Icon name="damage" />
                                        <input
                                            type="range"
                                            min={0}
                                            max={altSteps.count - 1}
                                            step={1}
                                            value={altSteps.positionOf(alt)}
                                            disabled={altDrivenByHull}
                                            onChange={e => setAlt(
                                                altSteps.levelAt(Number(e.target.value)))}
                                        />
                                        <span className="stage-value">
                                            {levelLabel('alt', alt, levels.alt,
                                                { table: scene?.damageTable ?? [] })}
                                        </span>
                                    </label>
                                )}

                                {levels.lod.length > 1 && (
                                    <label
                                        className="stage-field detail"
                                        title="Set LOD - 0 is lowest detail in EaW"
                                    >
                                        <Icon name="detail" />
                                        <input
                                            type="range"
                                            min={0}
                                            max={lodSteps.count - 1}
                                            step={1}
                                            value={lodSteps.positionOf(lod)}
                                            onChange={e => {
                                                lodChosenRef.current = true;
                                                setLod(lodSteps.levelAt(Number(e.target.value)));
                                            }}
                                        />
                                        <span className="stage-value">
                                            {levelLabel('lod', lod, levels.lod,
                                                { cost: lodCost.get(lod) })}
                                        </span>
                                    </label>
                                )}
                            </div>
                        )}

                        </span>

                        {/* The ability command bar - the game's own arrangement, and the reason
                            the icons were worth resolving at all. One key per ability, pressed to
                            activate, centred under the model it acts on.

                            A LENS: absent outside Gameplay, which is a statement about which lens
                            you are in rather than about this model, and the one case the
                            disable-don't-hide rule exempts.

                            Every ability gets a key, including the ones that drive nothing - most
                            of the shipped types are ORDERS, and a unit's repertoire is worth
                            reading whole. Those keys are DISABLED and say why in their tooltip
                            rather than being left out. */}
                        <span className="stage-slot stage-slot-mid">
                        <span className="command-stack">
                        {mode === 'gameplay' && abilities.length > 0 && (
                            <div
                                className="stage-chrome ability-bar"
                                role="group"
                                aria-label="Abilities"
                            >
                                {abilities.map(ability => (
                                    <button
                                        key={ability.type}
                                        type="button"
                                        className={'ability-key'
                                            + (activeAbilities.has(ability.type) ? ' active' : '')}
                                        aria-pressed={activeAbilities.has(ability.type)}
                                        aria-label={ability.label}
                                        disabled={!ability.drivesSomething}
                                        title={abilityBarTitle(ability)}
                                        onClick={() => setAbilityActive(
                                            ability.type, !activeAbilities.has(ability.type))}
                                    >
                                        {/* The icon IS the key. Where none resolved the label
                                            stands in rather than an empty slot: the engine
                                            hardcodes these names, so a miss usually means we
                                            cannot guess the name, not that the game draws
                                            nothing. */}
                                        {ability.iconDataUri !== null ? (
                                            <img
                                                className="ability-icon"
                                                src={ability.iconDataUri}
                                                alt=""
                                            />
                                        ) : (
                                            <span className="ability-key-text">
                                                {ability.label}
                                            </span>
                                        )}
                                    </button>
                                ))}

                                {/* The reading matter the dock list used to carry. A bar of icons
                                    cannot hold a tooltip's worth of text per ability, and the jump
                                    to a definition needs a control of its own - so both live one
                                    press away instead of being dropped. */}
                                <IconButton
                                    icon="details"
                                    className={abilitiesOpen ? 'active' : undefined}
                                    expanded={abilitiesOpen}
                                    title="Show abilities"
                                    onClick={() => setAbilitiesOpen(open => !open)}
                                />
                            </div>
                        )}

                        {/* What the unit has LEFT, under the keys that act on it - the user's own
                            arrangement. The same three pools the attacker's readout lists, said as
                            bars: shields blue, the hull on the hardpoints' own ramp so a bar and a
                            targeting mark agree, and energy a hue neither of them uses.

                            Energy is absent unless its setting is on - see `poolOptions`. */}
                        {mode === 'gameplay'
                            && poolRows(scene?.defence, livePools, hull, poolOptions).length > 0 && (
                            <div
                                className="stage-chrome status-bars"
                                role="group"
                                aria-label="What the unit has left"
                            >
                                {poolRows(scene?.defence, livePools, hull, poolOptions)
                                    .map(row => (
                                    <span
                                        key={row.id}
                                        className="status-bar"
                                        title={`${row.label}: ${row.detail}`}
                                    >
                                        <span className="status-bar-track">
                                            <span
                                                className="status-bar-fill"
                                                style={{
                                                    width: `${Math.round(row.fraction * 100)}%`,
                                                    background: row.colour,
                                                }}
                                            />
                                        </span>
                                    </span>
                                ))}
                            </div>
                        )}
                        </span>
                        </span>

                        {/* The attack tool. A LENS, like the command bar beside it: absent
                            outside Gameplay rather than disabled, because its absence says which
                            lens you are in and not something about this model.

                            A button rather than the form itself - ten fields laid along the bottom
                            edge would take a third of the viewport permanently, to show a tool that
                            is used in bursts. */}
                        <span className="stage-slot stage-slot-end">
                        {mode === 'gameplay' && scene?.defence !== null
                            && scene?.defence !== undefined && (
                            <div className="stage-chrome">
                                {/* The two ACTIONS, on the edge rather than inside the flyout -
                                    the same arrangement the scene controls and the camera presets
                                    use. `as-action`, not the plain toolbar glyph: neither ever
                                    latches, and a bare icon beside controls that do reads as a
                                    switch that happens to be off. Firing is something you do repeatedly while watching the
                                    model; having to open a panel first put a lid on the one
                                    control that gets pressed most. The flyout keeps the SETTINGS,
                                    and lives in a band of its own beside this one.

                                    A unit WITH hardpoints cannot be targeted itself, so with none
                                    picked there is nothing for a shot to land on: disabled with
                                    the reason, rather than firing into nothing. The title also
                                    names the target, which the flyout's own readout used to be
                                    the only place to see. */}
                                <IconButton
                                    icon="fire"
                                    className={'as-action' + (justFired ? ' fired' : '')}
                                    title={`Fire at ${fireTarget === 'hull'
                                        ? 'the whole unit' : fireTarget}`}
                                    disabled={fireTarget === 'hull' && !shipTargetable}
                                    disabledReason="Fire - pick a hardpoint to fire at"
                                    onClick={fire}
                                />
                                <IconButton
                                    icon="repair"
                                    className="as-action"
                                    title="Repair the target"
                                    onClick={() => { repairTarget(); destroyAll(false); }}
                                />
                            </div>
                        )}

                        {/* Its own band, the way the camera corner does it: Fire and Repair ACT on
                            the model, this OPENS something, and the two are not the same kind of
                            control. Last on the right, so the stage reads outwards from the model
                            in both directions. */}
                        {mode === 'gameplay' && scene?.defence !== null
                            && scene?.defence !== undefined && (
                            <div className="stage-chrome">
                                <IconButton
                                    icon="settings"
                                    className={attackOpen ? 'active' : undefined}
                                    expanded={attackOpen}
                                    title="Weapon settings"
                                    onClick={() => setAttackOpen(open => !open)}
                                />

                                {/* Beside the settings, because it is the same kind of control -
                                    it OPENS something. What each shot actually did, so a reader
                                    can see WHY a number came out the way it did rather than
                                    watching a bar move and guessing. */}
                                <IconButton
                                    icon="log"
                                    className={logOpen ? 'active' : undefined}
                                    expanded={logOpen}
                                    title="Damage log"
                                    disabled={damageLog.length === 0}
                                    // Names the control even when it is disabled: a tooltip that
                                    // gives only the reason leaves a reader who has never opened it
                                    // no idea what it is.
                                    disabledReason="Damage log - nothing fired yet"
                                    onClick={() => setLogOpen(open => !open)}
                                />
                            </div>
                        )}
                        </span>
                        </div>
                        {/* What the command bar cannot say in an icon.

                            One row per ability: the art, the words the game shows, what it drives
                            on THIS model, and the jump to where it is defined. No switch - the bar
                            above owns that, and offering it twice would be two controls for one
                            fact. */}
                        {abilitiesOpen && (
                            <div
                                className="stage-flyout from-bottom on-mid"
                                role="dialog"
                                ref={abilitiesRef}
                            >
                                <div className="stage-flyout-head">
                                    Abilities
                                    {/* Pushed right, as every other count in the panel is. Beside
                                        the word it read as part of the title. */}
                                    <span className="section-count header-right">
                                        {abilities.length}
                                    </span>
                                    <IconButton
                                        icon="close"
                                        title="Close"
                                        onClick={() => setAbilitiesOpen(false)}
                                    />
                                </div>
                                <div className="stage-flyout-body">
                                    <ul className="part-list ability-list">
                                        {abilities.map(ability => (
                                            <li
                                                key={ability.type}
                                                data-ability={ability.type}
                                                className="hardpoint-card"
                                            >
                                                <div className="card-head">
                                                    {ability.iconDataUri !== null && (
                                                        <img
                                                            className="ability-icon"
                                                            src={ability.iconDataUri}
                                                            alt=""
                                                        />
                                                    )}
                                                    <span className="part-name">
                                                        {ability.label}
                                                    </span>

                                                    {/* In the HEAD, where an action belongs. It
                                                        was appended under the text, which put a
                                                        button at the end of a paragraph.

                                                        Disabled, not absent, where the ability
                                                        names no block: most shipped abilities name
                                                        none, and a jump that came and went would
                                                        teach nobody it is there. */}
                                                    <button
                                                        type="button"
                                                        className="goto-definition"
                                                        disabled={ability.definition === null}
                                                        title={gotoDefinitionTitle(ability)}
                                                        onClick={() => vscode.postMessage({
                                                            type: 'revealDefinition',
                                                            value: ability.definition,
                                                            referenceType: 'SpecialAbility',
                                                        })}
                                                    >
                                                        <Icon name="definition" />
                                                    </button>
                                                </div>

                                                {/* The tag, in the card's monospace: it is what
                                                    binds a proxy and what a reader greps for. */}
                                                <span className="detail card-type">
                                                    {ability.type}
                                                </span>

                                                {/* What the game tells the PLAYER it does. The one
                                                    line here not written for a modder, and now the
                                                    only one on the card's face. */}
                                                {ability.description !== null && (
                                                    <span
                                                        className="detail wraps ability-description"
                                                    >
                                                        {ability.description}
                                                    </span>
                                                )}

                                                <span className="view-row card-actions">
                                                    <IconButton
                                                        icon="details"
                                                        className={'card-info-btn'
                                                            + (infoAbility === ability.type
                                                                ? ' active' : '')}
                                                        expanded={infoAbility === ability.type}
                                                        title="Info"
                                                        disabled={abilityFacts(ability).length === 0}
                                                        disabledReason="This ability declares nothing else"
                                                        onClick={() => setInfoAbility(open =>
                                                            open === ability.type
                                                                ? null
                                                                : ability.type)}
                                                    />
                                                </span>
                                            </li>
                                        ))}
                                    </ul>
                                </div>
                            </div>
                        )}

                        {/* The panel is INVERTED against the rest of the lens: everything else
                            here describes the subject, and this describes a weapon you build to
                            shoot it with. The heading says so, because a reader who assumes these
                            are the ship's own numbers would read every field backwards. */}
                        {/* What each shot DID, newest first - the reader asked for it in the shape
                            an old-school RPG reports a hit. The bars say what is left; this says
                            how it got that way, and the armour on every line is what answers "why
                            that number".

                            Its own flyout beside the settings, not a panel inside them: a log is
                            read after the fact and the settings are set before it. */}
                        {logOpen && (
                            <div
                                className="stage-flyout from-bottom on-right"
                                role="dialog"
                                ref={logRef}
                            >
                                <div className="stage-flyout-head">
                                    Damage log
                                    <span className="section-count header-right">
                                        {damageLog.length}
                                    </span>
                                    <IconButton
                                        icon="close"
                                        title="Close"
                                        onClick={() => setLogOpen(false)}
                                    />
                                </div>
                                <div className="stage-flyout-body">
                                    <ol className="damage-log">
                                        {damageLog.map((entry, index) => (
                                            <li
                                                key={`${index}:${entry.target}`}
                                                className={entry.destroyed
                                                    ? 'log-kill'
                                                    : entry.amount > 0 ? undefined : 'log-nothing'}
                                            >
                                                {damageLine(entry)}
                                            </li>
                                        ))}
                                    </ol>

                                    <span className="view-row">
                                        <Button
                                            compact
                                            title="Empty the log"
                                            onClick={() => setDamageLog([])}
                                        >
                                            <Icon name="remove" />
                                            Clear
                                        </Button>
                                    </span>
                                </div>
                            </div>
                        )}

                        {attackOpen && (
                            <div
                                className="stage-flyout from-bottom on-right"
                                role="dialog"
                                ref={attackRef}
                            >
                                <div className="stage-flyout-head">
                                    Weapon
                                    <IconButton
                                        icon="close"
                                        title="Close"
                                        onClick={() => setAttackOpen(false)}
                                    />
                                </div>
                                <div className="stage-flyout-body">
                        {mode === 'gameplay' && scene?.defence !== null
                            && scene?.defence !== undefined && (
                            <DockSection
                                title="Attacker"
                                count={scene.defence.isShielded ? 'shielded' : 'unshielded'}
                            >

                                <div className="field-note">
                                    The model on stage is the TARGET. Build a weapon here and fire
                                    it at the unit or at one hardpoint.
                                </div>

                                <Field label="Damage">
                                    <input
                                        type="number"
                                        min={0}
                                        value={attacker.damage}
                                        onChange={e => setAttacker(current => ({
                                            ...current,
                                            damage: Number(e.target.value),
                                        }))}
                                    />
                                </Field>

                                <Field
                                    label="Damage type"
                                    valueTitle={'What the damage table says against this '
                                        + 'target`s armor. A pair the table does not name is '
                                        + '1.0, which is over half of them.'}
                                    value={<>
                                        {/* Off the LIVE defence: a ship whose generators are
                                            gone is unshielded, so the factor a shot would
                                            actually take is the hull's. Reading the scene's
                                            own copy showed the shield column beside a bar
                                            that had just gone flat. */}
                                        x{armorFactor(
                                            attacker.shield
                                                && liveDefence?.isShielded === true
                                                ? scene.defence.shieldFactors
                                                : scene.defence.hullFactors,
                                            attacker.damageType)}
                                    </>}
                                >
                                    <select
                                        value={attacker.damageType}
                                        onChange={e => setAttacker(current => ({
                                            ...current, damageType: e.target.value,
                                        }))}
                                        title="Which damage type the shot does"
                                    >
                                        {/* The reader's own value first when the tree does not
                                            declare it - a preset saved against another mod must not
                                            silently become whatever happens to sort first. */}
                                        {!scene.defence.damageTypes.includes(attacker.damageType)
                                            && (
                                            <option value={attacker.damageType}>
                                                {attacker.damageType} (not in this tree)
                                            </option>
                                        )}
                                        {scene.defence.damageTypes.map(type => (
                                            <option key={type} value={type}>{type}</option>
                                        ))}
                                    </select>
                                </Field>

                                {/* The three switches a projectile carries. They decide entirely
                                    what a hit touches - see `attacker.ts` for the four rules. */}
                                <div className="view-row">
                                    {damageSwitches(poolOptions)
                                        .map(({ id, label, title }) => (
                                        <label key={id} className="field-label" title={title}>
                                            <input
                                                type="checkbox"
                                                checked={attacker[id]}
                                                onChange={e => setAttacker(current => ({
                                                    ...current, [id]: e.target.checked,
                                                }))}
                                            />
                                            {label}
                                        </label>
                                    ))}
                                </div>

                                {/* AREA DAMAGE. A blast reaches everything within its range as
                                    well as the thing it hit, and the two numbers are independent -
                                    `Proj_Veers_AT_AT_Max_Power_Laser_Red` declares a direct damage
                                    of 0.0 alongside a blast of 40, so a weapon can do all of its
                                    work here and none above.

                                    Range is what decides who ELSE is caught, so a blast with no
                                    range reaches nobody and the fields below it are moot - they
                                    disable rather than vanish, since a reader who has typed a
                                    damage and seen nothing happen needs to see the reason. */}
                                <Field label="Blast damage">
                                    <input
                                        type="number"
                                        min={0}
                                        value={attacker.blastDamage}
                                        title="Projectile_Blast_Area_Damage - applied to the target
                                            AND to everything inside the range below"
                                        onChange={e => setAttacker(current => ({
                                            ...current,
                                            blastDamage: Number(e.target.value),
                                        }))}
                                    />
                                </Field>

                                <Field label="Blast range">
                                    <input
                                        type="number"
                                        min={0}
                                        value={attacker.blastRange}
                                        title="Projectile_Blast_Area_Range, in engine units.
                                            The target itself is always hit."
                                        onChange={e => setAttacker(current => ({
                                            ...current,
                                            blastRange: Number(e.target.value),
                                        }))}
                                    />
                                </Field>

                                <div className="view-row">
                                    <label
                                        className="field-label"
                                        title="Projectile_Blast_Area_Dropoff - the blast weakens
                                            with distance instead of being flat"
                                    >
                                        <input
                                            type="checkbox"
                                            checked={attacker.blastDropoff}
                                            disabled={attacker.blastRange <= 0}
                                            onChange={e => setAttacker(current => ({
                                                ...current,
                                                blastDropoff: e.target.checked,
                                            }))}
                                        />
                                        Falls off
                                    </label>

                                    <label
                                        className="field-label"
                                        title="Projectile_Blast_Area_Dropoff_Tiers - how many bands
                                            it falls off in. Shipped values are 3, 4 or 5."
                                    >
                                        Tiers
                                        <input
                                            type="number"
                                            min={0}
                                            max={16}
                                            className="tier-count"
                                            value={attacker.blastDropoffTiers}
                                            disabled={!attacker.blastDropoff
                                                || attacker.blastRange <= 0}
                                            onChange={e => setAttacker(current => ({
                                                ...current,
                                                blastDropoffTiers: Number(e.target.value),
                                            }))}
                                        />
                                    </label>
                                </div>

                                {/* Disabled rather than absent when the subject fires nothing: a
                                    control that comes and goes reads as a feature that does. */}
                                {/* EVERY projectile in the tree, not the handful this subject
                                    fires - you are building a weapon to shoot AT it, so its own
                                    armament is the wrong list. 105 of them, so the search is the
                                    way through rather than an optional extra. */}
                                <Field
                                    label="Fill from a projectile"
                                    value={<>{scene.projectileCatalog?.length ?? 0}</>}
                                >
                                    <input
                                        type="text"
                                        placeholder="Search projectiles"
                                        value={projectileSearch}
                                        onChange={e => setProjectileSearch(e.target.value)}
                                    />
                                    <select
                                        value=""
                                        size={6}
                                        disabled={(scene.projectileCatalog?.length ?? 0) === 0}
                                        onChange={e => {
                                            const picked = e.target.value;
                                            const bolt = scene.projectiles
                                                ?.find(p => p.id === picked);

                                            if (bolt !== undefined) {
                                                // A COPY. Edits afterwards stick.
                                                setAttacker(attackerFromProjectile(bolt, attacker));
                                                // Except the blast area, which has no field to edit
                                                // and is read off the bolt when the shot resolves.
                                                setAttackerProjectile(bolt.id);
                                                setProjectileNote(null);
                                            } else if (picked !== '') {
                                                // Everything the subject does not fire is a NAME
                                                // until it is fetched. It used to stop here and say
                                                // so, which made the catalogue read as broken - and
                                                // as incomplete, since 104 of its 105 entries do
                                                // nothing on any given subject. Now it asks.
                                                setProjectileNote(`Loading ${picked}...`);
                                                vscode.postMessage({
                                                    type: 'requestProjectile', name: picked,
                                                });
                                            }
                                        }}
                                    >
                                        {projectileChoices(
                                            scene.projectileCatalog ?? [], projectileSearch)
                                            .map(name => (
                                                <option key={name} value={name}>{name}</option>
                                            ))}
                                    </select>
                                    {projectileNote !== null && (
                                        <span className="field-note">{projectileNote}</span>
                                    )}
                                </Field>

                                {/* Presets are TIER 1: they describe the reader's testing habits,
                                    not this model, so they live in globalState beside the camera
                                    presets and survive opening a different ship. */}
                                <Field label="Saved weapons" value={<>{attackerPresets.length}</>}>
                                    <select
                                        value=""
                                        disabled={attackerPresets.length === 0}
                                        onChange={e => {
                                            const saved = attackerPresets
                                                .find(p => p.id === e.target.value);

                                            if (saved !== undefined) {
                                                const { id, name, ...weapon } = saved;
                                                setAttacker(weapon);
                                            }
                                        }}
                                        title={attackerPresets.length === 0
                                            ? 'Name a configuration below to save it here.'
                                            : 'Loads a saved weapon into the fields above.'}
                                    >
                                        <option value="">Recall a weapon...</option>
                                        {attackerPresets.map(saved => (
                                            <option key={saved.id} value={saved.id}>
                                                {saved.name}
                                            </option>
                                        ))}
                                    </select>
                                </Field>

                                <span className="view-row">
                                    <input
                                        type="text"
                                        placeholder="Name this weapon"
                                        value={presetName}
                                        onChange={e => setPresetName(e.target.value)}
                                    />
                                    <Button
                                        compact
                                        title="Saves these values under that name, for any model"
                                        disabled={presetName.trim() === ''}
                                        disabledReason="Give the weapon a name to save it"
                                        onClick={() => {
                                            const name = presetName.trim();

                                            setAttackerPresets(current => [
                                                // Saving over a name REPLACES it. Two rows reading
                                                // the same thing is worse than losing the old one,
                                                // which is what the reader just asked for anyway.
                                                ...current.filter(p => p.name !== name),
                                                { ...attacker, id: `atk-${Date.now()}`, name },
                                            ]);
                                            setPresetName('');
                                        }}
                                    >
                                        <Icon name="save" />
                                        Save
                                    </Button>
                                    <Button
                                        compact
                                        title="Remove every saved weapon"
                                        disabled={attackerPresets.length === 0}
                                        disabledReason="No saved weapons to remove"
                                        onClick={() => setAttackerPresets([])}
                                    >
                                        <Icon name="remove" />
                                        Clear
                                    </Button>
                                </span>

                            </DockSection>
                        )}
                                </div>
                            </div>
                        )}


                    </div>

                    {problemsOpen && notices.length > 0 && (
                        <ProblemsPanel
                            className="preview-problems"
                            memoKey="modelPreview.problems"
                            defaultHeight={140}
                            title={`Problems (${notices.length})`}
                            onClose={() => setProblemsOpen(false)}
                        >
                            {/* The scrolling part, and the reason the panel scrolls at all: the
                                shared sheet gives `.problem-list` the overflow and leaves the
                                title bar its height. Mapping the rows straight into the panel
                                skipped it, and the list simply ran off the bottom. */}
                            <div className="problem-list">
                                {notices.map((problem, index) => {
                                    // Three severities, drawn as three. The ternary that used to
                                    // be here sent every `info` out under the warning triangle -
                                    // the one thing on the row that answers "must I fix this?".
                                    const look = problemLook(problem.severity);
                                    const where = problemWhere(
                                        problem.hardpointId, problem.message);
                                    const tag = problemTag(problem.diagnosticId);

                                    return (
                                        <div key={index} className="problem-row">
                                            <span
                                                className={`codicon codicon-${look.icon} `
                                                    + `sev-${look.tone}`}
                                                aria-hidden="true"
                                            />
                                            {where !== null && (
                                                <span className="problem-where">{where}</span>
                                            )}
                                            <span
                                                className="problem-msg"
                                                title={problem.message}
                                            >
                                                {problem.message}
                                            </span>
                                            {/* Last, and dim. It is what makes the finding
                                                referable, not what the reader is here to read. */}
                                            <span
                                                className={'problem-id'
                                                    + (tag.isDiagnostic ? '' : ' render-time')}
                                                title={tag.title}
                                            >
                                                {tag.text}
                                            </span>
                                        </div>
                                    );
                                })}
                            </div>
                        </ProblemsPanel>
                    )}
                </div>

                <RightDock
                    memoKey="modelPreview.dock"
                    initialWidth={260}
                    minWidth={200}
                    maxWidth={460}
                    header={<>
                        {/* The graph's layout exactly: one button anchored left where its save sits,
                            the dial alone in the centred slot, one anchored right. The preview has
                            nothing to save, so the left slot carries what the model IS - its name
                            and its counts - which frees the centre entirely for the dial. */}
                        <IconButton
                            icon="details"
                            className={'header-left' + (infoOpen ? ' active' : '')}
                            expanded={infoOpen}
                            title={scene?.subject ?? 'No model'}
                            onClick={() => setInfoOpen(open => !open)}
                        />

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
                                                title="The engine lights everything with the three scene
                                                    directionals instead"
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
                                        weapons: scene.weapons.length,
                                        abilities: abilities.length,
                                    })}
                                    onSelect={setMode}
                                />

                                {/* Hand-off, not view controls: this preview is read-only by design
                                    and these are where editing happens. Under the dial rather than
                                    in a corner, because they are about the subject it names. */}
                                {scene.kind === 'Model' && (
                                    <span className="header-handoff">
                                        <IconButton
                                            icon="open"
                                            title="Open this model in AloViewer"
                                            onClick={() => vscode.postMessage(
                                                { type: 'openExternal', tool: 'model' })}
                                        />
                                        <IconButton
                                            icon="bloom"
                                            title="Open this file in the Particle Editor"
                                            onClick={() => vscode.postMessage(
                                                { type: 'openExternal', tool: 'particles' })}
                                        />
                                    </span>
                                )}
                            </span>
                        )}

                        <SeverityTag
                            severity={severity}
                            count={notices.length}
                            expanded={problemsOpen}
                            disabled={notices.length === 0}
                            disabledReason="Nothing to report about this model"
                            title={`${problemsOpen ? 'Hide' : 'Read'} `
                                + `${notices.length} ${problemWord}`}
                            onClick={() => setProblemsOpen(open => !open)}
                        />


                    </>}
                    content={<>
                        {mode === 'model' && treeItems.length > 0 && (
                            // The count is inside `end` rather than given as `count`, because this
                            // one carries a title of its own when rows are filtered out - which a
                            // plain count cannot. The order within the group is still fixed, and
                            // the group still takes the section's one auto margin.
                            <DockSection
                                title="Model tree"
                                end={<>
                                    {/* The way OUT of a selection. Escape has always cleared
                                        it and clicking the row again does now, but neither is
                                        visible - the reader's complaint was that there was no
                                        obvious way back, and a count that does nothing was the
                                        only thing on screen saying a selection existed. */}
                                    {selected.size > 0 && (
                                        <button
                                            className="section-count as-button"
                                            title="Clear the selection"
                                            onClick={clearSelection}
                                        >
                                            {selected.size} selected
                                            <Icon name="close" size={11} />
                                        </button>
                                    )}

                                    {/* BOTH, when both apply. The selection readout used to
                                        replace the count outright, which put an active filter
                                        back out of sight the moment a row was picked - and
                                        hiding a filter is the one thing this header exists to
                                        stop. They are separate facts about the tree. */}
                                    <span
                                        className="section-count"
                                        title={filterSummary === null
                                            ? undefined
                                            : 'Some rows are filtered out'}
                                    >
                                        {filterSummary ?? treeRows.length}
                                    </span>

                                    {/* Reset stands with the count because they are the same
                                        corner of the same header: one says what state the tree
                                        is in, the other is the way out of it. Below the list it
                                        was a row of its own, a hundred rows from what it
                                        undoes. */}
                                    <IconButton
                                        icon="reset"
                                        className="header-right"
                                        size={13}
                                        title="Put the model back the way it opened"
                                        disabled={!touched}
                                        disabledReason="The model is as it opened"
                                        onClick={resetModel}
                                    />
                                </>}
                            >

                                {/* The list and the search element share a box, because the second
                                    one is drawn ON the first. Positioned against this rather than
                                    against the section, so the strip lands inside the tree's own
                                    bounds and not over the skeleton control below it. */}
                                <div className="tree-pane">
                                <ul
                                    className="bone-tree"
                                    style={{ minHeight: treeMinHeight(unfilteredRowCount) }}
                                >
                                    {treeRows.map(({ node, expandable, expanded }) => (
                                        <li
                                            key={node.id}
                                            data-row={node.id}
                                            className={
                                                'bone-row'
                                                + (selected.has(node.id) ? ' selected' : '')
                                                + (node.gatedOff ? ' hidden-bone' : '')
                                                + (node.scaffold === true ? ' scaffold' : '')
                                            }
                                            style={{ paddingLeft: 6 + node.depth * 10 }}
                                            title={node.because === undefined
                                                ? node.name
                                                : `${node.name}
${becauseText(node.because)}`}
                                            onClick={event => {
                                                // Clicking the row that is ALREADY the whole
                                                // selection clears it. There was no other way out
                                                // of a selection at all - the box and the axes
                                                // stayed until you picked something else.
                                                const plain = !(event.ctrlKey || event.metaKey)
                                                    && !event.shiftKey;

                                                if (plain && selected.size === 1
                                                    && selected.has(node.id)) {
                                                    clearSelection();
                                                    return;
                                                }

                                                const after = selectionAfterClick(
                                                    treeRows, selected, anchor, node.id, {
                                                        ctrl: event.ctrlKey || event.metaKey,
                                                        shift: event.shiftKey,
                                                    });
                                                setSelected(after.selected);
                                                setAnchor(after.anchor);

                                                if (node.kind === 'bone') {
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

                                            {/* Blender's arrangement, and the reader asked for it
                                                by name: an open eye, a half-shut one for a row
                                                whose visibility is inherited from above, and a
                                                closed one for a row that is off. A tick could only
                                                ever say on or off, which is one bit short - it
                                                could not tell "the file hides this, press to show
                                                it" from "a bone above it is hidden, pressing does
                                                nothing", and the second one made the control look
                                                broken. See `rowEye.ts`. */}
                                            {(() => {
                                                const eye = rowEye({
                                                    visible: node.visible,
                                                    decidedBy: node.decidedBy ?? 'file',
                                                    authored: !node.gatedOff,
                                                });

                                                return (
                                                    <IconButton
                                                        icon={EYE_ICONS[eye.state]}
                                                        className={`row-eye eye-${eye.state}`}
                                                        size={13}
                                                        pressed={eye.state === 'shown'}
                                                        title={eye.title}
                                                        disabled={!eye.canAct}
                                                        // Already the reason when it cannot act:
                                                        // rowEye phrases the title from the state,
                                                        // and a row that cannot be hidden says so.
                                                        disabledReason={eye.title}
                                                        onClick={event => {
                                                            event.stopPropagation();
                                                            setRowsVisible(node.id, !node.visible);
                                                        }}
                                                    />
                                                );
                                            })()}

                                            {/* Dim until the row is under the pointer or already
                                                open, so a deep tree stays a list of names rather
                                                than a column of buttons - but always THERE, since
                                                a control that only exists on hover is one nobody
                                                finds. */}
                                            <IconButton
                                                icon="details"
                                                className={'row-details'
                                                    + (detailsRow === node.id ? ' active' : '')}
                                                size={13}
                                                // pressed, not expanded: it opens an editor tab
                                                // beside this one, which is not this control
                                                // disclosing something underneath it.
                                                pressed={detailsRow === node.id}
                                                title={`Inspect ${node.name}`}
                                                onClick={event => {
                                                    event.stopPropagation();
                                                    openInspector(node.id);
                                                }}
                                            />
                                        </li>
                                    ))}
                                    {/* Inside the list, not after it. Below the box it was one
                                        more thing appearing where rows used to be, so the panel
                                        still moved - just by the height of the message instead of
                                        the height of the tree. */}
                                    {treeRows.length === 0 && (
                                        <li className="dock-hint tree-empty">
                                            Nothing matches that filter.
                                        </li>
                                    )}
                                </ul>
                                {/* Show and Hide used to sit here, doing what the row's own tick
                                    does - and doing it to whatever happened to be selected, which
                                    is a second way to say the same thing and a second thing to get
                                    wrong. What the panel actually lacked was a way BACK: after
                                    twenty ticks, a damage level and a detail level, nothing said
                                    what the model looked like when it was opened. */}


                                {/* ONE element, floating on the tree's bottom edge.

                                    The pattern and the kinds are the same question - which rows do
                                    I want - and they stood as two unrelated blocks with a gap
                                    between them, the input full width and the icons adrift
                                    beneath it. A mesh origin IS a bone and an emitter attaches to
                                    one, so all three kinds belong here rather than split across
                                    panels.

                                    ON the tree rather than under it, so it costs the list no
                                    height at all. The list carries a matching pad at its foot, so
                                    the last row can still be scrolled clear of the strip. */}
                                <div
                                    className={'tree-search' + (searchOpen ? ' open' : '')}
                                    ref={searchRef}
                                >
                                    {/* The box grows into the space to the LEFT of the icons,
                                        which never move. That is what makes the strip a fixed
                                        anchor rather than a row that rearranges itself: press the
                                        magnifier and the field appears beside it, press again and
                                        it is gone, and nothing you were pointing at has shifted. */}
                                    {searchOpen && (
                                        <input
                                            className="tree-filter"
                                            type="text"
                                            autoFocus
                                            value={boneFilter}
                                            placeholder="Filter the tree, e.g. HP_"
                                            onChange={e => setBoneFilter(e.target.value)}
                                            onKeyDown={e => {
                                                if (e.key === 'Escape') {
                                                    e.stopPropagation();
                                                    setBoneFilter('');
                                                    setSearchPressed(false);
                                                }
                                            }}
                                        />
                                    )}

                                    {/* The four icons are ONE flex item, so the plate can only
                                        wrap between the field and the strip - never in the middle
                                        of the strip. Left loose, the magnifier stayed up on the
                                        field's line and the three kinds dropped below it, which is
                                        neither of the two layouts this is meant to have. */}
                                    <div className="tree-search-icons">
                                    <IconButton
                                        icon="search"
                                        className={'tree-search-toggle'
                                            + (searchOpen ? ' active' : '')}
                                        expanded={searchOpen}
                                        title={searchOpen
                                            ? 'Close the filter'
                                            : 'Filter the tree by name'}
                                        onClick={() => {
                                            // Closing clears. A pattern kept behind a closed box
                                            // would reopen it immediately - see searchIsOpen - so
                                            // the press would appear to do nothing at all.
                                            if (searchOpen) {
                                                setBoneFilter('');
                                            }
                                            setSearchPressed(open => !open);
                                        }}
                                    />

                                    {/* Always here, open or closed. A pattern and a kind are two
                                        halves of the same question, and hiding one of them the
                                        moment you start typing makes narrowing by kind a round
                                        trip out of the box you are typing in. */}
                                    {TREE_KINDS.map(kind => (
                                        <IconButton
                                            key={kind}
                                            icon={KIND_ICONS[kind]}
                                            className={kinds.has(kind) ? 'active' : undefined}
                                            pressed={kinds.has(kind)}
                                            title={`Show ${KIND_LABELS[kind].toLowerCase()}`}
                                            onClick={() => setKinds(current => {
                                                const next = new Set(current);
                                                if (!next.delete(kind)) {
                                                    next.add(kind);
                                                }
                                                return next;
                                            })}
                                        />
                                    ))}
                                    </div>
                                </div>
                                </div>
                            </DockSection>
                        )}

                        {/* Bulk switches over the same systems the tree lists one by one - not a
                            second tree. Each row reads its state back OUT of those systems, so a
                            group goes half-lit the moment one of its members is switched off in the
                            tree, and there is only ever one answer to "is this effect playing".

                            One row per MEANING. There used to be two groupers - one over the gates
                            the model assigns, one over whole system names - and they listed the
                            same systems twice: a Nebulon-B showed "Hardpoint damage" and
                            "p_hp_stardestroyer_damage" as separate rows over the same six proxies,
                            so ticking either moved the other and nothing said why. See
                            `FAMILIES` in `particleScene.ts` for how a meaning is recognised. */}
                        {mode === 'model' && groups.length > 0 && (
                            <DockSection title="Effect groups" count={groups.length}>
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
                            </DockSection>
                        )}

                        {emitters.length > 0 && (
                            <DockSection title="Emitters" count={emitters.length}>
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
                            </DockSection>
                        )}


                        {mode === 'animation' && (
                            <DockSection title="Animations" count={animations.length}>

                                {animations.length === 0 ? (
                                    <div className="field-note">
                                        This model carries no animations. An .ala beside it
                                        only counts when its skeleton matches.
                                    </div>
                                ) : (
                                    <div className="anim-list">
                                        {/* A dropdown made every clip but one invisible, and hid
                                            the one thing a modder wants from an animation list:
                                            what KINDS of motion this unit has. */}
                                        <button
                                            type="button"
                                            className={'anim-row' + (animation === null
                                                ? ' active'
                                                : '')}
                                            title="Stop - show the rest pose"
                                            onClick={() => setAnimation(null)}
                                        >
                                            <Icon name="stop" />
                                            <span className="anim-name">Rest pose</span>
                                        </button>

                                        {/* Family, then action, then its takes.

                                            Three levels because the names carry three facts and
                                            flattening them lost two: nine idle files read as nine
                                            idles rather than as ONE idle the engine has nine
                                            recordings of, and `crouchidle` read as its own kind of
                                            motion rather than as the idle held while the unit is
                                            spread out. See `animationNames.ts`.

                                            Tiles and a foldable heading, which is the shape the
                                            story graph's palette already uses for its events and
                                            rewards - the reader asked for that one by name. */}
                                        {animationGroups.map(group => {
                                            const folded = foldedFamilies.has(group.family);

                                            return (
                                            <div className="dock-section anim-family"
                                                key={group.family}>
                                                <div
                                                    className="dock-section-title as-fold"
                                                    role="button"
                                                    tabIndex={0}
                                                    aria-expanded={!folded}
                                                    title={folded
                                                        ? `Open ${group.label.toLowerCase()}`
                                                        : `Fold ${group.label.toLowerCase()} away`}
                                                    onClick={() => toggleFamily(group.family)}
                                                    onKeyDown={event => {
                                                        if (event.key === 'Enter'
                                                            || event.key === ' ') {
                                                            event.preventDefault();
                                                            toggleFamily(group.family);
                                                        }
                                                    }}
                                                >
                                                    <Icon
                                                        name={folded ? 'collapsed' : 'expanded'}
                                                        size={13}
                                                    />
                                                    {group.label}
                                                    <span className="title-end">
                                                        <span className="section-count">
                                                            {group.actions.length}
                                                        </span>
                                                    </span>
                                                </div>

                                                {!folded && (
                                                    <div className="anim-list">
                                                        {group.actions.map(action => (
                                                            <AnimationTile
                                                                key={action.id}
                                                                action={action}
                                                                playing={animation}
                                                                onPlay={playAction}
                                                                onPick={pickClip}
                                                            />
                                                        ))}
                                                    </div>
                                                )}
                                            </div>
                                            );
                                        })}
                                    </div>
                                )}
                            </DockSection>
                        )}

                        {/* Nothing to say only when the subject is unarmed, unattached AND unable.
                            A fighter carries its guns on the unit itself and declares no hardpoints
                            at all, and telling it there is nothing here would be wrong twice. */}
                        {mode === 'gameplay' && (scene?.hardpoints.length ?? 0) === 0
                            && weapons.length === 0 && abilities.length === 0 && (
                            <DockSection title="Gameplay">
                                <div className="field-note">
                                    This unit declares no weapons, no hardpoints and no
                                    abilities, so there is nothing to destroy and no damage effects
                                    to drive.
                                </div>
                            </DockSection>
                        )}

                        {/* What is LEFT after the cards: the weapons without
                            hardpoint. A fighter carries its guns on the unit itself and declares no
                            hardpoints at all, so this is not an edge case - it is every fighter. */}
                        {mode === 'gameplay' && looseWeapons.length > 0 && (
                            <DockSection title="Unit weapons" count={looseWeapons.length}>

                                {/* The hardpoints' own card, which is what the user asked for -
                                    same shape, same list, same info button. What a unit weapon has
                                    not got is a POOL: it is not a target, so there is no health bar
                                    and no destroy switch. What it does have is its muzzles, and
                                    often more of them than a hardpoint carries. */}
                                <ul className="part-list hardpoint-cards">
                                    {looseWeapons.map(row => (
                                        <li
                                            key={row.id}
                                            className="hardpoint-card weapon-card"
                                            data-card={row.id}
                                            title={row.id}
                                        >
                                            <div className="card-head">
                                                <span className="part-name">{row.name}</span>
                                            </div>

                                            {/* What the file calls it. The hardpoint card puts its
                                                Type here for the same reason: it is game data a
                                                reader greps for, not prose. */}
                                            {row.label !== '' && row.label !== row.name && (
                                                <span className="detail card-type">{row.label}</span>
                                            )}

                                            {/* The turret, where it declares one. No bone line: a
                                                unit weapon hangs off the hull rather than an
                                                attachment bone of its own. */}
                                            {unitWeaponFacts(row).map(fact => (
                                                <span className="detail" key={fact}>{fact}</span>
                                            ))}

                                            {fireBoneButtons(row)}

                                            <span className="view-row card-actions">
                                                {weaponToggles(row)}

                                                {/* The sweep, which until now lived ONLY on a
                                                    hardpoint card - so the AT-AA, whose turret is
                                                    on its weapon and which has no hardpoints at
                                                    all, had nothing to press. */}
                                                <IconButton
                                                    icon="loop"
                                                    className={sweeping.has(row.id) ? 'active' : undefined}
                                                    pressed={sweeping.has(row.id)}
                                                    title="Move turret"
                                                    disabled={!sweepable.some(s => s.id === row.id)}
                                                    disabledReason="No traverse declared"
                                                    onClick={event => {
                                                        event.stopPropagation();
                                                        setSweeping(current =>
                                                            toggleSelected(current, row.id));
                                                    }}
                                                />

                                                <IconButton
                                                    icon="details"
                                                    className={'card-info-btn'
                                                        + (infoCard === row.id ? ' active' : '')}
                                                    expanded={infoCard === row.id}
                                                    title="Info"
                                                    onClick={event => {
                                                        event.stopPropagation();
                                                        setInfoCard(open =>
                                                            open === row.id ? null : row.id);
                                                    }}
                                                />
                                            </span>
                                        </li>
                                    ))}
                                </ul>
                            </DockSection>
                        )}

                        {mode === 'gameplay' && cards.length > 0 && (
                            <DockSection
                                title="Hardpoints"
                                count={cards.length}
                                end={<>
                                <IconButton
                                    icon="effects"
                                    className="header-right"
                                    size={13}
                                    title="Destroy all hardpoints"
                                    disabled={!cards.some(c => c.isDestroyable)}
                                    disabledReason="No hardpoint on this model can be destroyed"
                                    onClick={() => destroyAll(true)}
                                />
                                <IconButton
                                    icon="reset"
                                    className="header-right"
                                    size={13}
                                    title="Repair all hardpoints"
                                    disabled={destroyed.size === 0}
                                    disabledReason="Nothing is destroyed"
                                    onClick={() => destroyAll(false)}
                                />
                                </>}
                            >

                                <div className="field-note">
                                    Hover a targeting mark to see its tracked art; click one to aim
                                    the attacker at that hardpoint.
                                </div>

                                {/* The tree's own search element, in the same shape: a magnifier
                                    that opens leftward into a field, with the icons anchored to
                                    the right so nothing moves when it does. Filters on the TYPE,
                                    which is what the groups are, and on the name, which is what a
                                    reader looking for one hardpoint knows. */}
                                <div
                                    className={'tree-search hardpoint-search'
                                        + (hardpointSearchOpen ? ' open' : '')}
                                    ref={hardpointSearchRef}
                                >
                                    {hardpointSearchOpen && (
                                        <input
                                            className="tree-filter"
                                            type="text"
                                            autoFocus
                                            value={hardpointFilter}
                                            placeholder="Filter by type or name"
                                            onChange={e => setHardpointFilter(e.target.value)}
                                            onKeyDown={e => {
                                                if (e.key === 'Escape') {
                                                    e.stopPropagation();
                                                    setHardpointFilter('');
                                                    setHardpointSearchPressed(false);
                                                }
                                            }}
                                        />
                                    )}
                                    <div className="tree-search-icons">
                                        <IconButton
                                            icon="search"
                                            className={'tree-search-toggle'
                                                + (hardpointSearchOpen ? ' active' : '')}
                                            expanded={hardpointSearchOpen}
                                            title={hardpointSearchOpen
                                                ? 'Close the filter'
                                                : 'Filter by type or name'}
                                            onClick={() => {
                                                if (hardpointSearchOpen) {
                                                    setHardpointFilter('');
                                                }
                                                setHardpointSearchPressed(open => !open);
                                            }}
                                        />
                                    </div>
                                </div>

                                {hardpointGroups.length === 0 && (
                                    <div className="field-note">Nothing matches that filter.</div>
                                )}

                                {hardpointGroups.map(group => (
                                <Fragment key={group.type}>
                                <button
                                    type="button"
                                    className="card-group-title"
                                    aria-expanded={!foldedGroups.has(group.type)}
                                    title={foldedGroups.has(group.type)
                                        ? `Show the ${group.label.toLowerCase()} hardpoints`
                                        : `Hide the ${group.label.toLowerCase()} hardpoints`}
                                    onClick={() => setFoldedGroups(
                                        current => toggleSelected(current, group.type))}
                                >
                                    <Icon
                                        name={foldedGroups.has(group.type)
                                            ? 'collapsed'
                                            : 'expanded'}
                                        size={13}
                                    />
                                    {group.label}
                                    <span className="section-count">{group.cards.length}</span>
                                </button>
                                {!foldedGroups.has(group.type) && (
                                <ul className="part-list hardpoint-cards">
                                    {group.cards.map(card => (
                                        <li
                                            key={card.id}
                                            data-card={card.id}
                                            className={'hardpoint-card'
                                                + (fireTarget === card.id ? ' selected' : '')
                                                + (card.destroyed ? ' gone' : '')}
                                            /* Picking the card aims at it, which is the same act
                                               as picking it in the attacker's Fire at list and
                                               the same act as clicking its targeting mark. One
                                               meaning, three ways in. */
                                            onClick={() => selectHardpoint(card.id)}
                                        >
                                            <div className="card-head">
                                                {/* The art the GAME draws over this hardpoint,
                                                    the same image the marks on the model use. It
                                                    is picked by Type, so seeing it here is how a
                                                    reader checks that the Type they wrote resolves
                                                    to the mark they expected. */}
                                                {reticleFor(scene?.reticles, card.type) !== null
                                                    && (
                                                    <img
                                                        className="card-reticle"
                                                        src={reticleFor(
                                                            scene?.reticles, card.type) ?? ''}
                                                        alt=""
                                                        title={card.isTargetable
                                                            ? 'The mark the game draws over this'
                                                            : 'Is_Targetable is off, so the game '
                                                                + 'draws no mark over this'}
                                                    />
                                                )}
                                                <span className="part-name">{card.id}</span>

                                                {/* Top right, away from the switches: this leaves
                                                    the panel, and a jump standing in a row of
                                                    toggles reads as another toggle. */}
                                                <button
                                                    type="button"
                                                    className="goto-definition"
                                                    title={`Go to where ${card.id} is defined`}
                                                    onClick={event => {
                                                        event.stopPropagation();
                                                        vscode.postMessage({
                                                            type: 'revealDefinition',
                                                            value: card.id,
                                                            referenceType: 'HardPoint',
                                                        });
                                                    }}
                                                >
                                                    <Icon name="definition" />
                                                </button>
                                            </div>

                                            {/* The game's own words for it. The Type is what picks
                                                its reticle; the tooltip is what a player reads. */}
                                            {card.type !== null && (
                                                <span className="detail card-type">{card.type}</span>
                                            )}
                                            {/* What a PLAYER reads, with the key the author wrote
                                                on the hover. The card showed the key itself before
                                                the server resolved it, which is the one line on a
                                                card that nobody outside the files ever sees. */}
                                            {card.tooltip !== null && (
                                                <span
                                                    className="detail wraps"
                                                    title={card.tooltipKey ?? undefined}
                                                >
                                                    {card.tooltip}
                                                </span>
                                            )}

                                            {/* The key alone, when it resolved to nothing. Said as
                                                a key rather than dressed up as text: a row with no
                                                translation is the localisation editor's to fix, and
                                                showing it as prose would hide that. */}
                                            {card.tooltip === null && card.tooltipKey !== null && (
                                                <span
                                                    className="detail card-type"
                                                    title="This key resolves to no text"
                                                >
                                                    {card.tooltipKey}
                                                </span>
                                            )}

                                            {/* The pool, as a bar. The colour is the ramp the
                                                targeting marks on the model use, so a mark gone
                                                orange out there and a bar gone orange in here are
                                                saying the same thing. */}
                                            {healthBar(card, hardpointHealth[card.id] ?? null)
                                                !== null && (
                                                <span className="card-health">
                                                    <span
                                                        className={'card-health-fill'
                                                            + (card.destroyed ? ' gone' : '')}
                                                        style={{
                                                            width: `${Math.round(healthBar(
                                                                card,
                                                                hardpointHealth[card.id] ?? null,
                                                            )!.fraction * 100)}%`,
                                                            background: healthBar(
                                                                card,
                                                                hardpointHealth[card.id] ?? null,
                                                            )!.colour,
                                                        }}
                                                    />
                                                    <span className="card-health-label">
                                                        {healthBar(
                                                            card,
                                                            hardpointHealth[card.id] ?? null,
                                                        )!.label}
                                                    </span>
                                                </span>
                                            )}

                                            {card.weapon !== null
                                                && fireBoneButtons(card.weapon)}

                                            <span className="view-row card-actions">
                                                {/* The glyph follows the tooltip: a wrench once it
                                                    is destroyed, because that is what pressing it
                                                    does now. Alive, it is the same reticle the
                                                    stage's Fire wears - shooting a hardpoint off
                                                    and pressing this are one outcome, and two
                                                    glyphs for it said they were different things. */}
                                                <IconButton
                                                    icon={card.destroyed ? 'repair' : 'fire'}
                                                    className={card.destroyed ? 'active' : undefined}
                                                    pressed={card.destroyed}
                                                    title={card.destroyed
                                                        ? 'Repair hardpoint'
                                                        : 'Destroy hardpoint'}
                                                    disabled={!card.isDestroyable}
                                                    disabledReason="Is_Destroyable is off"
                                                    onClick={event => {
                                                        event.stopPropagation();
                                                        setHardpointDestroyed(
                                                            card.id, !card.destroyed);
                                                    }}
                                                />

                                                {card.weapon !== null
                                                    && weaponToggles(card.weapon)}

                                                {/* The sweep belongs to the hardpoint that
                                                    declares the traverse. One button swinging
                                                    everything at once could not say what it would
                                                    do on a hull whose hardpoints differ. */}
                                                <IconButton
                                                    icon="loop"
                                                    className={sweeping.has(card.id) ? 'active' : undefined}
                                                    pressed={sweeping.has(card.id)}
                                                    title="Move turret"
                                                    disabled={!card.sweepable}
                                                    disabledReason="No traverse declared"
                                                    onClick={event => {
                                                        event.stopPropagation();
                                                        setSweeping(current =>
                                                            toggleSelected(current, card.id));
                                                    }}
                                                />

                                                    {/* Pushed to the far end: the three on the
                                                        left DO something to the model, and this
                                                        one only says what the file holds. */}
                                                    <IconButton
                                                        icon="details"
                                                        className={'card-info-btn'
                                                            + (infoCard === card.id
                                                                ? ' active' : '')}
                                                        expanded={infoCard === card.id}
                                                        title="Info"
                                                        onClick={event => {
                                                            event.stopPropagation();
                                                            setInfoCard(open =>
                                                                open === card.id ? null : card.id);
                                                        }}
                                                    />
                                            </span>


                                        </li>
                                    ))}
                                </ul>
                                )}
                                </Fragment>
                                ))}
                            </DockSection>
                        )}
                        {mode === 'gameplay'
                            && ((scene?.deathClones?.length ?? 0) > 0
                                || (scene?.spinAway ?? null) !== null) && (
                            <DockSection title="Death clone" count={(scene?.deathClones?.length ?? 0)
                                            + (scene?.spinAway === null
                                                || scene?.spinAway === undefined ? 0 : 1)}>

                                {/* The automated one. Its own card, and marked the way a real clone
                                    is when it is what would happen - which is whenever the object
                                    declares no clone at all, since the two never coexist: not one
                                    of the 34 objects declaring `Spin_Away_On_Death` also declares a
                                    `Death_Clone`.

                                    The chance is READ OUT, not rolled - shipped values are 20% and
                                    40%, and a preview that honoured them would look broken four
                                    presses out of five. */}
                                {scene?.spinAway !== null && scene?.spinAway !== undefined && (
                                    <ul className="part-list hardpoint-cards">
                                        <li
                                            className={'hardpoint-card clone-card'
                                                + ((scene?.deathClones?.length ?? 0) === 0
                                                    ? ' selected' : '')}
                                            title="Spin_Away_On_Death"
                                        >
                                            <div className="card-head">
                                                {(scene?.deathClones?.length ?? 0) === 0
                                                    && <Icon name="check" />}
                                                <span className="part-name">Spins away</span>
                                            </div>

                                            <span className="detail card-type">
                                                {scene.spinAway.explosion
                                                    ?? scene.deathExplosions ?? 'no explosion named'}
                                            </span>

                                            <span className="detail">
                                                {spinAwaySummary(scene.spinAway)}
                                            </span>
                                        </li>
                                    </ul>
                                )}

                                {/* One card per mapping: this damage type leaves that object
                                    behind. The same card shape the hardpoints and the abilities
                                    use, because it is the same kind of thing - a named piece of
                                    game data with somewhere to jump to.

                                    Which one you get depends on what KILLED it, which is the weapon
                                    built in the Attacker panel - so the marked card follows the
                                    damage type set there rather than being a static list. The tick
                                    says which, and used to need a sentence under the heading to say
                                    so. */}
                                <ul className="part-list hardpoint-cards">
                                    {deathCloneRows(scene?.deathClones ?? [], attacker.damageType)
                                        .map(row => (
                                        <li
                                            key={`${row.label}:${row.objectId}`}
                                            className={'hardpoint-card clone-card'
                                                + (row.selected ? ' selected' : '')}
                                            title={row.objectId}
                                        >
                                            <div className="card-head">
                                                {row.selected && <Icon name="check" />}
                                                <span className="part-name">{row.label}</span>

                                                <button
                                                    type="button"
                                                    className="goto-definition"
                                                    title={`Go to where ${row.objectId} is defined`}
                                                    onClick={() => vscode.postMessage({
                                                        type: 'revealDefinition',
                                                        value: row.objectId,
                                                        referenceType: 'GameObject',
                                                    })}
                                                >
                                                    <Icon name="definition" />
                                                </button>
                                            </div>

                                            {/* The object it names, in the monospace a reader would
                                                grep their own files in. */}
                                            <span className="detail card-type">{row.objectId}</span>

                                            {/* The one thing worth saying beyond the mapping: there
                                                is no wreck. Either the object is not defined at all
                                                - a typo the game is silent about - or it declares
                                                no tactical model. */}
                                            {!row.resolved && (
                                                <span className="detail wraps card-unresolved">
                                                    Not defined, or declares no model
                                                </span>
                                            )}
                                        </li>
                                    ))}
                                </ul>
                            </DockSection>
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
                            abilities: activeAbilities.size,
                        }).map(chip => (
                            <Button
                                key={chip.mode}
                                compact
                                title={`Switch to ${chip.mode} mode`}
                                onClick={() => setMode(chip.mode)}
                            >
                                {chip.text}
                            </Button>
                        ))}

                        {/* HOW YOU LOOK at the model, which is what the foot is for - the dock's
                            three levels each answer a different question, and "draw the skeleton"
                            is not a fact about the model the way the tree above it is. It sat
                            under the tree because it is the thing the tree is a list OF; that is
                            an argument about subject matter, and the level is decided by what a
                            control DOES.

                            One control rather than two, because "draw the skeleton" and "label its
                            bones" were never independent - nobody wants labels floating with no
                            skeleton under them, and the old pair let you ask for exactly that. */}
                        {mode === 'model' && (
                            <div className="field skeleton-field">
                                <span className="field-label">Skeleton</span>
                                <ModeSelector
                                    label="How the skeleton is drawn"
                                    value={skeletonMode}
                                    options={[
                                        { id: 'off', label: 'Off', title: 'No skeleton' },
                                        {
                                            id: 'selected', label: 'Selected',
                                            title: 'Name the picked bone only',
                                        },
                                        {
                                            id: 'always', label: 'Always',
                                            // The dropping rule is real and worth knowing, but it
                                            // is a thing the reader SEES the moment they press
                                            // this on a large hull. The tooltip's job is to get
                                            // them there.
                                            title: 'Name every bone',
                                        },
                                    ]}
                                    onSelect={setSkeletonMode}
                                />
                            </div>
                        )}

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
                                {/* The ACTION and the take, which is what the reader picked - the
                                    filename repeats the model's name back at them. A rouletting
                                    action says so, because the clip under the playhead is about to
                                    change on its own and a readout that did not mention it would
                                    look like a bug. */}
                                <span className="section-count">
                                    {animation === null
                                        ? 'no clip'
                                        : playheadLabel(
                                            actionOfClip.get(animation) ?? null,
                                            animation,
                                            roulette !== null)}
                                </span>
                            </div>

                            {/* The layout every media player has had for thirty years, because
                                it needs no explaining: transport on the left, time on the right,
                                the scrub bar across the bottom. */}
                            <div className="player">
                                {/* The order and the glyphs a CD player uses, which is the
                                    reader's own reference: |<< to the previous clip, |< to this
                                    clip's first frame, play, >| to its last frame, >>| to the next
                                    clip, then the repeat latch. Last frame used to draw a bare play
                                    triangle - the same glyph as Play, saying the opposite of what
                                    it did. */}
                                <div className="player-row">
                                    {/* Every one of these says why it cannot be pressed. Four of
                                        them used to go quiet instead - "Previous clip" on a control
                                        that could not step - and only Play had ever been given the
                                        sentence. Its wording is reused rather than reinvented: it
                                        is already this panel's way of saying the same state. */}
                                    <IconButton
                                        icon="previousClip"
                                        title="Previous clip"
                                        disabled={orderedClips.length < 2}
                                        disabledReason="Only one clip"
                                        onClick={() => step(-1)}
                                    />
                                    <IconButton
                                        icon="firstFrame"
                                        title="First frame"
                                        disabled={animation === null}
                                        disabledReason="Pick a clip below to play it"
                                        onClick={() => seek(0)}
                                    />
                                    <IconButton
                                        icon={playing ? 'pause' : 'play'}
                                        title={playing
                                            ? 'Pause'
                                            : atEnd && !animationLoop
                                                ? 'Play again from the first frame'
                                                : 'Play'}
                                        disabled={animation === null}
                                        disabledReason="Pick a clip below to play it"
                                        onClick={togglePlay}
                                    />
                                    <IconButton
                                        icon="lastFrame"
                                        title="Last frame"
                                        disabled={animation === null}
                                        disabledReason="Pick a clip below to play it"
                                        onClick={() => seek(playhead.duration)}
                                    />
                                    <IconButton
                                        icon="nextClip"
                                        title="Next clip"
                                        disabled={orderedClips.length < 2}
                                        disabledReason="Only one clip"
                                        onClick={() => step(1)}
                                    />
                                    <IconButton
                                        icon="loop"
                                        className={animationLoop ? 'active' : undefined}
                                        pressed={animationLoop}
                                        title={animationLoop
                                            ? 'Looping. Press to play the clip once and hold '
                                                + 'its last frame.'
                                            : 'Playing once and holding the last frame. Press '
                                                + 'to loop.'}
                                        disabled={animation === null}
                                        disabledReason="Pick a clip below to play it"
                                        onClick={() => setAnimationLoop(current => !current)}
                                    />

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
                                    title="Drag to a frame"
                                    onPointerDown={() => { scrubbingRef.current = true; }}
                                    onPointerUp={() => { scrubbingRef.current = false; }}
                                    onChange={e => {
                                        const time = Number(e.target.value);
                                        viewportRef.current?.seekAnimation(time);
                                        setPlayhead(current => ({ ...current, time }));
                                    }}
                                />

                                <Field label="Speed" value={<>{animationSpeed.toFixed(2)}x</>}>
                                    <input
                                        type="range"
                                        disabled={animation === null}
                                        min={0}
                                        max={2}
                                        step={0.05}
                                        value={animationSpeed}
                                        title="Playback speed"
                                        onChange={e =>
                                            setAnimationSpeed(Number(e.target.value))}
                                    />
                                </Field>
                            </div>
                        </div>
                        )}

                    </>}
                />

                {/* What one row IS now opens as its own editor tab beside this one - see
                    `openInspector` and `modelInspectorPanel.ts`. It was a 320px flyout holding a
                    mesh's sections AND ten columns of vertex data, which is a page's worth of
                    reading in a fifth of the screen. The two flyouts below stayed: an ability and a
                    hardpoint card are a handful of rows, and they belong beside the thing that
                    named them. */}
                {/* What the FILE says about an ability, beside the card that names it. The card's
                    face carries what the GAME says; this is the other half. */}
                {infoAbility !== null && (
                    <div
                        className="details-flyout"
                        role="dialog"
                        ref={abilityInfoRef}
                        style={abilityInfoAt === null
                            ? { visibility: 'hidden' }
                            : { left: abilityInfoAt.left, top: abilityInfoAt.top }}
                    >
                        <div className="details-head">
                            <span className="details-title">
                                {abilities.find(a => a.type === infoAbility)?.label ?? infoAbility}
                            </span>
                            <IconButton
                                icon="close"
                                title="Close"
                                onClick={() => setInfoAbility(null)}
                            />
                        </div>
                        <div className="details-body">
                            <dl className="card-info">
                                {abilityFacts(
                                    abilities.find(a => a.type === infoAbility)!,
                                ).map(([label, value]) => (
                                    <Fragment key={label}>
                                        <dt>{label}</dt>
                                        <dd>{value}</dd>
                                    </Fragment>
                                ))}
                            </dl>
                        </div>
                    </div>
                )}

                {/* What a hardpoint measures. Its own flyout rather than an unfold inside the
                    card: an unfold pushes every card below it down the list, and this panel says
                    everything else of this shape - a tree row's details, the scene, the camera -
                    in a flyout. */}
                {infoCard !== null && (
                    <div
                        className="details-flyout"
                        role="dialog"
                        ref={cardInfoRef}
                        style={cardInfoAt === null
                            ? { visibility: 'hidden' }
                            : { left: cardInfoAt.left, top: cardInfoAt.top }}
                    >
                        <div className="details-head">
                            <span className="details-title">{infoCard}</span>
                            <IconButton
                                icon="close"
                                title="Close"
                                onClick={() => setInfoCard(null)}
                            />
                        </div>
                        <div className="details-body">
                            {(() => {
                                const card = cards.find(entry => entry.id === infoCard);

                                // A unit weapon's card carries the same button, and the same
                                // measurements sit behind it - it simply has no hardpoint half.
                                const loose = card === undefined
                                    ? looseWeapons.find(row => row.id === infoCard)
                                    : undefined;

                                const facts = card !== undefined
                                    ? hardpointFacts(card)
                                    : loose === undefined ? [] : unitWeaponFacts(loose);

                                const weapon = card?.weapon ?? loose ?? null;

                                if (card === undefined && loose === undefined) {
                                    return null;
                                }

                                return (
                                    <dl className="card-info">
                                        {facts.map(fact => (
                                            <dd key={fact}>{fact}</dd>
                                        ))}
                                        {weapon !== null
                                            && weaponFacts(weapon).map(([label, value]) => (
                                            <Fragment key={label}>
                                                <dt>{label}</dt>
                                                <dd>{value}</dd>
                                            </Fragment>
                                        ))}
                                    </dl>
                                );
                            })()}
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
