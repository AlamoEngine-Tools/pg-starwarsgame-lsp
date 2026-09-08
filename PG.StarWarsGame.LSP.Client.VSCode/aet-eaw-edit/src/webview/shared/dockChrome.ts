// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The chrome every dock in this extension is built from: titled sections, tiles, the search block,
// buttons and dialogs.
//
// One definition, interpolated into each editor's stylesheet, because these editors are meant to
// read as one application and they had already drifted apart twice: the credits step library and
// the file actions were two tile shapes in one dock, and `.tile-grid` meant one thing in the
// localisation editors and another in the story graph. Sharing the class names without sharing the
// rules is what allowed that - the names matched while the pixels did not.
//
// Colours stay out of here on purpose. Each editor tints its own tiles (the graph colours them by
// type family), and only the geometry has to agree for the two to look like the same product.
//
// The token layer is interpolated at the top of this block rather than imported by each panel
// separately: all five consumers already interpolate this stylesheet, so carrying the scale here
// gets it to every one of them without touching a single call site. The tile properties that used
// to be declared here in full now live in tokens.ts with the reasoning that fitted them.

import { tokensCss } from './tokens';

export const dockChromeCss = `
${tokensCss}

    /* ── Sections ──────────────────────────────────────────────────────── */
    .dock-section { display: flex; flex-direction: column; gap: var(--space-6); margin-bottom: var(--space-12); }
    .dock-section:last-child { margin-bottom: 0; }
    /* No opacity. It multiplies with everything inside, and the heading colour already carries
       alpha of its own in some shipped themes - a count at 0.75 inside a heading at 0.65 measured
       2.53:1 against the dock, which is what the reader saw as "nigh invisible with a default
       visual studio dark skin". Uppercase, 11px and 600 is what makes this read as a heading; the
       colour finishes it. The opacity only made it unreadable.

       One heading for every dock section, foldable or not. There were two of these - this one
       muted, and .panel-section's in the link colour - for the same thing. */
    .dock-section-title {
        font-size: var(--font-size-11);
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--colour-heading);
        display: flex;
        align-items: center;
        gap: var(--space-4);
    }

    /* A foldable section's heading IS the button - the whole width of it, not just the chevron: a
       13px target beside a word that plainly names the thing is a target people miss. Transparent
       and borderless, because it is a heading that happens to be pressable rather than a button
       with a title in it. */
    button.dock-section-title {
        width: 100%;
        padding: 0;
        border: none;
        background: transparent;
        text-align: left;
        cursor: pointer;
    }
    button.dock-section-title:hover { color: var(--colour-heading-hover); }
    .dock-section-name { flex: 1; min-width: 0; }

    .dock-section-body {
        display: flex;
        flex-direction: column;
        gap: var(--space-12);
        /* Indented under its heading, so a fold reads as a group closing rather than as controls
           disappearing from a flat list. */
        padding-left: var(--space-2);
    }

    /* A VALUE, not a label - it is the one thing in the heading someone reads a number off, so it
       takes the ordinary foreground rather than the heading's muted one. */
    .dock-section-title .section-count {
        margin-left: auto;
        font-weight: normal;
        color: var(--vscode-foreground, #ccc);
    }

    /* The right-hand end of a heading: chips, the count, and the section's own button, packed
       against the edge in a fixed order.

       ONE auto margin, and it lives here. Two of them - the count's and a chip's - split the free
       space between them, so a chip appearing shoved the count 88 pixels to the left and the thing
       the reader was looking at moved out from under their eye. Wrapped, the group grows leftwards
       and everything already in it stays exactly where it was. */
    .dock-section-title .title-end {
        margin-left: auto;
        display: inline-flex;
        align-items: center;
        gap: var(--space-4);
    }
    .dock-section-title .title-end .section-count { margin-left: 0; }

    /* ── Tiles ─────────────────────────────────────────────────────────── */
    /* Fixed size, not fluid. A grid that resizes its tiles as the dock is dragged means every tile
       moves and re-wraps continuously, which is noise; a fixed tile that stays put and gains a
       third column at one known width is calmer to use and to scan. The width is half the dock's
       default content width, so two fit without a gap at the size the dock opens at. */
    .tile-grid {
        display: grid;
        /* auto-FILL, not auto-fit: every group keeps the same column count whether or not it has a
           tile for each, so a three-tile group and a two-tile group below it line up. (auto-fit
           collapses the empty track, which re-centres each group independently and leaves the
           sections visibly out of step.) The whole track block is then centred, so leftover width
           sits evenly either side instead of all against the left edge. The column count, and the
           gaps in it, are worked out from the dock's own width. */
        grid-template-columns: repeat(auto-fill, var(--dock-tile-w));
        grid-auto-rows: var(--dock-tile-h);
        justify-content: center;
        gap: var(--dock-tile-gap);
    }

    .dock-tile {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--space-4);
        padding: var(--space-8) var(--space-6);
        border-radius: var(--radius-6);
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorWidget-background, rgba(128, 128, 128, 0.08));
        color: var(--vscode-foreground, #ccc);
        font-family: var(--vscode-font-family, sans-serif);
        font-size: var(--font-size-body);
        cursor: pointer;
        text-align: center;
        min-width: 0;
        overflow: hidden;
    }
    .dock-tile:hover {
        background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.2));
        border-color: var(--vscode-focusBorder, #007fd4);
    }
    .dock-tile:focus-visible { outline: var(--space-1) solid var(--vscode-focusBorder, #007fd4); }
    .dock-tile[draggable="true"] { cursor: grab; }
    .dock-tile[draggable="true"]:active { cursor: grabbing; }
    .dock-tile .codicon, .dock-tile .tile-glyph { font-size: var(--icon-size-18); line-height: 1; opacity: 0.85; }
    /* A tile carrying a badge has three stacked things in it, so its label gets one line; without
       one it can have two. Either way the tile is the same height and nothing is cut off. */
    .dock-tile.with-badge .tile-label { -webkit-line-clamp: 1; }
    .dock-tile .tile-label {
        max-width: 100%;
        /* Wrap rather than truncate, but the tile has a fixed height now, so a very long type name
           is clamped to two lines and reads in full from the tooltip every tile already carries. */
        white-space: normal;
        overflow-wrap: anywhere;
        word-break: break-word;
        line-height: 1.2;
        display: -webkit-box;
        -webkit-line-clamp: 2;
        -webkit-box-orient: vertical;
        overflow: hidden;
    }
    /* The raw token a tile stands for, in the monospace the file itself would show it in. */
    .dock-tile .tile-badge {
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: var(--font-size-smallest);
        padding: 0 var(--space-4);
        border: var(--space-1) solid var(--vscode-panel-border, #444);
        border-radius: var(--radius-3);
        opacity: 0.7;
        max-width: 100%;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    /* ── Search ────────────────────────────────────────────────────────── */
    /* The mode toggles sit inside the box, the way an editor's find widget puts them - that buys
       back a whole row, which is what made this corner feel cramped. */
    .dock-search { display: flex; flex-direction: column; gap: var(--space-6); }
    .search-field { position: relative; display: flex; align-items: center; }
    .search-field input[type=text] { width: 100%; padding: var(--space-4) var(--space-8); }
    /* Room for the three toggles, which overlay the trailing edge. Only where they exist - the
       graph editor's filters have no modes and must not carry a gap for buttons it never draws. */
    .search-field.with-modes input[type=text] { padding-right: var(--search-modes-inset); }
    .search-field .mode-group { position: absolute; right: 3px; display: flex; gap: var(--space-1); }
    /* The three modes are a one-of-N choice and say so - but inside the box they are a compact
       cluster over the trailing edge, not the full-width joined row a settings pane gets. So the
       group keeps its own width and its segments keep the size they had as separate toggles. */
    .search-field .mode-group .mode-selector { width: auto; }
    .search-field .mode-group .mode-selector button {
        flex: 0 0 auto;
        min-width: 22px;
        padding: var(--space-2) var(--space-4);
    }
    .search-field .mode-group .codicon { font-size: var(--icon-size-14); }

    /* A control and its label are a pair and sit close; the gap BETWEEN fields is what the
       section body sets, and it is larger. Three pixels here had the label almost touching the
       control above it, so a run of settings read as one block of text with sliders in it. */
    .field { display: flex; flex-direction: column; gap: var(--space-4); }
    .field select, .field input[type=text] { width: 100%; }
    /* Room to actually grab. A range input defaults to a track a few pixels tall inside a box the
       browser sizes to the thumb, which in a settings panel reads as a hairline. */
    .field input[type=range] { width: 100%; height: 18px; margin: 0; }
    .field .btn { width: 100%; padding: var(--space-4) var(--space-12); }
    /* A row, so a label can carry a checkbox on its left and a readout on its right. Laid out the
       way a section title is: the count goes to the far edge. Without that they simply butted up
       against the label - "Wind speed1.0", "Light around45 deg" - which read as a missing space. */
    .field-label { display: flex; align-items: center; gap: var(--space-4); font-size: var(--font-size-smaller); opacity: 0.7; }

    /* The mark on a label that has more to say. Sized and coloured like the text around it until
       it has a warning to carry, so a panel with nothing wrong on it stays quiet. */
    .info-badge {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 15px;
        height: 15px;
        padding: 0;
        flex-shrink: 0;
        border: none;
        border-radius: var(--radius-round);
        background: transparent;
        color: inherit;
        opacity: 0.6;
        cursor: pointer;
    }
    .info-badge:hover, .info-badge:focus-visible {
        opacity: 1;
        background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.2));
    }
    .info-badge svg { flex-shrink: 0; }
    .info-badge.sev-warning { color: var(--vscode-editorWarning-foreground, #cca700); opacity: 1; }
    .info-badge.sev-error { color: var(--vscode-editorError-foreground, #f14c4c); opacity: 1; }

    /* The BUBBLE's own styles are not here. This block is interpolated into a styled.div, so
       everything in it is scoped to that element - and the bubble is portalled to the document
       body, outside it. It carries its own global styles instead; see InfoBadge.tsx. */

    /* A slider that has one position worth returning to, marked at the middle of its track.
       Drawn OVER the input rather than under it, because a range input paints its own track and
       would bury the mark; the thumb covers it when it is parked there, which is the one moment
       nobody needs to see it. */
    .detent-track { position: relative; display: block; }
    .detent-track > input[type=range] { width: 100%; }
    .detent-track::after {
        content: '';
        position: absolute;
        left: 50%;
        top: 50%;
        width: 2px;
        height: 9px;
        transform: translate(-50%, -50%);
        border-radius: var(--radius-pill);
        background: var(--vscode-descriptionForeground, #999);
        opacity: 0.55;
        pointer-events: none;
    }

    /* The .panel-section family lived here: a second chrome for a dock section, differing from
       .dock-section on gap, on body spacing and on which colour role the heading took. Both meant
       the same thing, so they are one now - see .dock-section above and DockSection.tsx. */
    .field-label .section-count { margin-left: auto; opacity: 0.85; }
    /* The sentence under a control that says what it does. It is support, not content, so it is
       quieter and tighter than the setting it explains. */
    .field-note {
        font-size: var(--font-size-smaller);
        line-height: 1.35;
        color: var(--vscode-descriptionForeground, #999);
    }
    /* The same sentence, when it is telling the reader something is off rather than explaining
       the control. Warning colour and no other change: it has to read as the same kind of text in
       the same place, or it becomes an alert the eye has to deal with on every glance. */
    .field-warn {
        font-size: var(--font-size-smaller);
        line-height: 1.35;
        color: var(--vscode-editorWarning-foreground, #cca700);
    }
    /* A colour well, not a form control. The browser renders a colour input as a wide bordered
       button with a swatch inside it, which took a whole row for one colour.
       No backticks in this file: it is one big template literal.

       NEVER put one inside a .field-label. The label carries opacity 0.7, and opacity composites
       the WHOLE SUBTREE as a group - a swatch cannot opt back out with an opacity of its own, so it
       renders a colour that is not the one stored. Measured: a stored #ffffff drew as #bdbdbd. Put
       the label and the well side by side in a .view-row instead. */
    .field input[type=color], .field-label input[type=color] {
        margin-left: auto;
        width: 28px;
        height: 20px;
        padding: 0;
        flex-shrink: 0;
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: var(--radius-3);
        background: none;
        cursor: pointer;
    }
    .field input[type=range] { flex: 1; min-width: 0; }
    .check-field {
        display: flex;
        align-items: flex-start;
        gap: var(--space-6);
        cursor: pointer;
        line-height: 1.35;
    }
    .check-field input[type=checkbox] { margin-top: var(--space-2); flex-shrink: 0; }
    .check-detail { opacity: 0.7; }

    /* A one-of-N choice as joined buttons: the alternatives stay visible, and switching is one
       press rather than open-then-pick. Segments share their borders, so the group reads as one
       control rather than as a row of separate buttons. */
    .mode-selector { display: inline-flex; width: 100%; }
    .mode-selector button {
        flex: 1;
        min-width: 0;
        padding: var(--space-2) var(--space-6);
        border: var(--space-1) solid var(--vscode-panel-border, #444);
        border-right-width: 0;
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.18));
        color: var(--vscode-foreground);
        font-size: var(--font-size-smaller);
        line-height: 1.5;
        cursor: pointer;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    /* A segment that cannot be chosen. It had no disabled styling at all, so the model's own
       camera on a model that carries none looked exactly like the four presets beside it - offered,
       pressable, and doing nothing. Disable-don't-hide only works if disabled is visible. */
    .mode-selector button:disabled {
        opacity: 0.4;
        cursor: default;
    }
    .mode-selector button:disabled:hover {
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.18));
    }

    .mode-selector button:first-child { border-radius: var(--radius-3) 0 0 var(--radius-3); }
    .mode-selector button:last-child { border-right-width: var(--space-1); border-radius: 0 var(--radius-3) var(--radius-3) 0; }
    .mode-selector button:hover {
        background: var(--vscode-button-secondaryHoverBackground, rgba(128, 128, 128, 0.3));
    }
    .mode-selector button.active {
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
        border-color: var(--vscode-button-background);
    }
    /* The neighbour's shared edge, so the selected segment is not outlined by the one before it. */
    .mode-selector button.active + button { border-left-color: var(--vscode-button-background); }

    /* ── Buttons ───────────────────────────────────────────────────────── */
    .btn {
        /* A row, so a button can carry a codicon beside its label without the two colliding. */
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-4);
        padding: var(--space-4) var(--space-12);
        border-radius: var(--radius-3);
        border: var(--space-1) solid var(--vscode-button-border, var(--vscode-widget-border, rgba(128, 128, 128, 0.4)));
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.25));
        box-shadow: 0 1px 0 rgba(0, 0, 0, 0.25);
        color: var(--vscode-button-secondaryForeground, #ccc);
        font-family: var(--vscode-font-family, sans-serif);
        font-size: var(--font-size-body);
        cursor: pointer;
    }
    .btn.primary {
        background: var(--vscode-button-background, #0e639c);
        color: var(--vscode-button-foreground, #fff);
    }
    .btn:hover:not(:disabled) { filter: brightness(1.1); }
    .btn:disabled { opacity: 0.5; cursor: default; }
    /* For a button whose glyph IS the label - a transport control, say. The .icon-btn language is
       for toolbars, where a row of borderless glyphs reads as a toolbar; standing alone in a
       settings pane the same button reads as an ornament, because nothing says it can be pressed. */
    .btn.compact { padding: var(--space-4) var(--space-8); }

    /* ── Dialogs ───────────────────────────────────────────────────────── */
    .modal-backdrop {
        position: fixed;
        inset: 0;
        z-index: 40;
        display: flex;
        align-items: center;
        justify-content: center;
        background: rgba(0, 0, 0, 0.45);
    }
    /* A moved dialog is positioned by the hook, so the backdrop must stop centring it. */
    .modal {
        position: relative;
        /* border-box, so the width the resize hook writes back is the width it measured. This
           webview has no global box-sizing reset (the story graph does), so a content-box dialog
           grew by its own padding and border the instant it was first dragged. */
        box-sizing: border-box;
        min-width: 280px;
        max-width: min(440px, 90vw);
        max-height: 85vh;
        /* hidden, not auto: the box itself must never scroll, or the absolutely-positioned resize
           handles scroll away with it - and anything overhanging its edges gets clipped. The body
           scrolls instead, which also keeps the title bar and the buttons pinned. */
        overflow: hidden;
        display: flex;
        flex-direction: column;
        gap: var(--space-8);
        /* Coupled to .modal-title below, which pulls out over exactly this padding to meet the
           dialog's edges. The two are one measurement: change this and the title's negative margin
           has to follow, or the bar stops reaching the edge. */
        padding: var(--space-12) var(--space-16);
        border-radius: var(--radius-6);
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorWidget-background, #252526);
        box-shadow: 0 6px 24px rgba(0, 0, 0, 0.4);
    }
    /* The title bar is the drag handle, so it has to LOOK like one - a flat, full-bleed bar in the
       host's own title-bar colours, which is where a user already expects to grab a window. Pulled
       out over the dialog's padding to meet its edges. */
    .modal-title, .modal > h2.drag-handle {
        /* Derived from the dialog's padding rather than restated, so the bar cannot come adrift
           from the edge it is supposed to meet. */
        margin: calc(var(--space-12) * -1) calc(var(--space-16) * -1) 0;
        /* Horizontally the same inset as the dialog body, so the title sits over the content it
           names. It used to be 14px against the body's 16px, leaving the title 2px to the left of
           everything beneath it. */
        padding: var(--space-6) var(--space-16);
        /* The dialog's radius less its border, or the bar's corners sit proud of the dialog's. */
        border-radius: calc(var(--radius-6) - var(--space-1)) calc(var(--radius-6) - var(--space-1)) 0 0;
        border-bottom: var(--space-1) solid var(--vscode-titleBar-border, var(--vscode-widget-border, rgba(128, 128, 128, 0.35)));
        background: var(--vscode-titleBar-activeBackground, var(--vscode-editorWidget-background, #3c3c3c));
        color: var(--vscode-titleBar-activeForeground, var(--vscode-foreground, #ccc));
        font-weight: 600;
        font-size: 1em;
    }
    .drag-handle { cursor: move; user-select: none; touch-action: none; }

    /* The only thing a dialog is allowed to vary: how much room its content needs. Everything else -
       padding, radius, border, shadow, title bar, handles - is the same box, or they drift into two
       designs that merely resemble each other. */
    .modal.modal-wide { min-width: 380px; max-width: min(560px, 90vw); }

    /* Eight grips: four edges, four corners, all wholly inside the box so nothing is clipped.

       The class is namespaced on purpose. The dock's width sash already owns a bare '.resize-handle'
       (see locGridStyles), which is interpolated after this block at equal specificity - so a shared
       name let it win and flatten all eight of these into its own 6px full-height strip on the left
       edge. Every handle then sat on top of the others at the same place, and the dialog looked as
       though only its left border resized. */
    .modal-resize { position: absolute; touch-action: none; z-index: 2; }
    .modal-resize-n { top: 0; left: 10px; right: 10px; height: 6px; cursor: ns-resize; }
    .modal-resize-s { bottom: 0; left: 10px; right: 10px; height: 6px; cursor: ns-resize; }
    .modal-resize-w { left: 0; top: 10px; bottom: 10px; width: 6px; cursor: ew-resize; }
    .modal-resize-e { right: 0; top: 10px; bottom: 10px; width: 6px; cursor: ew-resize; }
    .modal-resize-nw { top: 0; left: 0; width: 12px; height: 12px; cursor: nwse-resize; }
    .modal-resize-ne { top: 0; right: 0; width: 12px; height: 12px; cursor: nesw-resize; }
    .modal-resize-sw { bottom: 0; left: 0; width: 12px; height: 12px; cursor: nesw-resize; }
    .modal-resize-se {
        bottom: 0;
        right: 0;
        width: 14px;
        height: 14px;
        cursor: nwse-resize;
        /* Two hairlines in the corner - the conventional grip, without shipping an image. */
        background:
            linear-gradient(135deg, transparent 50%,
                var(--vscode-widget-border, rgba(128, 128, 128, 0.55)) 50% 60%, transparent 60%),
            linear-gradient(135deg, transparent 70%,
                var(--vscode-widget-border, rgba(128, 128, 128, 0.55)) 70% 80%, transparent 80%);
    }

    /* The flexible part: the title bar and the buttons keep their height and the body takes what is
       left, scrolling when the content needs more. min-height:0 because a flex item will not shrink
       below its content without it, which would push the buttons out of a resized dialog. */
    .modal-body {
        display: flex;
        flex-direction: column;
        gap: var(--space-8);
        flex: 1 1 auto;
        min-height: 0;
        overflow-y: auto;
    }
    .modal-note { margin: 0; opacity: 0.8; font-size: var(--font-size-smaller); line-height: 1.35; }
    .modal-title, .modal > h2.drag-handle, .modal-buttons, .dialog-actions,
    .modal-pinned { flex: 0 0 auto; }

    /* Content that stays put while the body scrolls - for whatever the rest of the dialog is about,
       and for the messages explaining why its confirm button is disabled.

       The rule belongs here rather than on the first scrolling section: it marks where the fixed
       part ends, so scrolled content passes under a visible edge instead of appearing to slide out
       from beneath the text above it. */
    .modal-pinned {
        display: flex;
        flex-direction: column;
        gap: var(--space-8);
        padding-bottom: var(--space-8);
        border-bottom: var(--space-1) solid var(--vscode-panel-border, #444);
    }
    .modal-buttons { display: flex; align-items: center; gap: var(--space-8); }

    /* A one-line hint that shares the button row, so it stays readable however the body is scrolled
       or the dialog resized - a hint about what confirming will do is worth nothing once it has
       scrolled out of sight. The auto margin is what pushes the buttons to the right.

       Only for one-liners. A note that explains a control belongs beside that control, in the body. */
    .modal-footnote {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        margin: 0 auto 0 0;
        opacity: 0.75;
        font-size: var(--font-size-smaller);
        line-height: 1.35;
    }
    .modal-footnote .codicon { font-size: var(--icon-size-14); opacity: 0.9; flex: 0 0 auto; }

    /* Aligned to the first line rather than centred: the message can wrap, and a centred icon then
       drifts into the middle of the text block. */
    .field-error {
        display: flex;
        align-items: flex-start;
        gap: var(--space-6);
    }
    .field-error .codicon { font-size: var(--icon-size-14); flex: 0 0 auto; margin-top: var(--space-2); }

    .choice-list { display: flex; flex-direction: column; gap: var(--space-4); }
    .choice {
        display: grid;
        grid-template-columns: auto auto 1fr;
        align-items: center;
        gap: var(--space-8);
        padding: var(--space-6) var(--space-8);
        border-radius: var(--radius-3);
        border: var(--space-1) solid transparent;
        cursor: pointer;
    }
    .choice:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.15)); }
    .choice.selected {
        border-color: var(--vscode-focusBorder, #007fd4);
        background: var(--vscode-list-activeSelectionBackground, rgba(0, 127, 212, 0.15));
    }
    .choice-label { font-weight: 600; }
    .choice-detail { opacity: 0.75; font-size: var(--font-size-smaller); }
`;

