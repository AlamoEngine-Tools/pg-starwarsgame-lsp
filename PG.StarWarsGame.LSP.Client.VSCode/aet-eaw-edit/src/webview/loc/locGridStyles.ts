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

import styled from "styled-components";

import {
    dockBodyCss,
    dockChromeCss,
    dockHeaderCss,
    problemsPanelCss,
    rightDockCss,
    pillSelectCss,
    searchPlateCss,
} from "../shared/dockChrome";

export const Shell = styled.div`
    ${dockChromeCss}
    ${rightDockCss} /* 100% of the host's element, not 100vh: viewport units ignore the body's own box, so any
       margin or padding the host applies would make this overflow by exactly that much. */
    height: 100%;
    display: flex;
    flex-direction: column;
    /* Containing block for the crawl preview, which covers the whole editor. */
    position: relative;
    color: var(--colour-ink);
    background: var(--colour-editor-ground);
    font-family: var(--font-family-ui);
    font-size: var(--font-size-body);

    /* Every control here is sized with width:100% inside a track or a column, and each also carries
       padding and a border. Without this they come out wider than what holds them - which is what
       put a horizontal scrollbar under a table whose columns fitted perfectly well. */

    input,
    select,
    textarea,
    button {
        box-sizing: border-box;
    }

    .message {
        padding: var(--space-12);
    }

    .body {
        flex: 1;
        display: flex;
        min-height: 0;
    }

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

    /* The grid and the search plate on its corner. The grid scrolls inside it and the plate does
       not: a plate inside the scroller would leave with the rows. */

    .grid-frame {
        flex: 1;
        position: relative;
        display: flex;
        flex-direction: column;
        min-height: 0;
    }

    /* Clear of the grid's scroll bars, which run along the same two edges. */

    .grid-frame > .search-plate.floating {
        right: var(--space-16);
        bottom: var(--space-16);
    }

    .grid-area {
        flex: 1;
        overflow: auto;
        position: relative;
        /* Room past the last row, so it can be scrolled out from under the plate. */
        padding-bottom: calc(var(--space-24) * 2);
    }

    /* Where a dragged row will land. Pinned to the viewport rather than to the grid, so it stays on
       the boundary while the list scrolls under it. */

    .drop-indicator {
        position: fixed;
        left: 0;
        right: 0;
        height: 2px;
        background: var(--colour-focus);
        pointer-events: none;
        z-index: 15;
    }

    .step-help {
        margin: 0;
        font-size: var(--font-size-smaller);
        opacity: 0.6;
        line-height: 1.35;
    }

    /* Why the file on screen is not edited, in the dock header between Save and the validation
       tag. Held to the room the two leave, so a longer reason is cut short and never pushes them. */

    .dock-header .read-only-note {
        display: inline-flex;
        align-items: center;
        gap: var(--space-4);
        max-width: calc(100% - 112px);
        overflow: hidden;
        white-space: nowrap;
        text-overflow: ellipsis;
        font-size: var(--font-size-smaller);
        color: var(--colour-muted);
    }

    /* Validation results, across the foot of the table rather than down the dock - the same shape
       the story graph editor uses. */

    .loc-problems {
        position: relative;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        min-height: 0;
        border-top: var(--space-1) solid var(--colour-line);
        background: var(--colour-sidebar);
        font-size: var(--font-size-12);
    }

    ${problemsPanelCss}
    .problem-target {
        flex-shrink: 0;
        max-width: 220px;
        color: var(--colour-muted);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .grid-footer {
        flex-shrink: 0;
        padding: var(--space-2) var(--space-8);
        border-top: var(--space-1) solid var(--colour-line);
        background: var(--colour-panel);
        font-size: var(--font-size-smaller);
        opacity: 0.8;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .grid {
        display: block;
    }

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
        background: var(--colour-panel);
        border-bottom: var(--space-1) solid var(--colour-line);
        font-weight: 600;
    }

    .head-row .cell {
        padding: var(--space-4) var(--space-8);
        white-space: nowrap;
    }

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
           past its edge - and this is anchored to the rightmost cell. Placed from the gear's viewport
           box when it opens. */
        position: fixed;
        z-index: 25;
        min-width: 160px;
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
        padding: var(--space-8) var(--space-8);
        border-radius: var(--radius-6);
        border: var(--space-1) solid var(--colour-line-strong);
        background: var(--colour-panel);
        box-shadow: 0 4px 14px rgba(0, 0, 0, 0.35);
        font-weight: normal;
        white-space: nowrap;
    }

    /* The flyout is anchored to a header cell, so it sits inside .grid and inherits the rule that
       stretches every input in the table to fill its cell. On a checkbox that reserves the whole
       row and pushes the language name out past the flyout's edge - which is how this ended up
       looking like a column of checkboxes with the names floating over the dock beside them.
       Anything else put in the header will need the same undoing. */

    .column-flyout input[type="checkbox"] {
        width: auto;
        padding: 0;
        border: none;
        background: initial;
    }

    .column-flyout input[type="checkbox"]:hover,
    .column-flyout input[type="checkbox"]:focus {
        border: none;
    }

    .column-flyout-title {
        display: flex;
        align-items: center;
        gap: var(--space-8);
        font-size: var(--font-size-11);
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
        gap: var(--space-4);
        cursor: pointer;
        user-select: none;
    }

    .head-row .sortable:hover {
        background: var(--colour-hover);
    }

    .head-row .sortable:focus-visible {
        outline: var(--space-1) solid var(--colour-focus);
        outline-offset: -1px;
    }

    /* The sorted column is marked by its arrow, not by recolouring the label. An earlier version
       used the list *selection* foreground here, which assumes the dark accent background a
       selected row has - on a plain header in a light theme that is white on near-white. */

    .head-row .sorted {
        font-weight: 700;
    }

    .head-row svg {
        width: var(--icon-size-12);
        height: var(--icon-size-12);
        opacity: 0.6;
    }

    .head-row .sorted svg {
        opacity: 1;
    }

    .head-row .head-label {
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .rows {
        position: relative;
    }

    .data-row {
        display: grid;
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
        align-items: center;
    }

    .data-row:hover {
        background: var(--colour-hover);
    }

    /* Rows a validation run had something to say about, tinted by how bad it was. A single red
       outline for everything made a heading with no entries look as broken as a key collision.
       Valid rows are left alone deliberately - tinting them all would say nothing.

       The accent bar is an inset shadow rather than a background, so it survives selection: the
       selected row still shows what is wrong with it. */

    .data-row.row-sev-error {
        background: color-mix(in srgb, var(--colour-critical) 13%, transparent);
        box-shadow: inset var(--severity-stripe) 0 0 var(--colour-critical);
    }

    .data-row.row-sev-warning {
        background: color-mix(
            in srgb,
            var(--colour-data-yellow) 13%,
            transparent
        );
        box-shadow: inset var(--severity-stripe) 0 0 var(--colour-data-yellow);
    }

    .data-row.row-sev-info {
        background: color-mix(
            in srgb,
            var(--colour-data-blue) 11%,
            transparent
        );
        box-shadow: inset var(--severity-stripe) 0 0 var(--colour-data-blue);
    }

    .data-row.selected {
        background: var(--colour-selected-inactive);
    }

    .cell {
        min-width: 0;
        padding: 0 var(--space-4);
    }

    /* Sits flush in the cell like the text inputs, so a credits row does not look like a form. */

    select.directive {
        width: 100%;
        border: var(--space-1) solid transparent;
        background: transparent;
        color: inherit;
        font: inherit;
        padding: var(--space-2) var(--space-4);
    }

    select.directive:hover {
        border-color: var(--colour-line);
    }

    select.directive:focus {
        outline: none;
        border-color: var(--colour-focus);
        background: var(--colour-input);
    }

    .grid input {
        width: 100%;
        border: var(--space-1) solid transparent;
        background: transparent;
        color: inherit;
        font: inherit;
        padding: var(--space-2) var(--space-4);
    }

    .grid input:hover {
        border-color: var(--colour-line);
    }

    .grid input:focus {
        outline: none;
        border-color: var(--colour-focus);
        background: var(--colour-input);
    }

    ${dockHeaderCss}
    .dock-content {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
        padding: var(--space-8);
        display: flex;
        flex-direction: column;
        gap: var(--space-8);
    }

    /* Filters sit at the foot of the dock, as they do in the story graph editor - the header is for
       acting on the file, the foot for narrowing what you are looking at. */

    ${dockBodyCss}
    ${pillSelectCss}
    ${searchPlateCss}
    button {
        background: var(--colour-action-2);
        color: var(--colour-action-2-ink);
        border: none;
        padding: var(--space-2) var(--space-8);
        cursor: pointer;
        font-size: var(--font-size-body);
        font-family: var(--font-family-ui);
        white-space: nowrap;
        flex-shrink: 0;
    }

    button:hover {
        background: var(--colour-action-2-hover);
    }

    button.primary {
        background: var(--colour-action);
        color: var(--colour-action-ink);
    }

    button.danger {
        color: var(--colour-critical);
    }

    button:hover:not(:disabled) {
        background: var(--colour-toolbar-hover);
    }

    button:disabled {
        opacity: 0.5;
        cursor: default;
    }

    button.primary {
        background: var(--colour-action);
        color: var(--colour-action-ink);
    }

    button.validate.sev-ok {
        color: var(--colour-data-green);
    }

    button.validate.sev-info {
        color: var(--colour-data-blue);
    }

    button.validate.sev-warning {
        color: var(--colour-data-yellow);
    }

    button.validate.sev-error {
        color: var(--colour-critical);
    }

    /* Inputs, selects and the filter row are the story graph editor's rules verbatim, so the two
       editors' docks are the same control set rather than two that merely resemble each other. */

    select,
    input[type="text"] {
        background: var(--colour-input);
        color: var(--colour-input-ink);
        border: var(--space-1) solid var(--colour-input-line);
        padding: var(--space-2) var(--space-6);
        font-size: var(--font-size-body);
        font-family: var(--font-family-ui);
        outline: none;
        min-width: 0;
    }

    input[type="text"]:focus,
    select:focus {
        border-color: var(--colour-focus);
    }

    /* A spacer is a marker, not content: it spans the row, reads as a rule, and cannot be typed
       into - only removed from the row menu. */

    .spacer-cell {
        grid-column: 1 / -1;
        display: flex;
        align-items: center;
        gap: var(--space-8);
        opacity: 0.5;
        cursor: default;
        user-select: none;
    }

    .spacer-cell:focus {
        outline: var(--space-1) solid var(--colour-focus);
    }

    .spacer-rule {
        flex: 1;
        height: 1px;
        background: currentColor;
        opacity: 0.5;
    }

    .spacer-label {
        font-size: var(--font-size-smaller);
        font-style: italic;
        white-space: nowrap;
    }

    /* A row that only repeats the layer below is shown dimmed and italic, so when they are on
       screen it stays obvious which lines this file actually changes. */

    .data-row.inherited input,
    .data-row.inherited select {
        color: var(--colour-faint);
        font-style: italic;
    }

    .actions {
        display: flex;
        gap: var(--space-6);
        flex-wrap: wrap;
    }

    .problems {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
    }

    .problems li {
        border-left: 2px solid transparent;
        padding-left: var(--space-6);
    }

    .problems li.sev-error {
        border-left-color: var(--colour-critical);
    }

    .problems li.sev-warning {
        border-left-color: var(--colour-data-yellow);
    }

    .problems li.sev-info {
        border-left-color: var(--colour-data-blue);
    }

    /* ── Add-translation dialog content ──────────────────────────────────────
       Only what this dialog's own content needs. Its box, backdrop, title bar and resize handles
       are the shared .modal base in dockChrome - it used to carry a second copy of all of that,
       which is how the two dialogs ended up with different padding, radius and shadow. */

    .field {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
    }

    .field > span {
        opacity: 0.85;
        font-size: var(--font-size-smaller);
    }

    .field input {
        background: var(--colour-input);
        color: var(--colour-input-ink);
        border: var(--space-1) solid var(--colour-input-line);
        padding: var(--space-4) var(--space-6);
        font: inherit;
    }

    .field input:focus {
        outline: var(--space-1) solid var(--colour-focus);
        outline-offset: -1px;
    }

    .field input[aria-invalid="true"] {
        border-color: var(--colour-invalid-line);
    }

    .field-error {
        margin: -4px 0 0;
        color: var(--colour-invalid-ink);
        font-size: var(--font-size-smaller);
    }

    /* Flows; it does not scroll on its own. The dialog body is the scroller, and a list that kept
       its own cap went on scrolling however tall the dialog was made - the height went to dead space
       underneath while the languages stayed behind a scrollbar. One scroller per dialog. */

    .languages {
        display: flex;
        flex-direction: column;
        gap: var(--space-8);
        padding-top: var(--space-4);
    }

    .hint {
        margin: 0;
        opacity: 0.6;
        font-size: var(--font-size-smaller);
    }

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
        border: var(--space-1) solid var(--colour-line);
    }

    .suggestions button {
        display: flex;
        justify-content: space-between;
        gap: var(--space-12);
        width: 100%;
        padding: var(--space-2) var(--space-6);
        background: none;
        border: none;
        color: inherit;
        font: inherit;
        text-align: left;
        cursor: pointer;
    }

    .suggestions button:hover,
    .suggestions button:focus-visible {
        background: var(--colour-hover);
        outline: none;
    }

    .suggestion-key {
        white-space: nowrap;
    }

    .suggestion-value {
        opacity: 0.6;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .dialog-actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-6);
    }

    .dialog-actions button {
        background: var(--colour-action-2);
        color: var(--colour-action-2-ink);
        border: none;
        padding: var(--space-4) var(--space-12);
        font: inherit;
        cursor: pointer;
    }

    .dialog-actions .primary {
        background: var(--colour-action);
        color: var(--colour-action-ink);
    }

    .dialog-actions button:disabled {
        opacity: 0.5;
        cursor: default;
    }
`;
