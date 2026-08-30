// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The bulk-geometry tables: which one to read, and the rows themselves.
//
// A TAB STRIP over a box, rather than the mode selector and previous/next pair this used to be.
// That arrangement was built for a 320px flyout, where a hundred rows at a time was all that would
// fit; the inspector is its own editor tab now and has a page's worth of room, so the tables are
// read the way a table is read - one long list you scroll.
//
// Paging did not go away, it stopped being the reader's problem: the server caps a request at 500
// rows and a Star Destroyer sub-mesh is 3814 faces, so the next page is fetched as the scroller
// nears its end. `geometryRows` holds what has arrived and decides when to ask.

import { useEffect, useRef } from 'react';

import { geometryTabs, nearEnd, nextOffset, type GeometryRows } from '../preview/geometryRows';
import { type GeometryTable } from '../../protocol/modelPreview';

/**
 * Which table is being read.
 *
 * A real tablist: the three are one choice, and the box below is the chosen one's panel. Written as
 * three independent switches first, which told a screen reader the wrong thing.
 */
export function GeometryTabs(
    { rows, disabled, onSelect }: {
        /** What is in hand, so the open tab can carry its row count. */
        rows: GeometryRows | null;
        /** No mesh index means nothing can be fetched, so the choice is shown but inert. */
        disabled: boolean;
        onSelect: (table: GeometryTable) => void;
    },
): React.JSX.Element {
    return (
        <div className="geometry-tabs" role="tablist" aria-label="Which table to read">
            {geometryTabs(rows).map(tab => {
                const open = rows?.table === tab.id;

                return (
                    <button
                        key={tab.id}
                        type="button"
                        role="tab"
                        className={'geometry-tab' + (open ? ' open' : '')}
                        aria-selected={open}
                        disabled={disabled}
                        title={tab.title}
                        onClick={() => onSelect(tab.id)}
                    >
                        {tab.label}
                        {tab.count !== null && (
                            <span className="section-count">{tab.count.toLocaleString()}</span>
                        )}
                    </button>
                );
            })}
        </div>
    );
}

/**
 * The open table, filling its box and scrolling.
 *
 * The scroller owns the fetching. A row count is not something the reader should have to drive, and
 * the previous/next pair that used to sit under this made them drive it forty times to read one
 * Star Destroyer sub-mesh.
 */
export function GeometryRowsTable(
    { rows, onMore }: {
        rows: GeometryRows;
        /** Asked for the next offset. Called only while there is more to come. */
        onMore: (table: GeometryTable, offset: number) => void;
    },
): React.JSX.Element {
    const box = useRef<HTMLDivElement>(null);

    // Asked for at most once per arrival: `rows` changes when a page lands, so the effect runs
    // again with the new length and decides afresh. Without the effect a table shorter than its own
    // box would stall - no scrolling can happen there, so a scroll handler alone never fires.
    useEffect(() => {
        const view = box.current;
        const offset = nextOffset(rows);

        if (view === null || offset === null) {
            return;
        }

        if (nearEnd(view)) {
            onMore(rows.table, offset);
        }
    }, [rows, onMore]);

    const onScroll = (): void => {
        const view = box.current;
        const offset = nextOffset(rows);

        if (view !== null && offset !== null && nearEnd(view)) {
            onMore(rows.table, offset);
        }
    };

    // An empty table that is empty for a REASON says so instead of drawing a bare grid.
    if (rows.rows.length === 0 && rows.note !== undefined) {
        return <div className="field-note">{rows.note}</div>;
    }

    return (
        <div className="geometry-panel" role="tabpanel">
            {/* Scrolls in BOTH directions inside itself: a vertex row is ten columns wide, and the
                list is however long the sub-mesh is. Either one pushing its host out of shape is
                what put this behind a pager in the first place. */}
            <div className="geometry-scroll" ref={box} onScroll={onScroll}>
                <table className="geometry-table">
                    <thead>
                        <tr>{rows.columns.map(column => <th key={column}>{column}</th>)}</tr>
                    </thead>
                    <tbody>
                        {rows.rows.map((row, at) => (
                            <tr key={at}>{row.map((cell, col) => <td key={col}>{cell}</td>)}</tr>
                        ))}
                    </tbody>
                </table>
            </div>

            {/* What is in hand out of what there is. The rows arrive in pages either way, and a
                reader several thousand rows down a table wants to know whether the end they are
                looking at is the table's end or merely today's.

                The count and nothing else. A "loading" suffix here said so whenever more rows
                EXISTED rather than whenever one was in flight, which is a different thing and was
                wrong every time the reader stopped scrolling. */}
            <div className="geometry-foot">
                {rows.rows.length.toLocaleString()} of {rows.total.toLocaleString()}
            </div>
        </div>
    );
}