/**
 * The dock body and foot. `scrollbar-gutter` keeps the usable width the same whether the dock is
 * scrolling or not, so the tile grid does not re-flow the moment content grows past the fold.
 */
export const dockBodyCss = `
    /* scrollbar-gutter keeps the usable width the same whether the dock is scrolling or not, so the
       tile grid does not re-flow the moment content grows past the fold.

       both-edges as well, because the scrollbar sits INSIDE the padding box: with the gutter on one
       side only, 8px of padding put every section 8px from the left edge and 8 + 15 = 23px from the
       right, so the whole dock read as sitting a scrollbar's width left of centre. It does. */
    .dock-content { scrollbar-gutter: stable both-edges; }
`;

/**
 * The right-hand tool dock itself.
 *
 * Tools live on the right in this extension - that is the layout the story graph and the
 * localisation editors already established - so the frame is shared rather than restated. Only the
 * geometry is here; a dock's header is its own, because what belongs in one differs per editor.
 */
export const rightDockCss = `
    .right-dock {
        position: relative;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        min-height: 0;
        border-left: var(--space-1) solid var(--vscode-panel-border);
        background: var(--vscode-sideBar-background);
    }
    /* The gutter is set in dockBodyCss, which is interpolated after this one and would win anyway.
       One owner: this rule declaring it too was how it came to be set twice with different values,
       and the losing declaration was the one carrying the fix. */
    .dock-content { flex: 1; min-height: 0; overflow-y: auto; padding: var(--space-8); }
    .dock-hint { font-size: var(--font-size-12); color: var(--vscode-descriptionForeground); padding: var(--space-8) var(--space-4); }
    /* Sits just outside the dock's left edge, so the grab area is not stolen from its content. */
    .resize-handle-w {
        position: absolute;
        top: 0;
        left: -3px;
        width: 6px;
        height: 100%;
        cursor: ew-resize;
        z-index: 2;
    }
    .resize-handle-w:hover { background: var(--vscode-sash-hoverBorder, #007fd4); }
`;

