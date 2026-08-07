// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Layout and theming for a localisation grid, shared by both editors.
//
// Entirely VS Code custom properties with literal fallbacks, so the grid follows the user's colour
// theme in both light and dark.
//
// A few rules here belong to one editor rather than both - the directive dropdown and spacer rows
// are credits-only, the inherited toggle is translations-only. They are inert in the other editor
// and are kept together while the two share one stylesheet.

import styled from 'styled-components';

import { dockBodyCss, dockChromeCss, dockOverviewCss } from '../shared/dockChrome';

export const Shell = styled.div`
    ${dockChromeCss}

    /* 100% of the host's element, not 100vh: viewport units ignore the body's own box, so any
       margin or padding the host applies would make this overflow by exactly that much. */
    height: 100%;
    display: flex;
    flex-direction: column;
    /* Containing block for the crawl preview, which covers the whole editor. */
    position: relative;
    color: var(--vscode-foreground, #ccc);
    background: var(--vscode-editor-background, #1e1e1e);
    font-family: var(--vscode-font-family, sans-serif);
    font-size: var(--vscode-font-size, 13px);

    /* Every control here is sized with width:100% inside a track or a column, and each also carries
       padding and a border. Without this they come out wider than what holds them - which is what
       put a horizontal scrollbar under a table whose columns fitted perfectly well. */
    input, select, textarea, button { box-sizing: border-box; }

    .message { padding: 12px; }

    .body { flex: 1; display: flex; min-height: 0; }

    /* The table column: the grid scrolls, the footer under it does not.
       min-height: 0 is load-bearing - without it the scrolling grid refuses to shrink below its
       content and pushes the footer off the bottom of the window. */
    .grid-column {
        flex: 1;
        display: flex;
        flex-direction: column;
        min-width: 0;
        min-height: 0;
    }

    .grid-area { flex: 1; overflow: auto; position: relative; }

    /* Where a dragged row will land. Pinned to the viewport rather than to the grid, so it stays on
       the boundary while the list scrolls under it. */
    .drop-indicator {
        position: fixed;
        left: 0;
        right: 0;
        height: 2px;
        background: var(--vscode-focusBorder, #007fd4);
        pointer-events: none;
        z-index: 15;
    }

    .step-help {
        margin: 0;
        font-size: 0.9em;
        opacity: 0.6;
        line-height: 1.35;
    }

    /* Validation results, across the foot of the table rather than down the dock - the same shape
       the story graph editor uses. */
    .loc-problems {
        position: relative;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        min-height: 0;
        border-top: 1px solid var(--vscode-panel-border, #444);
        background: var(--vscode-sideBar-background, #252526);
        font-size: 12px;
    }

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
    .panel-title {
        font-weight: bold;
        font-size: 11px;
        color: var(--vscode-descriptionForeground, #999);
    }
    .panel-close {
        background: transparent;
        border: none;
        color: var(--vscode-descriptionForeground, #999);
        cursor: pointer;
        padding: 0 4px;
        flex-shrink: 0;
    }
    .panel-close:hover { color: var(--vscode-editor-foreground, #ccc); }

    .problem-list { flex: 1; min-height: 0; overflow-y: auto; padding-bottom: 2px; }

    .problem-row {
        display: flex;
        gap: 6px;
        align-items: center;
        padding: 1px 6px;
    }
    .problem-row.clickable { cursor: pointer; }
    .problem-row:hover { background: var(--vscode-list-hoverBackground, rgba(128,128,128,0.15)); }
    .problem-row .codicon.sev-error { color: var(--vscode-errorForeground, #f14c4c); }
    .problem-row .codicon.sev-warning { color: var(--vscode-charts-yellow, #cca700); }
    .problem-row .codicon.sev-info { color: var(--vscode-charts-blue, #3794ff); }
    .problem-target {
        flex-shrink: 0;
        max-width: 220px;
        color: var(--vscode-descriptionForeground, #999);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .problem-msg {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .grid-footer {
        flex-shrink: 0;
        padding: 3px 8px;
        border-top: 1px solid var(--vscode-panel-border, #444);
        background: var(--vscode-editorWidget-background, #252526);
        font-size: 0.9em;
        opacity: 0.8;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .grid { display: block; }

    .head-row {
        display: grid;
        /* An item beyond the declared tracks makes a new column, never a new row. Without this a
           header cell the template did not account for wrapped underneath the first column, which
           reads as the table having lost its layout. */
        grid-auto-flow: column;
        grid-auto-columns: min-content;
        position: sticky;
        top: 0;
        z-index: 2;
        background: var(--vscode-editorWidget-background, #252526);
        border-bottom: 1px solid var(--vscode-panel-border, #444);
        font-weight: 600;
    }

    .head-row .cell { padding: 4px 8px; white-space: nowrap; }

    /* The column picker sits at the right-hand end of the header, above the rows' unused trailing
       track. Positioned so the flyout can hang from it. */
    .head-row .column-menu {
        position: relative;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 0;
    }
    .column-flyout {
        /* Fixed, not absolute: the header scrolls inside .grid-area, which clips whatever reaches
           past its edge - and this hangs off the rightmost cell. Placed from the gear's viewport
           box when it opens. */
        position: fixed;
        z-index: 25;
        min-width: 160px;
        display: flex;
        flex-direction: column;
        gap: 4px;
        padding: 8px 10px;
        border-radius: 6px;
        border: 1px solid var(--vscode-widget-border, rgba(128, 128, 128, 0.35));
        background: var(--vscode-editorWidget-background, #252526);
        box-shadow: 0 4px 14px rgba(0, 0, 0, 0.35);
        font-weight: normal;
        white-space: nowrap;
    }
    /* The flyout hangs off a header cell, so it sits inside .grid and inherits the rule that
       stretches every input in the table to fill its cell. On a checkbox that reserves the whole
       row and pushes the language name out past the flyout's edge - which is how this ended up
       looking like a column of checkboxes with the names floating over the dock beside them.
       Anything else put in the header will need the same undoing. */
    .column-flyout input[type=checkbox] {
        width: auto;
        padding: 0;
        border: none;
        background: initial;
    }
    .column-flyout input[type=checkbox]:hover,
    .column-flyout input[type=checkbox]:focus { border: none; }

    .column-flyout-title {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        opacity: 0.65;
    }

    /* Sortable headers read as controls, so the affordance is visible before the click rather than
       being something you have to guess at. */
    .head-row .sortable {
        display: flex;
        align-items: center;
        gap: 4px;
        cursor: pointer;
        user-select: none;
    }

    .head-row .sortable:hover { background: var(--vscode-list-hoverBackground, #2a2d2e); }

    .head-row .sortable:focus-visible {
        outline: 1px solid var(--vscode-focusBorder, #007fd4);
        outline-offset: -1px;
    }

    /* The sorted column is marked by its arrow, not by recolouring the label. An earlier version
       used the list *selection* foreground here, which assumes the dark accent background a
       selected row has - on a plain header in a light theme that is white on near-white. */
    .head-row .sorted { font-weight: 700; }

    .head-row .codicon { font-size: 12px; opacity: 0.6; }
    .head-row .sorted .codicon { opacity: 1; }

    .head-row .head-label { overflow: hidden; text-overflow: ellipsis; }

    .rows { position: relative; }

    .data-row {
        display: grid;
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
        align-items: center;
    }

    .data-row:hover { background: var(--vscode-list-hoverBackground, #2a2d2e); }
    /* Rows a validation run had something to say about, tinted by how bad it was. A single red
       outline for everything made a heading with no entries look as broken as a key collision.
       Valid rows are left alone deliberately - tinting them all would say nothing.

       The accent bar is an inset shadow rather than a background, so it survives selection: the
       selected row still shows what is wrong with it. */
    .data-row.row-sev-error {
        background: color-mix(in srgb, var(--vscode-errorForeground, #f48771) 13%, transparent);
        box-shadow: inset 3px 0 0 var(--vscode-errorForeground, #f48771);
    }
    .data-row.row-sev-warning {
        background: color-mix(in srgb, var(--vscode-charts-yellow, #cca700) 13%, transparent);
        box-shadow: inset 3px 0 0 var(--vscode-charts-yellow, #cca700);
    }
    .data-row.row-sev-info {
        background: color-mix(in srgb, var(--vscode-charts-blue, #3794ff) 11%, transparent);
        box-shadow: inset 3px 0 0 var(--vscode-charts-blue, #3794ff);
    }
    .data-row.selected { background: var(--vscode-list-inactiveSelectionBackground, #37373d); }

    .cell { min-width: 0; padding: 0 4px; }

    /* Sits flush in the cell like the text inputs, so a credits row does not look like a form. */
    select.directive {
        width: 100%;
        border: 1px solid transparent;
        background: transparent;
        color: inherit;
        font: inherit;
        padding: 2px 4px;
    }
    select.directive:hover { border-color: var(--vscode-panel-border, #444); }
    select.directive:focus {
        outline: none;
        border-color: var(--vscode-focusBorder, #007fd4);
        background: var(--vscode-input-background, #3c3c3c);
    }

    .grid input {
        width: 100%;
        border: 1px solid transparent;
        background: transparent;
        color: inherit;
        font: inherit;
        padding: 2px 4px;
    }
    .grid input:hover { border-color: var(--vscode-panel-border, #444); }
    .grid input:focus {
        outline: none;
        border-color: var(--vscode-focusBorder, #007fd4);
        background: var(--vscode-input-background, #3c3c3c);
    }

    .dock {
        position: relative;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        border-left: 1px solid var(--vscode-panel-border, #444);
        background: var(--vscode-sideBar-background, #252526);
    }

    .resize-handle {
        position: absolute;
        left: -3px;
        top: 0;
        bottom: 0;
        width: 6px;
        cursor: ew-resize;
    }
    .resize-handle:hover { background: var(--vscode-sash-hoverBorder, #007fd4); }

    .dock-header {
        position: relative;
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 6px;
        padding: 6px 8px;
        min-height: 34px;
        border-bottom: 1px solid var(--vscode-panel-border, #444);
    }
    .dock-header .header-left { position: absolute; left: 8px; }
    .dock-header .header-right { position: absolute; right: 8px; }

    .dock-content { flex: 1; min-height: 0; overflow-y: auto; padding: 8px; display: flex; flex-direction: column; gap: 8px; }

    /* Filters sit at the foot of the dock, as they do in the story graph editor - the header is for
       acting on the file, the foot for narrowing what you are looking at. */
    ${dockBodyCss}
    ${dockOverviewCss}

    /* Header controls, matching the story graph editor: soft icon buttons with almost no chrome
       until hovered, the glyph doing the talking. */
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
    .dock-header .icon-btn { font-size: 18px; padding: 6px 9px; }
    .dock-header .icon-btn .codicon { font-size: 18px; }

    /* Validate is an always-present soft pill, tinted with a hue of its own state colour. */
    .validate-btn { border-radius: 14px; font-weight: 600; }
    .validate-btn.sev-unvalidated {
        background: color-mix(in srgb, var(--vscode-foreground, #ccc) 15%, transparent);
    }
    .validate-btn.sev-ok {
        color: var(--vscode-charts-green, #89d185);
        background: color-mix(in srgb, var(--vscode-charts-green, #89d185) 14%, transparent);
        opacity: 1;
    }
    .validate-btn.sev-info {
        color: var(--vscode-charts-blue, #3794ff);
        background: color-mix(in srgb, var(--vscode-charts-blue, #3794ff) 14%, transparent);
        opacity: 1;
    }
    .validate-btn.sev-warning {
        color: var(--vscode-charts-yellow, #cca700);
        background: color-mix(in srgb, var(--vscode-charts-yellow, #cca700) 16%, transparent);
        opacity: 1;
    }
    .validate-btn.sev-error {
        color: var(--vscode-errorForeground, #f14c4c);
        background: color-mix(in srgb, var(--vscode-errorForeground, #f14c4c) 16%, transparent);
        opacity: 1;
    }
    .validate-btn:hover { filter: brightness(1.2); }

    button {
        background: var(--vscode-button-secondaryBackground, var(--vscode-button-background, #3a3d41));
        color: var(--vscode-button-secondaryForeground, var(--vscode-button-foreground, #ccc));
        border: none;
        padding: 3px 8px;
        cursor: pointer;
        font-size: var(--vscode-font-size, 13px);
        font-family: var(--vscode-font-family, sans-serif);
        white-space: nowrap;
        flex-shrink: 0;
    }
    button:hover {
        background: var(--vscode-button-secondaryHoverBackground, var(--vscode-button-hoverBackground));
    }
    button.primary {
        background: var(--vscode-button-background, #0e639c);
        color: var(--vscode-button-foreground, #fff);
    }
    button.danger { color: var(--vscode-errorForeground, #f44); }
    button:hover:not(:disabled) { background: var(--vscode-toolbar-hoverBackground, #4a4d51); }
    button:disabled { opacity: 0.5; cursor: default; }

    button.primary {
        background: var(--vscode-button-background, #0e639c);
        color: var(--vscode-button-foreground, #fff);
    }

    button.validate.sev-ok { color: var(--vscode-charts-green, #89d185); }
    button.validate.sev-info { color: var(--vscode-charts-blue, #3794ff); }
    button.validate.sev-warning { color: var(--vscode-charts-yellow, #cca700); }
    button.validate.sev-error { color: var(--vscode-errorForeground, #f48771); }

    /* Inputs, selects and the filter row are the story graph editor's rules verbatim, so the two
       editors' docks are the same control set rather than two that merely resemble each other. */
    select, input[type=text] {
        background: var(--vscode-input-background, #3c3c3c);
        color: var(--vscode-input-foreground, #ccc);
        border: 1px solid var(--vscode-input-border, transparent);
        padding: 2px 6px;
        font-size: var(--vscode-font-size, 13px);
        font-family: var(--vscode-font-family, sans-serif);
        outline: none;
        min-width: 0;
    }
    input[type=text]:focus, select:focus {
        border-color: var(--vscode-focusBorder, #007fd4);
    }

    /* The search-mode toggles: icon buttons, the same ones the graph editor's overview tools use. */
    .mode-group { display: flex; gap: 4px; }

    .row-menu {
        position: fixed;
        z-index: 20;
        min-width: 200px;
        display: flex;
        flex-direction: column;
        padding: 4px 0;
        background: var(--vscode-menu-background, var(--vscode-editorWidget-background, #252526));
        color: var(--vscode-menu-foreground, #ccc);
        border: 1px solid var(--vscode-menu-border, var(--vscode-panel-border, #454545));
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
    }

    .row-menu button {
        background: transparent;
        color: inherit;
        text-align: left;
        padding: 4px 12px;
        border: none;
    }
    .row-menu button:hover:not(:disabled) {
        background: var(--vscode-menu-selectionBackground, #04395e);
        color: var(--vscode-menu-selectionForeground, #fff);
    }
    .row-menu button.danger:hover:not(:disabled) {
        background: var(--vscode-inputValidation-errorBackground, #5a1d1d);
    }
    .row-menu .sep {
        height: 1px;
        margin: 4px 0;
        background: var(--vscode-menu-separatorBackground, #454545);
    }

    /* A spacer is a marker, not content: it spans the row, reads as a rule, and cannot be typed
       into - only removed from the row menu. */
    .spacer-cell {
        grid-column: 1 / -1;
        display: flex;
        align-items: center;
        gap: 8px;
        opacity: 0.5;
        cursor: default;
        user-select: none;
    }
    .spacer-cell:focus { outline: 1px solid var(--vscode-focusBorder, #007fd4); }
    .spacer-rule { flex: 1; height: 1px; background: currentColor; opacity: 0.5; }
    .spacer-label { font-size: 0.85em; font-style: italic; white-space: nowrap; }

    /* A row that only repeats the layer below is shown dimmed and italic, so when they are on
       screen it stays obvious which lines this file actually changes. */
    .data-row.inherited input, .data-row.inherited select {
        color: var(--vscode-disabledForeground, #888);
        font-style: italic;
    }

    .inherited-toggle { display: flex; align-items: center; gap: 6px; cursor: pointer; }
    .inherited-toggle input { cursor: pointer; }
    .inherited-toggle .badge {
        opacity: 0.7;
        font-size: 0.9em;
        background: var(--vscode-badge-background, #4d4d4d);
        color: var(--vscode-badge-foreground, #fff);
        border-radius: 8px;
        padding: 0 6px;
    }

    .counts { opacity: 0.75; display: flex; flex-direction: column; gap: 2px; }
    /* A duplicate key in a keyed file is an error the batch validator reports, so it is not just
       another statistic. */
    .counts .warn { color: var(--vscode-charts-yellow, #cca700); opacity: 1; }
    .actions { display: flex; gap: 6px; flex-wrap: wrap; }

    .problems { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
    .problems li { border-left: 2px solid transparent; padding-left: 6px; }
    .problems li.sev-error { border-left-color: var(--vscode-errorForeground, #f48771); }
    .problems li.sev-warning { border-left-color: var(--vscode-charts-yellow, #cca700); }
    .problems li.sev-info { border-left-color: var(--vscode-charts-blue, #3794ff); }

    /* ── Add-translation dialog content ──────────────────────────────────────
       Only what this dialog's own content needs. Its box, backdrop, title bar and resize handles
       are the shared .modal base in dockChrome - it used to carry a second copy of all of that,
       which is how the two dialogs ended up with different padding, radius and shadow. */
    .field { display: flex; flex-direction: column; gap: 3px; }

    .field > span { opacity: 0.85; font-size: 0.9em; }

    .field input {
        background: var(--vscode-input-background, #3c3c3c);
        color: var(--vscode-input-foreground, #ccc);
        border: 1px solid var(--vscode-input-border, transparent);
        padding: 4px 6px;
        font: inherit;
    }

    .field input:focus {
        outline: 1px solid var(--vscode-focusBorder, #007fd4);
        outline-offset: -1px;
    }

    .field input[aria-invalid='true'] {
        border-color: var(--vscode-inputValidation-errorBorder, #be1100);
    }

    .field-error {
        margin: -4px 0 0;
        color: var(--vscode-inputValidation-errorForeground, var(--vscode-errorForeground, #f48771));
        font-size: 0.9em;
    }

    /* Flows; it does not scroll on its own. The dialog body is the scroller, and a list that kept
       its own cap went on scrolling however tall the dialog was made - the height went to dead space
       underneath while the languages stayed behind a scrollbar. One scroller per dialog. */
    .languages {
        display: flex;
        flex-direction: column;
        gap: 8px;
        padding-top: 4px;
    }

    .hint { margin: 0; opacity: 0.6; font-size: 0.9em; }

    /* The one list that does keep a cap: it appears under the key field while typing, and a broad
       prefix would otherwise shove the rest of the form down the moment a letter is deleted. Capped
       in px rather than vh so it is a number of rows, not a fraction of whatever window you happen
       to be in. */
    .suggestions {
        list-style: none;
        margin: -4px 0 0;
        padding: 0;
        max-height: 180px;
        overflow-y: auto;
        border: 1px solid var(--vscode-panel-border, #444);
    }

    .suggestions button {
        display: flex;
        justify-content: space-between;
        gap: 12px;
        width: 100%;
        padding: 3px 6px;
        background: none;
        border: none;
        color: inherit;
        font: inherit;
        text-align: left;
        cursor: pointer;
    }

    .suggestions button:hover, .suggestions button:focus-visible {
        background: var(--vscode-list-hoverBackground, #2a2d2e);
        outline: none;
    }

    .suggestion-key { white-space: nowrap; }

    .suggestion-value {
        opacity: 0.6;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .dialog-actions { display: flex; justify-content: flex-end; gap: 6px; }

    .dialog-actions button {
        background: var(--vscode-button-secondaryBackground, #3a3d41);
        color: var(--vscode-button-secondaryForeground, #ccc);
        border: none;
        padding: 4px 14px;
        font: inherit;
        cursor: pointer;
    }

    .dialog-actions .primary {
        background: var(--vscode-button-background, #0e639c);
        color: var(--vscode-button-foreground, #fff);
    }

    .dialog-actions button:disabled { opacity: 0.5; cursor: default; }
`;
