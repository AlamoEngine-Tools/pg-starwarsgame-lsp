// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The parts of a localisation editor that are the same whatever the file holds: a virtualised grid
// on one shared column template, a sticky header, and a resizable right-hand dock.
//
// It deliberately knows nothing about what a row means - not what its key is for, not whether its
// order matters, not whether it can be blank. Everything semantic is supplied by the editor above
// it. That is the whole point: a single component that branched on the kind of file is what put
// credits behaviour into translation files three separate times.

import { ReactNode, useEffect, useRef } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';

import { LocRow } from '../loc/locRow';
import { useEdgeResize } from '../useEdgeResize';
import { Shell } from './locGridStyles';

const ROW_HEIGHT = 26;

// Module-level so a remount (the webview reloads when the tab is restored) keeps the dock width.
let dockWidthMemo = 300;

export interface LocGridShellProps {
    /** The rows to draw, already filtered and ordered by the editor. */
    rows: LocRow[];
    /** Grid template shared by the header and every row, so the columns cannot drift apart. */
    columns: string;

    header: ReactNode;
    renderRow: (row: LocRow) => ReactNode;

    /** Extra classes for a row - problem, selection, and anything the editor marks. */
    rowClassName?: (row: LocRow) => string;
    rowTitle?: (row: LocRow) => string | undefined;
    onRowFocus?: (row: LocRow) => void;
    onRowContextMenu?: (row: LocRow, event: React.MouseEvent) => void;

    /**
     * Document index of a row to scroll to and focus, or null. Set after an insert: a row the user
     * cannot see is not much of an insert. Cleared through {@link onFocused}.
     */
    focusRowIndex?: number | null;
    onFocused?: () => void;

    /** Menus, dialogs and previews, drawn over the whole editor. */
    overlays?: ReactNode;
    /** A strip under the table describing what it is showing. */
    footer?: ReactNode;
    /** Validation results, between the table and its footer. */
    problemsBar?: ReactNode;
    dockHeader: ReactNode;
    dockContent: ReactNode;
    /** Filters, at the foot of the dock - the story graph editor puts its there too. */
    dockOverview?: ReactNode;
    /** Right-clicking the empty area below the rows, so an empty file is still reachable. */
    onEmptyAreaContextMenu?: (event: React.MouseEvent) => void;

    /**
     * Dragging something onto the table. The shell only forwards the events and draws the line -
     * what is being dropped, and what it means, is the editor's business.
     */
    onGridDragOver?: (event: React.DragEvent) => void;
    onGridDrop?: (event: React.DragEvent) => void;
    onGridDragLeave?: (event: React.DragEvent) => void;
    /** Viewport Y of the boundary a drop would land on, or null to draw nothing. */
    dropIndicatorY?: number | null;
}

export function LocGridShell(props: LocGridShellProps): React.JSX.Element {
    const scrollRef = useRef<HTMLDivElement>(null);

    const virtualizer = useVirtualizer({
        count: props.rows.length,
        getScrollElement: () => scrollRef.current,
        estimateSize: () => ROW_HEIGHT,
        overscan: 12,
    });

    const { size: dockWidth, handleProps } = useEdgeResize(
        dockWidthMemo, 210, 520, 'w', v => { dockWidthMemo = v; });

    const { focusRowIndex, onFocused, rows } = props;

    // Takes the user to a freshly inserted row. Two steps, because the grid is virtualised: the row
    // has to be scrolled into range before it exists in the DOM to be focused.
    useEffect(() => {
        if (focusRowIndex === null || focusRowIndex === undefined) { return; }

        const position = rows.findIndex(r => r.index === focusRowIndex);
        if (position < 0) { return; }

        // 'auto' scrolls only when the row is out of view, and then by the least amount. Centring
        // would yank the viewport on every insert, so a row inserted next to one you can already
        // see would jump away from the pointer that asked for it.
        virtualizer.scrollToIndex(position, { align: 'auto' });

        const timer = window.setTimeout(() => {
            const row = scrollRef.current?.querySelector(`[data-row-index="${focusRowIndex}"]`);
            const field = row?.querySelector<HTMLElement>('input, select');
            field?.focus();
            if (field instanceof HTMLInputElement) { field.select(); }
            onFocused?.();
        }, 0);

        return () => window.clearTimeout(timer);
    }, [focusRowIndex, onFocused, rows, virtualizer]);

    return (
        <Shell>
            {props.overlays}
            <div className="body">
                {/* The table and its footer share a column so the footer stays put while the grid
                    scrolls under it. */}
                <div className="grid-column">
                <div
                    className="grid-area"
                    ref={scrollRef}
                    onDragOver={props.onGridDragOver}
                    onDrop={props.onGridDrop}
                    onDragLeave={props.onGridDragLeave}
                    onContextMenu={e => {
                        // Only when the click missed every row: a file with nothing in it has no
                        // row to right-click, and the menu is the only way to add one.
                        if ((e.target as HTMLElement).closest('.data-row') === null) {
                            props.onEmptyAreaContextMenu?.(e);
                        }
                    }}
                >
                    {/* Divs on one shared grid template rather than a table: a <table> whose tbody
                        is display:block and whose rows are absolutely positioned cannot keep its
                        header and body columns aligned, and the row/cell display overrides fight
                        the table layout algorithm. One template string drives both. */}
                    {/* min-content, not fit-content: the columns' own minimums decide how narrow
                        the table may get, not whatever text happens to be in the rows currently
                        mounted. With a virtualised list fit-content made the width depend on the
                        scroll position. */}
                    <div className="grid" style={{ minWidth: 'min-content' }}>
                        <div className="head-row" style={{ gridTemplateColumns: props.columns }}>
                            {props.header}
                        </div>

                        <div className="rows" style={{ height: `${virtualizer.getTotalSize()}px` }}>
                            {virtualizer.getVirtualItems().map(item => {
                                const row = props.rows[item.index];
                                return (
                                    <div
                                        key={item.key}
                                        data-row-index={row.index}
                                        className={['data-row', props.rowClassName?.(row) ?? '']
                                            .filter(Boolean).join(' ')}
                                        title={props.rowTitle?.(row)}
                                        onFocusCapture={() => props.onRowFocus?.(row)}
                                        onContextMenu={e => props.onRowContextMenu?.(row, e)}
                                        style={{
                                            gridTemplateColumns: props.columns,
                                            transform: `translateY(${item.start}px)`,
                                            height: `${ROW_HEIGHT}px`,
                                        }}
                                    >
                                        {props.renderRow(row)}
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                </div>
                {/* Fixed to the viewport, because the boundary it marks is reported in viewport
                    coordinates and the grid underneath it scrolls. */}
                {props.dropIndicatorY !== null && props.dropIndicatorY !== undefined && (
                    <div className="drop-indicator" style={{ top: props.dropIndicatorY }} />
                )}
                {props.problemsBar}
                {props.footer && <div className="grid-footer">{props.footer}</div>}
                </div>

                <div className="dock" style={{ width: `${dockWidth}px` }}>
                    <div className="resize-handle" {...handleProps} />
                    <div className="dock-header">{props.dockHeader}</div>
                    <div className="dock-content">{props.dockContent}</div>
                    {props.dockOverview && (
                        <div className="dock-overview">{props.dockOverview}</div>
                    )}
                </div>
            </div>
        </Shell>
    );
}

/** A message in place of the grid - an error, or the moment before the first rows arrive. */
export function LocGridMessage(props: { children: ReactNode }): React.JSX.Element {
    return <Shell><p className="message">{props.children}</p></Shell>;
}