/**
 * The dock's top level: the row that carries mode switches and the severity tag, and the buttons in
 * it.
 *
 * Shared for the reason the rest of this file is. The story graph and the localisation grids had
 * grown near-identical copies of all of this - the same class names, the same soft icon buttons, the
 * same severity pill - differing only in ways nobody chose: one had `cursor: pointer` and the other
 * did not, one knew about `sev-info` and the other did not. The severity tag in particular has to
 * mean ONE thing across the extension, and it cannot if each editor tints it from its own rules.
 *
 * `min-height` is deliberately NOT here. It is the one part that legitimately differs - the story
 * graph's header holds a rotary dial and needs 78px, a header with a button or two needs 34 - so
 * each editor sets its own after interpolating this.
 */
export const dockHeaderCss = `
    /* Whatever is centred stays dead-centre; the edge controls float over the sides so they can
       never shift it. */
    .dock-header {
        position: relative;
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-6);
        padding: var(--space-6) var(--space-8);
        border-bottom: var(--space-1) solid var(--vscode-panel-border, #444);
    }
    .dock-header .header-left { position: absolute; left: 8px; }
    .dock-header .header-right { position: absolute; right: 8px; }

    /* Soft, icon-forward buttons: almost no chrome until hovered, the glyph does the talking. */
    .icon-btn {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        min-width: 26px;
        justify-content: center;
        padding: var(--space-4) var(--space-6);
        background: transparent;
        border: none;
        border-radius: var(--radius-6);
        color: var(--vscode-foreground, #ccc);
        opacity: 0.72;
        font-size: var(--icon-size-14);
        line-height: 1;
        cursor: pointer;
    }
    .icon-btn:hover {
        background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.15));
        opacity: 1;
    }
    /* An ACTION rather than a state.

       The icon-btn language is a toolbar's: borderless until hovered, and .active fills it in. That is
       right for something that LATCHES, and wrong for something that fires and returns - standing
       in a band beside controls that latch, a bare glyph reads as a switch that happens to be off.
       A resting border is what says press me. */
    .icon-btn.as-action {
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.14));
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        opacity: 1;
    }
    .icon-btn.as-action:hover:not(:disabled) {
        background: var(--vscode-button-secondaryHoverBackground, rgba(128, 128, 128, 0.28));
        border-color: var(--vscode-focusBorder, #007fd4);
    }
    .icon-btn:disabled { opacity: 0.35; cursor: default; }
    .icon-btn.active {
        background: var(--vscode-button-background, #0e639c);
        color: var(--vscode-button-foreground, #fff);
        opacity: 1;
    }
    .icon-btn.sev-unvalidated { color: var(--vscode-foreground, #ccc); opacity: 1; }
    .icon-btn.sev-ok { color: var(--vscode-charts-green, #89d185); opacity: 1; }
    .icon-btn.sev-info { color: var(--vscode-charts-blue, #3794ff); opacity: 1; }
    .icon-btn.sev-warning { color: var(--vscode-charts-yellow, #cca700); opacity: 1; }
    .icon-btn.sev-error { color: var(--vscode-errorForeground, #f14c4c); opacity: 1; }
    /* Header controls read out at roughly VS Code activity-bar icon scale. */
    .dock-header .icon-btn { font-size: var(--icon-size-18); padding: var(--space-6) var(--space-8); }
    .dock-header .icon-btn .codicon { font-size: var(--icon-size-18); }

    /* The severity tag is an always-present soft pill, tinted with a hue of its own state colour.
       Must stay after .icon-btn: same specificity, so order is what lets it win. */
    .validate-btn { border-radius: var(--radius-pill); font-weight: 600; }
    .validate-btn.sev-unvalidated {
        background: color-mix(in srgb, var(--vscode-foreground, #ccc) 15%, transparent);
    }
    .validate-btn.sev-ok {
        background: color-mix(in srgb, var(--vscode-charts-green, #89d185) 14%, transparent);
    }
    .validate-btn.sev-info {
        background: color-mix(in srgb, var(--vscode-charts-blue, #3794ff) 14%, transparent);
    }
    .validate-btn.sev-warning {
        background: color-mix(in srgb, var(--vscode-charts-yellow, #cca700) 16%, transparent);
    }
    .validate-btn.sev-error {
        background: color-mix(in srgb, var(--vscode-errorForeground, #f14c4c) 16%, transparent);
    }
    .validate-btn:hover { filter: brightness(1.2); }
    .validate-btn:disabled:hover { filter: none; }
`;

