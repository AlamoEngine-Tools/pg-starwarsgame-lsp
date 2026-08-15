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

export const dockChromeCss = `
    /* One tile size for every dock in the extension.
       The width has to leave room for the dock's own padding AND its vertical scrollbar, which is
       always there once the content is taller than the panel - 136px fitted the padding but not the
       scrollbar, so two tiles overflowed by a few pixels and the dock grew a horizontal one. */
    --dock-tile-w: 128px;
    /* Tall enough for the worst case a tile actually holds: a glyph, a label and the raw token
       badge under it. At 64px that combination overflowed and was clipped mid-word. */
    --dock-tile-h: 78px;
    --dock-tile-gap: 6px;

    /* ── Sections ──────────────────────────────────────────────────────── */
    .dock-section { display: flex; flex-direction: column; gap: 6px; margin-bottom: 14px; }
    .dock-section:last-child { margin-bottom: 0; }
    .dock-section-title {
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        opacity: 0.65;
        color: var(--vscode-descriptionForeground, #999);
        display: flex;
        align-items: center;
        gap: 4px;
    }
    .dock-section-title .section-count { margin-left: auto; font-weight: normal; opacity: 0.75; }

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
        gap: 4px;
        padding: 8px 6px;
        border-radius: 6px;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorWidget-background, rgba(128, 128, 128, 0.08));
        color: var(--vscode-foreground, #ccc);
        font-family: var(--vscode-font-family, sans-serif);
        font-size: var(--vscode-font-size, 13px);
        cursor: pointer;
        text-align: center;
        min-width: 0;
        overflow: hidden;
    }
    .dock-tile:hover {
        background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.2));
        border-color: var(--vscode-focusBorder, #007fd4);
    }
    .dock-tile:focus-visible { outline: 1px solid var(--vscode-focusBorder, #007fd4); }
    .dock-tile[draggable="true"] { cursor: grab; }
    .dock-tile[draggable="true"]:active { cursor: grabbing; }
    .dock-tile .codicon, .dock-tile .tile-glyph { font-size: 18px; line-height: 1; opacity: 0.85; }
    /* A tile carrying a badge has three stacked things in it, so its label gets one line; without
       one it can have two. Either way the tile is the same height and nothing is cut off. */
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
        font-size: 0.8em;
        padding: 0 4px;
        border: 1px solid var(--vscode-panel-border, #444);
        border-radius: 3px;
        opacity: 0.7;
        max-width: 100%;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    /* ── Search ────────────────────────────────────────────────────────── */
    /* The mode toggles sit inside the box, the way an editor's find widget puts them - that buys
       back a whole row, which is what made this corner feel cramped. */
    .dock-search { display: flex; flex-direction: column; gap: 6px; }
    .search-field { position: relative; display: flex; align-items: center; }
    .search-field input[type=text] { width: 100%; padding: 4px 8px; }
    /* Room for the three toggles, which overlay the trailing edge. Only where they exist - the
       graph editor's filters have no modes and must not carry a gap for buttons it never draws. */
    .search-field.with-modes input[type=text] { padding-right: 78px; }
    .search-field .mode-group { position: absolute; right: 3px; display: flex; gap: 1px; }
    .search-field .mode-group .icon-btn { min-width: 22px; padding: 2px 4px; border-radius: 4px; }
    .search-field .mode-group .codicon { font-size: 14px; }

    /* A control and its label are a pair and sit close; the gap BETWEEN fields is what the
       section body sets, and it is larger. Three pixels here had the label almost touching the
       control above it, so a run of settings read as one block of text with sliders in it. */
    .field { display: flex; flex-direction: column; gap: 5px; }
    .field select, .field input[type=text] { width: 100%; }
    /* Room to actually grab. A range input defaults to a track a few pixels tall inside a box the
       browser sizes to the thumb, which in a settings panel reads as a hairline. */
    .field input[type=range] { width: 100%; height: 18px; margin: 0; }
    .field .btn { width: 100%; padding: 5px 14px; }
    /* A row, so a label can carry a checkbox on its left and a readout on its right. Laid out the
       way a section title is: the count goes to the far edge. Without that they simply butted up
       against the label - "Wind speed1.0", "Light around45 deg" - which read as a missing space. */
    .field-label { display: flex; align-items: center; gap: 5px; font-size: 0.9em; opacity: 0.7; }

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
        border-radius: 50%;
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
        border-radius: 1px;
        background: var(--vscode-descriptionForeground, #999);
        opacity: 0.55;
        pointer-events: none;
    }

    /* A foldable group of controls, after BSI CX's step library: a small coloured heading, a count
       where the number says something, and room between groups so the eye lands on a heading. */
    .panel-section { display: flex; flex-direction: column; gap: 10px; }

    /* The whole heading folds the group, not just the chevron - a 12px target beside a word that
       plainly names the thing is a target people miss. Transparent and borderless: it is a heading
       that happens to be pressable, not a button with a title in it. */
    .panel-section-head {
        display: flex;
        align-items: center;
        gap: 5px;
        width: 100%;
        padding: 0;
        border: none;
        background: transparent;
        color: var(--vscode-textLink-foreground, #4daafc);
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        text-align: left;
        cursor: pointer;
    }
    .panel-section-head:hover { color: var(--vscode-textLink-activeForeground, #6fb3ff); }
    .panel-section-head .codicon { font-size: 13px; opacity: 0.8; }
    .panel-section-title { flex: 1; min-width: 0; }
    /* To the far edge, quieter than the name: it reports, the name identifies. */
    .panel-section-count {
        font-weight: 400;
        letter-spacing: 0;
        text-transform: none;
        color: var(--vscode-descriptionForeground, #999);
    }
    .panel-section-body {
        display: flex;
        flex-direction: column;
        gap: 14px;
        /* Indented under its heading, so a fold reads as a group closing rather than as controls
           disappearing from a flat list. */
        padding-left: 2px;
    }
    .field-label .section-count { margin-left: auto; opacity: 0.85; }
    /* The sentence under a control that says what it does. It is support, not content, so it is
       quieter and tighter than the setting it explains. */
    .field-note {
        font-size: 0.9em;
        line-height: 1.35;
        color: var(--vscode-descriptionForeground, #999);
    }
    /* The same sentence, when it is telling the reader something is off rather than explaining
       the control. Warning colour and no other change: it has to read as the same kind of text in
       the same place, or it becomes an alert the eye has to deal with on every glance. */
    .field-warn {
        font-size: 0.9em;
        line-height: 1.35;
        color: var(--vscode-editorWarning-foreground, #cca700);
    }
    /* A colour well, not a form control. The browser renders a colour input as a wide bordered
       button with a swatch inside it, which took a whole row for one colour.
       No backticks in this file: it is one big template literal. */
    .field input[type=color], .field-label input[type=color] {
        margin-left: auto;
        width: 28px;
        height: 20px;
        padding: 0;
        flex-shrink: 0;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        border-radius: 3px;
        background: none;
        cursor: pointer;
    }
    .field input[type=range] { flex: 1; min-width: 0; }
    .check-field {
        display: flex;
        align-items: flex-start;
        gap: 6px;
        cursor: pointer;
        line-height: 1.35;
    }
    .check-field input[type=checkbox] { margin-top: 2px; flex-shrink: 0; }
    .check-detail { opacity: 0.7; }

    /* A one-of-N choice as joined buttons: the alternatives stay visible, and switching is one
       press rather than open-then-pick. Segments share their borders, so the group reads as one
       control rather than as a row of separate buttons. */
    .mode-selector { display: inline-flex; width: 100%; }
    .mode-selector button {
        flex: 1;
        min-width: 0;
        padding: 3px 6px;
        border: 1px solid var(--vscode-panel-border, #444);
        border-right-width: 0;
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.18));
        color: var(--vscode-foreground);
        font-size: 0.9em;
        line-height: 1.5;
        cursor: pointer;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .mode-selector button:first-child { border-radius: 4px 0 0 4px; }
    .mode-selector button:last-child { border-right-width: 1px; border-radius: 0 4px 4px 0; }
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
        gap: 5px;
        padding: 4px 14px;
        border-radius: 4px;
        border: 1px solid var(--vscode-button-border, var(--vscode-widget-border, rgba(128, 128, 128, 0.4)));
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.25));
        box-shadow: 0 1px 0 rgba(0, 0, 0, 0.25);
        color: var(--vscode-button-secondaryForeground, #ccc);
        font-family: var(--vscode-font-family, sans-serif);
        font-size: var(--vscode-font-size, 13px);
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
    .btn.compact { padding: 4px 9px; }

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
        gap: 10px;
        padding: 14px 16px;
        border-radius: 8px;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorWidget-background, #252526);
        box-shadow: 0 6px 24px rgba(0, 0, 0, 0.4);
    }
    /* The title bar is the drag handle, so it has to LOOK like one - a flat, full-bleed bar in the
       host's own title-bar colours, which is where a user already expects to grab a window. Pulled
       out over the dialog's padding to meet its edges. */
    .modal-title, .modal > h2.drag-handle {
        margin: -14px -16px 0;
        padding: 7px 14px;
        border-radius: 7px 7px 0 0;
        border-bottom: 1px solid var(--vscode-titleBar-border, var(--vscode-widget-border, rgba(128, 128, 128, 0.35)));
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
        gap: 8px;
        flex: 1 1 auto;
        min-height: 0;
        overflow-y: auto;
    }
    .modal-note { margin: 0; opacity: 0.8; font-size: 0.92em; line-height: 1.35; }
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
        gap: 8px;
        padding-bottom: 8px;
        border-bottom: 1px solid var(--vscode-panel-border, #444);
    }
    .modal-buttons { display: flex; align-items: center; gap: 8px; }

    /* A one-line hint that shares the button row, so it stays readable however the body is scrolled
       or the dialog resized - a hint about what confirming will do is worth nothing once it has
       scrolled out of sight. The auto margin is what pushes the buttons to the right.

       Only for one-liners. A note that explains a control belongs beside that control, in the body. */
    .modal-footnote {
        display: flex;
        align-items: center;
        gap: 6px;
        margin: 0 auto 0 0;
        opacity: 0.75;
        font-size: 0.9em;
        line-height: 1.35;
    }
    .modal-footnote .codicon { font-size: 14px; opacity: 0.9; flex: 0 0 auto; }

    /* Aligned to the first line rather than centred: the message can wrap, and a centred icon then
       drifts into the middle of the text block. */
    .field-error {
        display: flex;
        align-items: flex-start;
        gap: 6px;
    }
    .field-error .codicon { font-size: 14px; flex: 0 0 auto; margin-top: 2px; }

    .choice-list { display: flex; flex-direction: column; gap: 4px; }
    .choice {
        display: grid;
        grid-template-columns: auto auto 1fr;
        align-items: center;
        gap: 8px;
        padding: 6px 8px;
        border-radius: 4px;
        border: 1px solid transparent;
        cursor: pointer;
    }
    .choice:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.15)); }
    .choice.selected {
        border-color: var(--vscode-focusBorder, #007fd4);
        background: var(--vscode-list-activeSelectionBackground, rgba(0, 127, 212, 0.15));
    }
    .choice-label { font-weight: 600; }
    .choice-detail { opacity: 0.75; font-size: 0.9em; }
`;

