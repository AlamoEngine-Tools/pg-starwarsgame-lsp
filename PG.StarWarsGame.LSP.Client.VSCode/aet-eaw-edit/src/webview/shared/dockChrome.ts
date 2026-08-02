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

    .field { display: flex; flex-direction: column; gap: 3px; }
    .field select, .field input[type=text] { width: 100%; }
    .field-label { font-size: 0.9em; opacity: 0.7; }
    .check-field {
        display: flex;
        align-items: flex-start;
        gap: 6px;
        cursor: pointer;
        line-height: 1.35;
    }
    .check-field input[type=checkbox] { margin-top: 2px; flex-shrink: 0; }
    .check-detail { opacity: 0.7; }

    /* ── Buttons ───────────────────────────────────────────────────────── */
    .btn {
        padding: 4px 14px;
        border-radius: 4px;
        border: 1px solid var(--vscode-button-border, transparent);
        background: var(--vscode-button-secondaryBackground, rgba(128, 128, 128, 0.25));
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
    .modal {
        min-width: 280px;
        max-width: min(440px, 90vw);
        max-height: 85vh;
        overflow: auto;
        display: flex;
        flex-direction: column;
        gap: 10px;
        padding: 14px 16px;
        border-radius: 8px;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorWidget-background, #252526);
        box-shadow: 0 6px 24px rgba(0, 0, 0, 0.4);
    }
    .modal-title { font-weight: 600; font-size: 1.05em; }
    .modal-body { display: flex; flex-direction: column; gap: 8px; }
    .modal-note { margin: 0; opacity: 0.8; font-size: 0.92em; line-height: 1.35; }
    .modal-buttons { display: flex; justify-content: flex-end; gap: 8px; }

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

/** The dock foot, where the search and overview controls live. Shared for its breathing room. */
export const dockOverviewCss = `
    .dock-overview {
        flex-shrink: 0;
        border-top: 1px solid var(--vscode-panel-border, #444);
        padding: 10px 8px;
        display: flex;
        flex-direction: column;
        gap: 8px;
    }
    .dock-overview > input[type=text] { width: 100%; }
`;