/**
 * The dismissable bar across the FOOT OF THE EDITOR that validation results appear in - the frame
 * `ProblemsPanel` renders, and the rows inside it.
 *
 * Where these go is settled and is not a per-editor choice: the severity tag lives in the dock
 * header, and pressing it opens a panel across the bottom of the editor, never a list inside the
 * dock. `LocProblemsBar` records why, from having tried it the other way - the dock is narrow, so
 * every message is truncated, and a list that appears and disappears there shoves the controls
 * around it. Both existing editors had grown their own copy of these rules.
 *
 * Each editor still names and skins its own container (`.problems`, `.loc-problems`) - that is what
 * the component's `className` prop is for - because how the bar sits in its editor's layout differs.
 */
export const problemsPanelCss = `
    /* The panel is dragged by its top edge. Sits just outside it, so the grab area is not stolen
       from the first row of content. */
    .resize-handle-n {
        position: absolute;
        top: -3px;
        left: 0;
        height: 6px;
        width: 100%;
        cursor: ns-resize;
        z-index: 2;
    }
    .resize-handle-n:hover, .resize-handle-n:active {
        background: var(--vscode-sash-hoverBorder, var(--vscode-focusBorder, #007fd4));
    }

    .panel-bar {
        flex-shrink: 0;
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-4);
    }
    .panel-title { font-weight: bold; font-size: var(--font-size-11); color: var(--vscode-descriptionForeground, #999); }
    .panel-close {
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground, #999);
        cursor: pointer;
        padding: 0 var(--space-4);
        flex-shrink: 0;
    }
    .panel-close:hover { background: transparent; color: var(--vscode-editor-foreground, #ccc); }

    /* The view filter, beside the count it qualifies.

       margin-left auto on the CLOSE button instead of the bar's space-between, so the filter
       sits with the title rather than drifting to the middle: it reads as part of the count, not
       as a second action competing with Close. */
    .panel-filter {
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground, #999);
        cursor: pointer;
        padding: 0 var(--space-6);
        margin-left: var(--space-8);
        font-size: var(--font-size-11);
        flex-shrink: 0;
        display: inline-flex;
        align-items: center;
        gap: var(--space-4);
    }
    .panel-filter:hover:not(:disabled) {
        background: transparent;
        color: var(--vscode-editor-foreground, #ccc);
    }
    /* On, and therefore hiding something. Coloured because this is the state a reader has to be
       able to notice without going looking - it is why the table is shorter than they expect. */
    .panel-filter.active { color: var(--vscode-charts-blue, #3794ff); }
    /* Nothing to act on. Kept, not removed - see the panel's own note. */
    .panel-filter:disabled { opacity: 0.4; cursor: default; }

    .panel-bar .panel-close { margin-left: auto; }

    /* The scrolling part: the title bar keeps its height and the rows take what is left. */
    .problem-list { flex: 1; min-height: 0; overflow-y: auto; padding-bottom: var(--space-2); }

    .problem-row { display: flex; gap: var(--space-6); align-items: center; padding: var(--space-1) var(--space-6); }
    /* Only where the row actually goes somewhere - a row that names nothing navigable must not
       advertise a click, which is the same rule the ship-name list follows. */
    .problem-row.clickable { cursor: pointer; }
    .problem-row:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.15)); }
    .problem-row .codicon.sev-error { color: var(--vscode-errorForeground, #f14c4c); }
    .problem-row .codicon.sev-warning { color: var(--vscode-charts-yellow, #cca700); }
    .problem-row .codicon.sev-info { color: var(--vscode-charts-blue, #3794ff); }
    /* The message takes the room the target label leaves, clamped to the row so a long one cannot
       reflow the list - these are short and each row carries the full text as its tooltip. An
       editor whose messages are prose rather than labels overrides white-space in its own sheet. */
    .problem-msg {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
`;