/**
 * The dock body and foot. `scrollbar-gutter` keeps the usable width the same whether the dock is
 * scrolling or not, so the tile grid does not re-flow the moment content grows past the fold.
 */
export const dockBodyCss = `
    /* scrollbar-gutter keeps the usable width the same whether the dock is scrolling or not, so the
       tile grid does not re-flow the moment content grows past the fold. */
    .dock-content { scrollbar-gutter: stable; }
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
        border-left: 1px solid var(--vscode-panel-border);
        background: var(--vscode-sideBar-background);
    }
    .dock-content { flex: 1; min-height: 0; overflow-y: auto; padding: 8px; }
    .dock-hint { font-size: 12px; color: var(--vscode-descriptionForeground); padding: 8px 4px; }
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
        gap: 6px;
        padding: 6px 8px;
        border-bottom: 1px solid var(--vscode-panel-border, #444);
    }
    .dock-header .header-left { position: absolute; left: 8px; }
    .dock-header .header-right { position: absolute; right: 8px; }

    /* Soft, icon-forward buttons: almost no chrome until hovered, the glyph does the talking. */
    .icon-btn {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        min-width: 26px;
        justify-content: center;
        padding: 5px 7px;
        background: transparent;
        border: none;
        border-radius: 8px;
        color: var(--vscode-foreground, #ccc);
        opacity: 0.72;
        font-size: 14px;
        line-height: 1;
        cursor: pointer;
    }
    .icon-btn:hover {
        background: var(--vscode-toolbar-hoverBackground, rgba(128, 128, 128, 0.15));
        opacity: 1;
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
    .dock-header .icon-btn { font-size: 18px; padding: 6px 9px; }
    .dock-header .icon-btn .codicon { font-size: 18px; }

    /* The severity tag is an always-present soft pill, tinted with a hue of its own state colour.
       Must stay after .icon-btn: same specificity, so order is what lets it win. */
    .validate-btn { border-radius: 14px; font-weight: 600; }
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
        padding: 2px 4px;
    }
    .panel-title { font-weight: bold; font-size: 11px; color: var(--vscode-descriptionForeground, #999); }
    .panel-close {
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground, #999);
        cursor: pointer;
        padding: 0 4px;
        flex-shrink: 0;
    }
    .panel-close:hover { background: transparent; color: var(--vscode-editor-foreground, #ccc); }

    /* The scrolling part: the title bar keeps its height and the rows take what is left. */
    .problem-list { flex: 1; min-height: 0; overflow-y: auto; padding-bottom: 2px; }

    .problem-row { display: flex; gap: 6px; align-items: center; padding: 1px 6px; }
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
        border-top: 1px solid var(--vscode-panel-border, #444);
        padding: 10px 8px;
        display: flex;
        flex-direction: column;
        gap: 8px;

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
    .rotary-center .codicon { font-size: 18px; }
    .rotary-pos .codicon { font-size: 11px; }

    /* Rotary mode switch - large clickable readout (cycles modes) with the three modes on an arc above. */
    .rotary { position: relative; width: 108px; height: 80px; flex-shrink: 0; }
    .rotary-center {
        position: absolute;
        left: 50%; top: 70%;
        transform: translate(-50%, -50%);
        width: 40px; height: 40px;
        padding: 0;
        display: flex; align-items: center; justify-content: center;
        border-radius: 50%;
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
        border: 2px solid var(--vscode-focusBorder);
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
        border-radius: 50%;
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
