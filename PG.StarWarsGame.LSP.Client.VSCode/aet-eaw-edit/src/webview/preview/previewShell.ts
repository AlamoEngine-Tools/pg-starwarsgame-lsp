// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model preview's stylesheet: the shared dock chrome it composes, and the preview's own rules.
//
// Out of modelPreview.tsx so the component file is the component, the way the localisation grid keeps
// its own in locGridStyles.ts. The rules are unchanged by the move.

import styled from "styled-components";

import {
    dockBodyCss,
    dockChromeCss,
    dockHeaderCss,
    dockOverviewCss,
    playerCss,
    problemsPanelCss,
    rightDockCss,
    rotarySwitchCss,
    stageChromeCss,
    stageFlyoutCss,
} from "../shared/dockChrome";

export const Shell = styled.div`
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

    .header-modes {
        display: inline-flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-2);
    }

    /* The right edge as a column: the hand-off above the severity tag, the pair centred on the
       header's height. The tag carries header-right for every other dock, which positions it on its
       own; inside this column it is a plain item. */

    .header-corner {
        position: absolute;
        top: 0;
        bottom: 0;
        right: 8px;
        display: flex;
        flex-direction: column;
        align-items: flex-end;
        justify-content: center;
        gap: var(--space-2);
    }

    .header-corner .header-right {
        position: static;
    }

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
        border: var(--space-1) solid var(--colour-line);
        border-radius: var(--radius-3);
        background: var(--colour-panel);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.45);
    }

    /* The caret, which is what makes it read as coming FROM the button rather than as a panel that
       happens to be nearby. Two squares: the border first, then the fill a pixel over it. */

    .model-info::before,
    .model-info::after {
        content: "";
        position: absolute;
        left: 12px;
        width: 8px;
        height: 8px;
        transform: rotate(45deg);
    }

    .model-info::before {
        top: -5px;
        border: var(--space-1) solid var(--colour-line);
        background: var(--colour-line);
    }

    .model-info::after {
        top: -4px;
        background: var(--colour-panel);
    }

    .model-info-name {
        font-weight: 600;
        word-break: break-all;
        margin-bottom: var(--space-6);
    }

    /* Room to breathe. Every control in this section had been tuned down to 1-2px of padding to
           win back vertical space for the tree - which stopped being the trade the moment the tree got
           the top of the panel to itself. Squeezed rows are harder to hit and harder to scan, and the
           saving was a dozen pixels. */

    .dock-section {
        gap: var(--space-8);
    }

    /* The tree's search: a magnifier and, next to it, either the kind filters or the box.

       One element rather than two blocks with a gap. border-box on the input because the loose one
       was width 100% with its own padding on top, so it reached 10px PAST the section every other
       control in the panel lines up with - which is a good part of why it read as adrift. */
    /* A hardpoint card. A bordered block rather than a list row, because it carries four kinds of
       thing - a name, the game's own words, its measurements and its switches - and a flat row put
       them all on one line where none of them read. */

    .hardpoint-cards {
        display: flex;
        flex-direction: column;
        gap: var(--space-6);
        margin: 0 0 var(--space-8);
    }

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
        color: var(--colour-muted);
    }

    .card-group-title .section-count {
        margin-left: auto;
    }

    .card-group-title:hover {
        background: var(--colour-hover);
        opacity: 0.9;
    }

    /* The hardpoint list's search sits in the flow above the cards rather than floating on them:
       there is no list box here for it to float over, and the section already scrolls as one. */

    .hardpoint-search {
        position: static;
        margin: var(--space-2) 0 var(--space-6);
        padding: 0;
        background: none;
        border: none;
    }

    /* Pressed. The one control on the stage that FIRES rather than latching, so it says so for a
       moment and then goes quiet - red because that is what it just did to the model. */

    .icon-btn.as-action.fired {
        background: var(--colour-data-red);
        border-color: var(--colour-data-red);
        color: var(--colour-action-ink);
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
        font-family: var(--font-family-code);
        font-size: var(--font-size-smaller);
    }

    .damage-log li {
        padding: var(--space-2) var(--space-2);
        border-bottom: var(--space-1) solid var(--colour-line);
        /* Long names - a projectile, an armour column, a hardpoint id - must wrap rather than push
           a horizontal scrollbar under the whole flyout. */
        overflow-wrap: anywhere;
    }

    .damage-log li:last-child {
        border-bottom: none;
    }

    /* The pools, in one line above the shots that moved them. */

    .pool-summary {
        margin: 0 0 var(--space-4) 0;
        padding: 0 0 var(--space-4) 0;
        border-bottom: var(--space-1) solid var(--colour-line);
        font-family: var(--font-family-code);
        font-size: var(--font-size-smaller);
        font-variant-numeric: tabular-nums;
        white-space: pre-wrap;
        opacity: 0.85;
    }

    /* Given a height by the grip, the LIST is what should grow - not the empty space under it.
       Its own max-height was fixed at 220px, so dragging the flyout taller moved the Clear button
       down and nothing else. */

    .stage-flyout.sizeable .damage-log {
        flex: 1 1 auto;
        max-height: none;
        min-height: 0;
    }

    /* The workings, subordinate to the sentence they explain. Monospace because they are numbers
       lining up, and dimmed because the sentence is what a reader scans first. */

    .damage-log .log-workings {
        list-style: none;
        margin: var(--space-1) 0 0 0;
        padding: 0 0 0 var(--space-12);
        font-family: var(--font-family-code);
        font-size: 0.9em;
        opacity: 0.7;
        font-variant-numeric: tabular-nums;
    }

    .damage-log .log-workings li {
        padding: 0;
        border-bottom: none;
    }

    /* The shot that finished something, and the shot that did nothing: the two lines a reader is
       scanning for. Everything between them is ordinary and stays quiet. */

    .damage-log .log-kill {
        color: var(--colour-data-red);
    }

    .damage-log .log-nothing {
        opacity: 0.55;
    }

    .fire-target {
        font-family: var(--font-family-code);
        opacity: 0.85;
    }

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
        border: var(--space-1) solid var(--colour-line-strong);
        border-radius: var(--radius-6);
        cursor: pointer;
    }

    .hardpoint-card:hover {
        background: var(--colour-hover);
    }

    /* The selected card is the one the attacker is aimed at, which is the same state the Fire at
       list and the targeting marks show. One fact, three places, one highlight. */

    .hardpoint-card.selected {
        background: var(--colour-selected);
        border-color: var(--colour-focus);
    }

    /* Shot away. Dimmed rather than struck through or removed: it is still a card you can press to
       put back, and a row that vanishes when destroyed takes the way back with it. */

    .hardpoint-card.gone .part-name {
        opacity: 0.5;
        text-decoration: line-through;
    }

    /* A death clone card is a READING, not a control: the card itself does nothing and only the
       jump in its corner does. So it drops the hardpoint card's pointer and its hover, both of
       which promise a click that is not there. The SELECTED highlight stays - it says which clone
       the weapon you have built would actually produce, and that is the section's whole question.

       A unit weapon card is the same: pressing a hardpoint card aims the attacker at it, and a unit
       weapon is not a target, so there is nothing for the card body itself to do. Its buttons are
       still buttons. */

    .clone-card,
    .weapon-card {
        cursor: default;
    }

    .clone-card:not(.selected):hover,
    .weapon-card:hover {
        background: none;
    }

    /* No wreck at all: the object is not defined anywhere, or it declares no tactical model. The
       one thing worth saying on a clone card beyond the mapping itself. */

    .card-unresolved {
        color: var(--colour-warning);
    }

    /* Title line: the name, and the jump pushed to the far corner. */

    .card-head {
        display: flex;
        align-items: center;
        gap: var(--space-6);
    }

    .card-head .part-name {
        font-weight: 600;
    }

    .card-head .goto-definition,
    .card-head .icon-btn {
        margin-left: auto;
        flex-shrink: 0;
    }

    /* The Type, which is game data rather than prose - it picks the reticle and it is what a reader
       greps their own files for. */

    .card-type {
        font-family: var(--font-family-code);
        opacity: 0.75;
    }

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
        background: var(--colour-input);
    }

    .card-health-fill {
        display: block;
        height: 100%;
        transition: width 0.15s linear;
    }

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
        color: var(--colour-ink);
        text-shadow: 0 0 3px var(--colour-editor-ground);
    }

    /* One row per fire bone, full width: the slot on the left and the bone it names beside it. They
       were inline pills showing the bone alone, which says where a shot leaves from but not which
       of Fire_Bone_A and _B declared it. */

    .bone-picks {
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
    }

    /* Where the turret is pointed, with the way back to rest beside it. A readout rather than a
       control, so the numbers lead and the button sits at the end of the row. */

    .turret-aim {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        margin-top: var(--space-4);
    }

    .turret-aim .detail {
        flex: 1;
        font-variant-numeric: tabular-nums;
    }

    .muzzle-row {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        width: 100%;
        padding: var(--space-2) var(--space-6);
        border: var(--space-1) solid transparent;
        border-radius: var(--radius-3);
        background: var(--colour-action-2);
        color: var(--colour-ink);
        font-size: var(--font-size-smaller);
        cursor: pointer;
        text-align: left;
    }

    .muzzle-row:hover:not(:disabled) {
        background: var(--colour-hover);
    }

    .muzzle-row.selected {
        border-color: var(--colour-focus);
        background: var(--colour-selected);
    }

    .muzzle-row:disabled {
        opacity: 0.45;
        cursor: default;
    }

    .muzzle-slot {
        flex: 0 0 auto;
        opacity: 0.75;
    }

    .muzzle-bone {
        font-family: var(--font-family-code);
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

    .card-info dt {
        opacity: 0.65;
    }

    .card-info dd {
        margin: 0;
        font-variant-numeric: tabular-nums;
    }

    .card-info dd:only-child,
    .card-info dd:not(dt + dd) {
        grid-column: 1 / -1;
        opacity: 0.8;
    }

    /* Pushed to the far end: the three on its left DO something to the model, and this one only
       says what the file holds. */

    .card-actions .card-info-btn {
        margin-left: auto;
    }

    /* The gaps that group the card. Each of these starts a new group, so the space goes above it. */

    .hardpoint-card .card-health {
        margin-top: var(--space-8);
    }

    .hardpoint-card .bone-picks {
        margin-top: var(--space-8);
    }

    .card-actions {
        margin-top: var(--space-8);
    }

    .tree-pane {
        position: relative;
    }

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
        background: color-mix(in srgb, var(--colour-sidebar) 88%, transparent);
        border: var(--space-1) solid var(--colour-line-strong);
    }

    .tree-search.open {
        left: 6px;
    }

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
        color: var(--colour-input-ink);
        background: var(--colour-input);
        border: var(--space-1) solid var(--colour-input-line);
        border-radius: var(--radius-3);
        font-family: inherit;
        font-size: inherit;
    }

    /* Inset, so the ring is drawn ON the field rather than growing it - an outline that adds a
       pixel each side would nudge the icons every time the box took focus. */

    .tree-search .tree-filter:focus {
        outline: var(--space-1) solid var(--colour-focus);
        outline-offset: -1px;
    }

    .tree-search .tree-filter::placeholder {
        color: var(--colour-placeholder);
    }

    /* The one button that never moves. Everything else in this row is what it opens onto, so it
       keeps its width whichever of the two is showing. */

    .tree-search .tree-search-toggle {
        flex: 0 0 auto;
    }

    .tree-search-icons {
        display: flex;
        align-items: center;
        gap: var(--space-4);
        flex: 0 0 auto;
    }

    /* The empty message keeps the list's own shape rather than the row's - no hover, no pointer,
       and it must not look like something you can click. */

    .bone-tree .tree-empty {
        list-style: none;
        cursor: default;
    }

    /* Controls that describe the VIEW rather than the subject, docked to the stage's own corners.
    
           They belonged to nothing in the right-hand dock, which describes the MODEL, and between them
           they filled it: the camera, five overlay pills, a faction dropdown with a colour well under
           it and a renderer switch left no room for the one thing the foot is for, which is the
           playback bar. On the stage each cluster sits next to what it acts on, and the dock is free.
    
           The plate itself is shared - see stageChromeCss. */

    ${stageChromeCss}
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

    .stage-row > * {
        pointer-events: auto;
    }

    .stage-row .stage-chrome {
        position: static;
    }

    .stage-top {
        top: 8px;
        align-items: flex-start;
    }

    .stage-bottom {
        bottom: 8px;
        align-items: flex-end;
    }

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
        .stage-bottom {
            flex-direction: column-reverse;
        }
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
        color: var(--colour-muted);
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

    .stage-slot-end {
        justify-content: flex-end;
    }

    /* The middle slot centres its contents; the two side slots being EQUAL is what centres the
       slot itself. */

    .stage-slot-mid {
        justify-content: center;
    }

    .stage-bottom .stage-slot {
        align-items: flex-end;
    }

    /* Nothing in a side slot shrinks. Letting the renderer switch compress turned "Default | Game"
       into "Dif..|Ga..." - a control you can neither read nor aim at - while the palette in the
       middle wraps to another row perfectly well and stays centred doing it. */

    .stage-slot .stage-chrome {
        flex: 0 0 auto;
    }

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

    .stage-chrome .mode-selector {
        flex-shrink: 0;
        width: auto;
    }

    .stage-chrome .mode-selector button {
        flex: 0 0 auto;
        min-width: max-content;
        overflow: visible;
        text-overflow: clip;
        white-space: nowrap;
    }

    .faction-palette {
        min-width: 0;
        justify-content: center;
    }

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

    .stage-field > span {
        opacity: 0.7;
    }

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

    .stage-field.detail .stage-value {
        min-width: 165px;
    }

    /* Short enough that two of them plus the renderer fit the left slot on a docked panel. */

    .stage-field input[type="range"] {
        width: 84px;
    }

    /* The tier count sits inline after its word, so it is sized to the two digits it holds rather
       than stretching like the fields above it. */

    .stage-flyout .tier-count {
        width: 52px;
    }

    .stage-chrome select {
        height: 22px;
        font-size: 1em;
        color: var(--colour-dropdown-ink);
        background: var(--colour-dropdown);
        border: var(--space-1) solid var(--colour-dropdown-line);
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

    .ability-bar > .icon-btn {
        margin-left: auto;
    }

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

    .status-bar {
        display: block;
        width: 100%;
    }

    .status-bar-track {
        display: block;
        width: 100%;
        height: 5px;
        border-radius: var(--radius-3);
        overflow: hidden;
        background: var(--colour-panel);
    }

    /* No transition on the width. A shot empties a pool in one step and the engine's own bars do
       not slide; animating it would show a number the simulation never held. */

    .status-bar-fill {
        display: block;
        height: 100%;
    }

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
        background: var(--colour-action-2);
        color: var(--colour-action-2-ink);
        cursor: pointer;
        overflow: hidden;
    }

    .stage-chrome .ability-key:hover:not(:disabled) {
        border-color: var(--colour-focus);
    }

    /* Active reads as PRESSED IN, not merely highlighted - it is a state you leave running while
       you look at what it did, so it has to survive the pointer moving away. */

    .stage-chrome .ability-key.active {
        background: var(--colour-action);
        color: var(--colour-action-ink);
        border-color: var(--colour-focus);
    }

    /* An order - it drives nothing on this model. Dimmed rather than absent, and its tooltip says
       why: what a unit CAN do is worth reading even where the answer is "nothing you can see". */

    .stage-chrome .ability-key:disabled {
        opacity: 0.4;
        cursor: default;
    }

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

    /* Icon-only plates are square-buttoned by the shared stageChromeCss. */

    /* ONE height for everything on the stage, set here rather than left to each control's
       contents. A glyph and a word are different heights, and the plates ended up stepped: the
       scene globe stood taller than the preset words beside it and the camera button taller again.
       The box is the same and what sits in it is centred. */

    .stage-chrome .icon-btn,
    .stage-chrome .swatch,
    .stage-chrome .mode-selector button {
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

    .stage-chrome .icon-btn,
    .stage-chrome .swatch {
        border-radius: var(--radius-6);
    }

    .stage-chrome .mode-selector button {
        border-radius: 0;
    }

    .stage-chrome .mode-selector button:first-child {
        border-radius: var(--radius-3) 0 0 var(--radius-3);
    }

    .stage-chrome .mode-selector button:last-child {
        border-radius: 0 var(--radius-3) var(--radius-3) 0;
    }

    /* An icon is an SVG here, sized by the component. The stage's own buttons carry nothing else,
       so this only has to stop a glyph stretching the 22px box it sits in. */

    .stage-chrome .icon-btn svg {
        flex-shrink: 0;
    }

    /* The colour IS the control, so the swatch carries no label - the faction's name is the title
       and reaches both a hover and a screen reader. Square and generous enough to judge a colour
       by, which a thin strip is not. */

    .faction-palette .swatch {
        width: 22px;
        height: 22px;
        padding: 0;
        border: var(--space-1) solid var(--colour-line-strong);
        border-radius: var(--radius-3);
        cursor: pointer;
        background-clip: padding-box;
    }

    /* The ring goes OUTSIDE the swatch. A border would eat into the colour being judged, and on a
       dark faction it is the only thing that says which one is applied. */

    .faction-palette .swatch.active {
        outline: 2px solid var(--colour-focus);
        outline-offset: 1px;
    }

    .faction-palette .swatch-plain {
        display: flex;
        align-items: center;
        justify-content: center;
        color: var(--colour-muted);
        background: transparent;
    }

    /* Always last, whatever the roster does - see the markup. A divider says it is not a faction. */

    .faction-palette .swatch-custom {
        margin-left: var(--space-4);
        border-left: var(--space-1) solid var(--colour-line);
        padding-left: var(--space-6);
        width: 28px;
        box-sizing: content-box;
    }

    /* The scene sits at the left of the stage and the camera at the right, mirroring the buttons
       that open them. The flyout itself is shared - see stageFlyoutCss. */

    ${stageFlyoutCss}
    /* Everything below is the flyout being LESS CRAMPED, and every rule is scoped to it on purpose.
           The .field and .dock-section rules come from shared/dockChrome.ts, which the story
           graph, the localisation grid and the encyclopedia also wear - loosening them there is a
           different decision from loosening this dialog, and not one this asked for.
    
           What was measured on the weapon flyout before this: sections 8px apart, a label 5px from its
           control, checkbox rows 4px apart, and inputs on the browser's own padding - so the fields ran
           together into one dense stack, and the projectile list drew its rows with no padding at all. */
        /* A section is a GROUP. At 8px its fields butted into each other and the heading read as one
           more row. */
    .stage-flyout .dock-section {
        gap: var(--space-12);
    }

    .stage-flyout .field {
        gap: var(--space-6);
    }

    /* A row of switches needs more between them than a flex gap of 4, which put "Shield" and "Hull"
       close enough to read as one control with two boxes. */

    .stage-flyout .view-row {
        gap: var(--space-8);
    }

    /* A note is prose and is the one thing here that wraps, so it gets the leading to match. */

    .stage-flyout .field-note {
        line-height: 1.45;
    }

    /* The controls themselves. Nothing gave these any padding, so a number sat hard against the
       left edge of its box. Not applied to the stage plates - the stage-chrome select is a 22px
       control on the stage edge and sets its own. */

    .stage-flyout input[type="text"],
    .stage-flyout input[type="number"],
    .stage-flyout select {
        padding: var(--space-4) var(--space-8);
    }

    /* A list box, where the padding has to go on the ROWS - padding on the select itself indents
       the frame and leaves the rows as tight as they were. This is the control the report was
       most obviously about. */

    .stage-flyout select[size] {
        padding: var(--space-2);
    }

    .stage-flyout select[size] option {
        padding: var(--space-4) var(--space-6);
        border-radius: var(--radius-3);
    }

    .body {
        flex: 1;
        display: flex;
        min-height: 0;
    }

    /* min-height: 0 is load-bearing - without it the canvas refuses to shrink below its content and
       shoves the problems bar off the bottom of the panel. */

    .stage-column {
        flex: 1;
        display: flex;
        flex-direction: column;
        min-width: 0;
        min-height: 0;
    }

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

    /* The capture frame: its host is a size container over the canvas, so the frame can be the
       largest box of the capture's shape that fits - full height or full width - in plain CSS. The
       shadow dims everything outside it; the host clips the shadow to the viewport. Under the stage
       chrome, and never under the pointer. */

    .capture-frame-host {
        position: absolute;
        inset: 0;
        container-type: size;
        display: grid;
        place-items: center;
        overflow: hidden;
        pointer-events: none;
        z-index: 1;
    }

    .capture-frame {
        position: relative;
        width: min(100cqw, calc(100cqh * var(--capture-ratio)));
        aspect-ratio: var(--capture-ratio);
        box-shadow: 0 0 0 100vmax rgba(0, 0, 0, 0.45);
        outline: 1px dashed var(--colour-focus);
    }

    .capture-frame-size {
        position: absolute;
        top: var(--space-4);
        left: var(--space-4);
        padding: 0 var(--space-4);
        font-size: var(--font-size-smaller);
        font-variant-numeric: tabular-nums;
        color: var(--colour-ink);
        background: color-mix(in srgb, var(--colour-panel) 82%, transparent);
        border-radius: var(--radius-3);
    }

    .capture-custom .integer-field {
        flex: 1 1 0;
        min-width: 0;
    }

    /* One line per preset whatever its name: the name gives way, the row's actions never wrap. */

    .preset-list .view-row {
        flex-wrap: nowrap;
    }

    .preset-name,
    .preset-apply {
        flex: 1 1 0;
        min-width: 0;
    }

    /* The button is a flex row, so the ellipsis belongs to the name inside it. */

    .preset-apply {
        justify-content: flex-start;
    }

    .preset-apply-name {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
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
        background: var(--colour-editor-ground);
        color: var(--colour-muted);
        font-size: var(--font-size-12);
    }

    .load-spinner {
        width: 22px;
        height: 22px;
        border-radius: 50%;
        border: 2px solid var(--colour-line-strong);
        border-top-color: var(--colour-progress);
        animation: load-spin 0.9s linear infinite;
    }

    /* A reader who has asked for no motion gets none: the ring holds still and the counts
       underneath are what says the load is moving. */
    @media (prefers-reduced-motion: reduce) {
        .load-spinner {
            animation: none;
        }
    }

    @keyframes load-spin {
        to {
            transform: rotate(360deg);
        }
    }

    .load-label {
        letter-spacing: 0.02em;
    }

    .load-detail {
        font-variant-numeric: tabular-nums;
        opacity: 0.7;
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
           an unlit hull on a dark theme is invisible. Deliberately NOT a themed colour - see the
           token's own note in tokens.ts. */
        background: var(--colour-scene-ground);
    }

    /* Labels are DOM, not sprites: they stay crisp at any zoom, theme themselves, and can be read by
       a screen reader. pointer-events off, or they would swallow the drags that orbit the camera. */

    .bone-labels {
        position: absolute;
        inset: 0;
        overflow: hidden;
        pointer-events: none;
    }

    .bone-label {
        position: absolute;
        top: 0;
        left: 0;
        margin: -9px 0 0 var(--space-8);
        padding: 0 var(--space-2);
        font-size: var(--font-size-10);
        white-space: nowrap;
        color: var(--colour-editor-ink);
        background: color-mix(
            in srgb,
            var(--colour-editor-ground) 70%,
            transparent
        );
        border-radius: var(--radius-3);
    }

    .bone-label.selected {
        color: var(--colour-editor-ground);
        background: var(--colour-focus);
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

    .right-dock {
        min-width: 0;
    }

    .dock-content {
        min-width: 0;
    }

    /* Toggle buttons in the icon-btn / .active language the localisation search and the story
       graph's lane switches already use, rather than a checkbox row of their own invention.
       No backticks in here: this is a styled-components template literal.

       .kind-filter and .selection-actions were the two blocks this pass folded away - the kinds
       into the search element, Reset into the section title - so only the overlay strip is left. */

    .view-toggles {
        display: flex;
        gap: var(--space-4);
        flex-wrap: wrap;
    }

    .tree-search .icon-btn,
    .view-toggles .icon-btn {
        font-size: var(--font-size-smaller);
        padding: var(--space-2) var(--space-8);
        border-radius: var(--radius-6);
    }

    /* A row's twisty, kind picture and name are dockChromeCss's .tree-row, shared with the Mod
       Browser; what follows is this tree's own - its list, its selection, its gating, its eye. */

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
        cursor: pointer;
    }

    .bone-row.selected {
        background: var(--colour-selected);
    }

    .bone-row.hidden-bone .tree-name {
        opacity: 0.55;
        font-style: italic;
    }

    /* A row the filter kept only to reach a match below it. The WHOLE row recedes - name, kind
       icon and controls together - because the complaint was about weight, not about text: an
       ancestor bone drew its icon in full colour beside the greyed, italic effect rows that were
       actually being looked for, so the scaffolding was the loudest thing in a filtered tree.

       Recessive, never removed. These rows are the only thing holding the hierarchy together, and
       they are still real rows worth hiding or inspecting - they just are not the answer. Hover
       brings the row back to full weight, so acting on one is never a fight. */

    .bone-row.scaffold {
        opacity: 0.42;
    }

    .bone-row.scaffold:hover,
    .bone-row.scaffold.selected,
    .bone-row.scaffold:focus-within {
        opacity: 1;
    }

    .bone-row .bone-attach {
        color: var(--colour-muted);
        font-size: var(--font-size-smaller);
    }

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

    .bone-row .row-eye:hover {
        opacity: 1;
    }

    /* An open eye is the ordinary state and stays quiet. A CLOSED one is the reader's own doing
       nine times in ten, so it keeps full weight - a hidden row you cannot find the eye on is the
       tick that "did nothing" all over again. */

    .bone-row .row-eye.eye-shown {
        opacity: 0.7;
    }

    .bone-row:hover .row-eye.eye-shown {
        opacity: 1;
    }

    /* Inherited: dimmed and inert, the way Blender greys an eye whose collection is hidden. The
       title says which ancestor, because a dead control that cannot explain itself is worse than
       no control. */

    .bone-row .row-eye.eye-inherited {
        opacity: 0.4;
    }

    .bone-row .row-eye.eye-inherited:disabled {
        cursor: default;
    }

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
    .bone-row .row-details.active {
        opacity: 1;
    }

    /* Pinned against the WINDOW, because the row it belongs to lives inside a scroller that would
       clip it. placeInfo and placeCardInfo supply the corner; everything here is the box itself. */

    .details-flyout {
        position: fixed;
        z-index: 6;
        width: 320px;
        max-height: 62vh;
        display: flex;
        flex-direction: column;
        border: var(--space-1) solid var(--colour-line-strong);
        border-radius: var(--radius-6);
        background: var(--colour-panel);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.45);
    }

    .details-head {
        display: flex;
        align-items: baseline;
        gap: var(--space-6);
        padding: var(--space-4) var(--space-4) var(--space-4) var(--space-8);
        border-bottom: var(--space-1) solid var(--colour-line);
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
        color: var(--colour-muted);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .details-head .icon-btn {
        margin-left: auto;
        align-self: center;
    }

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

    /* The band and its prose rows are problemsPanelCss's .problems-band and .prose-problems: this
       editor's findings are prose, not the short entry labels the loc grids report. */

    /* A row's diagnostic id is problemsPanelCss's .problem-id. */

    /* Which hardpoint, when the sentence does not already say so - see problemWhere. */

    .problem-where {
        flex-shrink: 0;
        padding: 0 var(--space-4);
        border-radius: var(--radius-3);
        font-family: var(--font-family-code);
        font-size: var(--font-size-smaller);
        background: var(--colour-badge);
        color: var(--colour-badge-ink);
    }

    /* A readout, not a menu: no hover state and no pointer, because nothing here is clickable yet.
       A row that looks pressable is a promise of interaction. */

    .part-list {
        list-style: none;
        margin: 0;
        padding: 0;
    }

    .part-list li {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
        padding: var(--space-2) var(--space-6);
        min-width: 0;
        cursor: default;
    }

    .part-list li .field-label {
        min-width: 0;
        align-items: flex-start;
    }

    .part-list li .field-label input[type="checkbox"] {
        margin-top: var(--space-2);
        flex-shrink: 0;
    }

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
        color: var(--colour-muted);
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

    .ability-row-head {
        display: flex;
        align-items: flex-start;
        gap: var(--space-6);
        min-width: 0;
    }

    .ability-row-head .field-label,
    .ability-row-head .part-name {
        flex: 1;
    }

    .ability-icon {
        width: 26px;
        height: 26px;
        flex-shrink: 0;
        image-rendering: pixelated;
        display: block;
    }

    /* The tooltip text the game shows. Wraps - it is a sentence, and clipping it to the dock's
       width would end most of them mid-word. */

    .part-list li .ability-description {
        font-style: italic;
    }

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
        border-top: var(--space-1) solid var(--colour-line-strong);
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
        color: var(--colour-heading);
        cursor: pointer;
    }

    .part-list li .goto-definition:hover:not(:disabled) {
        text-decoration: underline;
    }

    .part-list li .goto-definition:disabled {
        color: var(--colour-faint);
        cursor: default;
    }

    /* Full width now, one row per fire bone - the 21px indent it carried lined it up with a jump
       that no longer sits under the name. */

    .part-list li .bone-picks {
        margin-top: var(--space-2);
    }

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

    .dock-section-title .header-right:disabled {
        opacity: 0.35;
    }

    .section-count.as-button {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        border: var(--space-1) solid var(--colour-line-strong);
        border-radius: var(--radius-6);
        padding: 0 var(--space-4);
        background: none;
        color: inherit;
        font: inherit;
        cursor: pointer;
    }

    .section-count.as-button:hover {
        background: var(--colour-hover);
    }

    /* A picked bone is boxed on the model, so its button carries the same weight as the active
       segment of a mode selector - it is a current state, not an action waiting to be taken. */

    .btn.selected {
        background: var(--colour-action);
        color: var(--colour-action-ink);
        border-color: var(--colour-action);
    }

    .stat-row {
        display: flex;
        justify-content: space-between;
        gap: var(--space-12);
        padding: var(--space-1) var(--space-6);
        font-variant-numeric: tabular-nums;
    }

    .stat-row .value {
        color: var(--colour-muted);
    }

    .view-row {
        display: flex;
        align-items: center;
        gap: var(--space-4);
        flex-wrap: wrap;
    }

    /* The transport itself is shared chrome (playerCss); the story graph's tick transport is the
       same row. Only the preview's own field sliders are pinned here.
       (No backticks in here: this is a styled-components template literal.) */

    ${playerCss}
    .field input[type=range] {
        direction: ltr;
    }

    /* No padding of its own now that it lives in the foot - the foot lays its children out with a
       gap. The old top padding was there to hold it off the tree it used to sit under. */

    .anim-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
    }

    /* A family, folded or open. The heading is the whole hit area, not just the chevron - a 13px
       target beside a word that obviously names the thing being folded is a target people miss. */

    .anim-family {
        gap: var(--space-4);
        margin-bottom: var(--space-8);
    }

    .anim-family:last-child {
        margin-bottom: 0;
    }

    .dock-section-title.as-fold {
        cursor: pointer;
        user-select: none;
    }

    .dock-section-title.as-fold:hover {
        color: var(--colour-ink);
    }

    /* An ACTION, as a full-width card down a list.

       It was a tile on the dock's shared grid. The user's verdict: the idea "was good on paper, in
       reality we have a lot of animations where that works and some where the number of alternate
       indexes escalates so drastically that it is not usable that way". A 128px tile held about
       four take chips and put the rest behind a sideways scrollbar - which is precisely the case
       worth looking at. Full width, and the chips WRAP.

       The shape is the user's: play state left and vertically centred, name on the top row left
       aligned, takes on the bottom row left aligned and filling right. */

    .anim-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
    }

    .anim-card {
        display: flex;
        align-items: center;
        gap: var(--space-8);
        padding: var(--space-8) var(--space-8);
        border: var(--space-1) solid var(--colour-line-strong);
        border-radius: var(--radius-6);
        background: var(--colour-panel);
    }

    .anim-card:hover {
        background: var(--colour-hover);
    }

    .anim-card.active {
        border-color: var(--colour-action);
        background: color-mix(in srgb, var(--colour-action) 22%, transparent);
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

    .anim-play:hover {
        background: var(--colour-toolbar-hover);
    }

    .anim-play svg {
        flex-shrink: 0;
        opacity: 0.85;
    }

    /* min-width 0, or a long action name refuses to wrap and pushes the card wider than the dock. */

    .anim-card-body {
        display: flex;
        flex: 1 1 auto;
        flex-direction: column;
        gap: var(--space-2);
        min-width: 0;
    }

    .anim-name {
        text-align: left;
        overflow-wrap: anywhere;
    }

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
        color: var(--colour-muted);
        font-family: var(--font-family-code);
        font-size: var(--font-size-smallest);
        cursor: pointer;
    }

    .anim-take:hover {
        background: var(--colour-hover);
    }

    .anim-take.active {
        background: var(--colour-action);
        color: var(--colour-action-ink);
    }

    .anim-row {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        width: 100%;
        padding: var(--space-4) var(--space-8);
        border: var(--space-1) solid transparent;
        border-radius: var(--radius-3);
        background: var(--colour-panel);
        color: var(--colour-ink);
        font-family: var(--font-family-ui);
        font-size: var(--font-size-body);
        text-align: left;
        cursor: pointer;
    }

    .anim-row:hover {
        background: var(--colour-toolbar-hover);
        border-color: var(--colour-focus);
    }

    .anim-row.active {
        background: var(--colour-action);
        color: var(--colour-action-ink);
    }

    .anim-row svg {
        flex-shrink: 0;
        opacity: 0.8;
    }

    .anim-row .anim-name {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
`;