/** The dock foot, where the search and overview controls live. Shared for its breathing room. */
export const dockOverviewCss = `
    .dock-overview {
        flex-shrink: 0;
        border-top: var(--space-1) solid var(--vscode-panel-border, #444);
        padding: var(--space-8) var(--space-8);
        display: flex;
        flex-direction: column;
        gap: var(--space-8);

        /* Capped and scrollable, or a foot that outgrows the panel simply runs off the bottom -
           taking with it whatever control would have closed it again. That is not a squeeze on the
           content above: flex-shrink 0 still keeps the foot at its natural size until it would
           need more than half the dock. */
        max-height: 55%;
        overflow-y: auto;
    }
    .dock-overview > input[type=text] { width: 100%; }
`;

/**
 * The rotary mode switch.
 *
 * Shared because the switch itself is shared: the story graph had it first and the model preview
 * takes the same one, so "how this product switches modes" stays one implementation and one look.
 * The icon sizing lives with it - it is part of the dial rather than of whatever hosts it.
 */
export const rotarySwitchCss = `
    /* Glyphs sized WELL inside their circles. At 22px across, a 13px icon covers three fifths of
       the face and the button stops reading as a round switch position - it reads as a smudge.
       These ratios (18/40 in the readout, 11/24 on a position) leave a visible ring of ground. */
    .rotary-center .codicon { font-size: var(--icon-size-18); }
    .rotary-pos .codicon { font-size: var(--icon-size-11); }

    /* Rotary mode switch - large clickable readout (cycles modes) with the three modes on an arc above. */
    .rotary { position: relative; width: 108px; height: 80px; flex-shrink: 0; }
    .rotary-center {
        position: absolute;
        left: 50%; top: 70%;
        transform: translate(-50%, -50%);
        width: 40px; height: 40px;
        padding: 0;
        display: flex; align-items: center; justify-content: center;
        border-radius: var(--radius-round);
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
        border: var(--space-2) solid var(--vscode-focusBorder);
        cursor: pointer;
        z-index: 1;
    }
    .rotary-center:hover { background: var(--vscode-button-hoverBackground, var(--vscode-button-background)); }
    .rotary-pos {
        position: absolute;
        left: 50%; top: 70%;
        width: 24px; height: 24px;
        padding: 0;
        display: flex; align-items: center; justify-content: center;
        border: none;
        border-radius: var(--radius-round);
        line-height: 1;
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.25));
        color: var(--vscode-button-secondaryForeground, #ccc);
        cursor: pointer;
        opacity: 0.6;
    }
    .rotary-pos:hover { opacity: 1; }
    /* The active position is LIT, like the indicator lamp over a console switch - that is the feel
       this control is after, and a merely tinted glyph does not read as a lamp.

       The two shadows are what keep it from merging with the readout below it: a ring of the page's
       own ground first, so there is a visible gap however close the two sit, then the glow. An
       earlier attempt at a flat fill put two identical discs a few pixels apart and read as a
       rendering fault; an outline landed a blue ring against the readout's blue border. */
    .rotary-pos.active {
        opacity: 1;
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
        box-shadow:
            0 0 0 2px var(--vscode-editor-background, #1f1f1f),
            0 0 7px 1px var(--vscode-focusBorder, #0078d4);
    }
`;

