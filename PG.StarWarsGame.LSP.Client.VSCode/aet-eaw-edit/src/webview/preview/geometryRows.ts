// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The bulk geometry tables, gathered as they arrive.
//
// The inspector is its own editor tab now, with a page's worth of room, so the tables are read the
// way a table is read: one long list you scroll. They used to be a hundred rows at a time behind a
// previous/next pair, which is the arrangement you build when a panel is 320 pixels wide.
//
// Paging did not go away - the server caps a request at 500 rows and a Star Destroyer sub-mesh is
// 3814 faces - it stopped being the reader's problem. This module holds what has arrived, says what
// to ask for next, and says when the scroller is close enough to its end to want it.

import { geometryTable } from './inspector';
import { type GeometryTable, type SubMeshGeometryPage } from '../../protocol/modelPreview';

/**
 * Rows per request.
 *
 * `PreviewSubMeshGeometry.MaxPage` on the server, which is a cap rather than a suggestion. Asking
 * for less only means more round trips for the same rows.
 */
export const GEOMETRY_PAGE = 500;

/** The tables, in the order the tabs offer them. */
const TABLES: readonly { id: GeometryTable; label: string; title: string }[] = [
    {
        id: 'vertices', label: 'Vertices',
        title: 'Position, normal, UVs, tangents, colour and bone binding, per vertex',
    },
    { id: 'faces', label: 'Faces', title: 'Each triangle as the three vertex indices it draws' },
    {
        id: 'boneMapping', label: 'Bone mapping',
        title: 'Which model bone each local bone slot of this sub-mesh resolves to',
    },
];

/** Everything gathered for one table so far. */
export interface GeometryRows {
    table: GeometryTable;
    columns: readonly string[];
    /** Formatted rows, in table order, contiguous from row zero. */
    rows: readonly (readonly string[])[];
    /** Rows in the whole table, which is what makes the list a part of something. */
    total: number;
    /** Why the table is empty, when it is empty for a reason. */
    note?: string;
}

/** One tab, and what it can say about itself before it has been opened. */
export interface GeometryTab {
    id: GeometryTable;
    label: string;
    title: string;
    /** Rows in that table, or null while nothing has been read from it. */
    count: number | null;
}

/**
 * The tabs, told what is currently in hand.
 *
 * Only the table being read knows its own total - the server sends the counts on its pages - so the
 * other two carry null rather than a zero. A zero would be a claim, and the wrong one.
 */
export function geometryTabs(rows: GeometryRows | null = null): GeometryTab[] {
    return TABLES.map(entry => ({
        ...entry,
        count: rows !== null && rows.table === entry.id ? rows.total : null,
    }));
}

/**
 * Folds an arriving page into what is already in hand.
 *
 * Contiguity is the invariant: rows are indexed by their position once stored, so the list must
 * start at row zero and have no gaps. Everything here follows from that.
 *
 * - A page for another table REPLACES rather than appends. The columns differ and so does what a
 *   row means.
 * - A page that repeats rows already held contributes only its tail. A scroll handler fires far
 *   more often than pages arrive, so the same offset being asked for twice is ordinary.
 * - A page that would leave a HOLE is dropped. The table stays short and the scroller asks again;
 *   accepting it would silently mis-label every row after the gap.
 */
export function appendPage(
    current: GeometryRows | null, page: SubMeshGeometryPage,
): GeometryRows {
    const view = geometryTable(page);
    const table = page.table as GeometryTable;

    // The bone mapping is not paged - there are a handful of slots at most - so its own length is
    // its total. The other two are pages of something the server has counted for us.
    const total = table === 'faces'
        ? page.totalFaces
        : table === 'vertices' ? page.totalVertices : view.rows.length;

    const fresh: GeometryRows = {
        table,
        columns: view.columns,
        rows: view.rows,
        total,
        ...(view.note === undefined ? {} : { note: view.note }),
    };

    if (current === null || current.table !== table) {
        return fresh;
    }

    if (page.offset > current.rows.length) {
        return current;
    }

    const tail = view.rows.slice(current.rows.length - page.offset);

    return {
        ...current,
        total,
        ...(view.note === undefined ? {} : { note: view.note }),
        rows: tail.length === 0 ? current.rows : [...current.rows, ...tail],
    };
}

/** The offset to ask for next, or null when there is nothing more to fetch. */
export function nextOffset(rows: GeometryRows | null): number | null {
    if (rows === null) {
        return 0;
    }

    return rows.rows.length < rows.total ? rows.rows.length : null;
}

/** How much runway to keep below the reader, as a multiple of the scroller's own height. */
const RUNWAY = 1;

/**
 * Whether a scroller is close enough to its end to want the next page.
 *
 * One viewport height of runway, so the next page is fetched while the reader is still reading the
 * rows above it and the table never visibly stops. A box the rows do not fill is ALWAYS near its
 * end, which is how the second page gets asked for at all when the first one left no room to
 * scroll - without that the table would stall at 500 rows on a tall screen.
 */
export function nearEnd(
    view: { scrollTop: number; scrollHeight: number; clientHeight: number },
): boolean {
    return view.scrollTop + view.clientHeight * (1 + RUNWAY) >= view.scrollHeight;
}