/**
 * The item inspector: what the selected row IS, and its bulk geometry tables.
 *
 * Shared because the inspector is its own editor tab and the preview still hosts the same rows -
 * the same definition lists, the same tables, the same swatch. Copying it would make "what a mesh's
 * facts look like" two stylesheets, which is the drift this file exists to prevent. A host that
 * needs more room than a dock allows overrides the caps in its own sheet rather than forking these.
 */
export const inspectorCss = `
    /* What the selected row actually IS, read out of the file.

       A definition list rather than a table: the rows are label/value pairs of wildly different
       lengths - a shader name runs past the column a triangle count needs - and a table would set
       one column width for both. The label column is fixed and the value takes the rest, so the
       labels line up while a long value wraps under itself instead of widening the panel. */
    .inspect-group + .inspect-group { margin-top: var(--space-8); }

    .inspect-title {
        font-size: var(--font-size-smaller);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        opacity: 0.75;
        margin-bottom: var(--space-4);
    }

    .inspect-rows {
        display: grid;
        grid-template-columns: minmax(0, 8.5em) minmax(0, 1fr);
        gap: var(--space-2) var(--space-8);
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
        font-size: var(--font-size-smaller);
        line-height: 1.35;
    }

    /* The bulk tables, as a tabbed box: a strip of tabs sitting ON the panel they open, which is
       what a reader already knows how to use. It was a mode selector over a paged table, an
       arrangement built for a 320px flyout where a hundred rows at a time was all that would fit.

       Wraps, because three tabs ending in "Bone mapping" reach the edge of a narrow host. */
    .geometry-tabs {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-2);
        margin: var(--space-4) 0 0;
    }

    .geometry-tab {
        display: inline-flex;
        align-items: center;
        gap: var(--space-4);
        padding: var(--space-4) var(--space-12);
        border: var(--space-1) solid transparent;
        border-bottom: none;
        border-radius: var(--radius-3) var(--radius-3) 0 0;
        background: none;
        color: var(--vscode-descriptionForeground, #999);
        font: inherit;
        cursor: pointer;
    }
    .geometry-tab:hover:not(:disabled) { background: var(--vscode-list-hoverBackground); }
    .geometry-tab:disabled { opacity: 0.4; cursor: default; }

    /* The open tab joins the panel below it: same border, and no line between the two. */
    .geometry-tab.open {
        border-color: var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorWidget-background, #202020);
        color: var(--vscode-foreground, #ccc);
        margin-bottom: -var(--space-1);
        padding-bottom: var(--space-4);
    }

    .geometry-tab .section-count {
        margin-left: 0;
        opacity: 0.75;
        font-variant-numeric: tabular-nums;
    }

    .geometry-panel {
        border: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: 0 var(--radius-3) var(--radius-3) var(--radius-3);
        overflow: hidden;
    }

    /* A vertex row is ten columns wide and the list is however long the sub-mesh is, so the table
       scrolls in BOTH directions inside itself and its host keeps its shape. The height is set by
       whoever mounts it - a dock and a full editor tab want very different amounts of it. */
    .geometry-scroll {
        overflow: auto;
        max-height: 40vh;
    }

    .geometry-table {
        border-collapse: collapse;
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: var(--font-size-smaller);
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
    }

    .geometry-table th, .geometry-table td { padding: var(--space-2) var(--space-8); text-align: right; }

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

    /* How much of the table is in hand. The rows arrive in pages either way - the server caps a
       request at 500 - and a reader several thousand rows down wants to know whether the end they
       are looking at is the table's end or merely today's. */
    .geometry-foot {
        padding: var(--space-2) var(--space-8);
        border-top: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        color: var(--vscode-descriptionForeground, #999);
        font-size: var(--font-size-smaller);
        font-variant-numeric: tabular-nums;
    }

    .inspect-swatch {
        display: inline-block;
        width: 0.8em;
        height: 0.8em;
        margin-right: var(--space-4);
        vertical-align: -1px;
        border-radius: var(--radius-3);
        /* Over a border rather than inside it: a border would eat into the colour being judged. */
        outline: var(--space-1) solid var(--vscode-widget-border, rgba(128, 128, 128, 0.5));
        outline-offset: -1px;
    }
`;
