// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    GEOMETRY_PAGE, appendPage, geometryTabs, nearEnd, nextOffset, type GeometryRows,
} from './geometryRows';
import { type SubMeshGeometryPage } from '../../protocol/modelPreview';

/** A page of `faces`, which is the table with a real total behind it. */
function faces(offset: number, count: number, totalFaces = 1000): SubMeshGeometryPage {
    return {
        model: 'EV_StarDestroyer.ALO',
        meshIndex: 0,
        subMeshIndex: 0,
        table: 'faces',
        offset,
        totalVertices: 4000,
        totalFaces,
        vertices: [],
        boneMapping: [],
        faces: Array.from({ length: count }, (_, i) => ({
            index: offset + i, v0: offset + i, v1: offset + i + 1, v2: offset + i + 2,
        })),
    };
}

describe('appendPage', () => {
    it('takes the first page whole, with its columns and its total', () => {
        const rows = appendPage(null, faces(0, 500));

        assert.equal(rows.table, 'faces');
        assert.equal(rows.rows.length, 500);
        assert.equal(rows.total, 1000);
        assert.deepEqual([...rows.columns], ['#', 'V0', 'V1', 'V2']);
    });

    it('appends the next page onto the end', () => {
        const rows = appendPage(appendPage(null, faces(0, 500)), faces(500, 500));

        assert.equal(rows.rows.length, 1000);
        assert.equal(rows.rows[999][0], '999');
    });

    it('does not duplicate a page that arrives twice', () => {
        // A scroll handler fires far more often than pages arrive, so the same offset being asked
        // for twice is ordinary rather than exceptional. Duplicating would put row 500 on screen
        // twice and make the table longer than the model.
        const once = appendPage(null, faces(0, 500));
        const twice = appendPage(once, faces(0, 500));

        assert.equal(twice.rows.length, 500);
    });

    it('takes only the tail of a page that overlaps what is already in hand', () => {
        const rows = appendPage(appendPage(null, faces(0, 500)), faces(400, 300));

        assert.equal(rows.rows.length, 700);
        assert.equal(rows.rows[699][0], '699');
    });

    it('ignores a page that would leave a HOLE', () => {
        // Rows are indexed by position once they are in hand, so a gap would silently mis-label
        // every row after it. Dropping the page leaves the table short, and the scroller asks again.
        const rows = appendPage(appendPage(null, faces(0, 500)), faces(900, 100));

        assert.equal(rows.rows.length, 500);
    });

    it('starts again when the page is for a different table', () => {
        // Switching tab is not appending - the columns differ, and so does what a row means.
        const start = appendPage(null, faces(0, 500));
        const swapped = appendPage(start, {
            ...faces(0, 0), table: 'boneMapping',
            boneMapping: [{ slot: 0, boneIndex: 3, name: 'Root' }],
        });

        assert.equal(swapped.table, 'boneMapping');
        assert.equal(swapped.rows.length, 1);
    });

    it('takes the bone mapping as complete, since it arrives whole', () => {
        // The server does not page it - there are a handful of slots at most - so its own length is
        // its total and nothing further is ever asked for.
        const rows = appendPage(null, {
            ...faces(0, 0), table: 'boneMapping',
            boneMapping: [
                { slot: 0, boneIndex: 3, name: 'Root' },
                { slot: 1, boneIndex: 4, name: 'Body' },
            ],
        });

        assert.equal(rows.total, 2);
        assert.equal(nextOffset(rows), null);
    });

    it('carries the note that says why a table is empty', () => {
        // A static sub-mesh has no skin table at all - the Star Destroyer's own hull is one - and
        // saying so beats an empty grid, which reads as a request that failed.
        const rows = appendPage(null, { ...faces(0, 0), table: 'boneMapping' });

        assert.match(rows.note ?? '', /not skinned/i);
    });
});

describe('nextOffset', () => {
    it('asks for the first page when nothing is in hand', () => {
        assert.equal(nextOffset(null), 0);
    });

    it('asks for the row after the last one it has', () => {
        assert.equal(nextOffset(appendPage(null, faces(0, 500))), 500);
    });

    it('asks for nothing once the table is complete', () => {
        assert.equal(nextOffset(appendPage(null, faces(0, 40, 40))), null);
    });

    it('asks for nothing on an empty table', () => {
        assert.equal(nextOffset(appendPage(null, faces(0, 0, 0))), null);
    });
});

describe('nearEnd', () => {
    // One viewport height of runway, so the next page is being fetched while the reader is still
    // reading the rows above it and the table never visibly stops.
    it('is true within a screenful of the bottom', () => {
        // Showing 1400-1800 of 2000: 200 left below the fold, against 400 of runway.
        assert.equal(nearEnd({ scrollTop: 1400, scrollHeight: 2000, clientHeight: 400 }), true);
    });

    it('is false while there is more than a screenful still to come', () => {
        // Showing 900-1300 of 2000, so 700 left - more than one viewport, and nothing to fetch yet.
        assert.equal(nearEnd({ scrollTop: 900, scrollHeight: 2000, clientHeight: 400 }), false);
        assert.equal(nearEnd({ scrollTop: 100, scrollHeight: 5000, clientHeight: 400 }), false);
    });

    it('is true for a table shorter than its own scroller', () => {
        // Which is how the SECOND page gets asked for at all when the first one did not fill the
        // box: no scrolling can happen, so nothing would ever ask again.
        assert.equal(nearEnd({ scrollTop: 0, scrollHeight: 300, clientHeight: 400 }), true);
    });
});

describe('geometryTabs', () => {
    it('offers the three tables in a fixed order', () => {
        assert.deepEqual(geometryTabs().map(tab => tab.id),
            ['vertices', 'faces', 'boneMapping']);
    });

    it('says how many rows a table holds once they are in hand', () => {
        const rows: GeometryRows = appendPage(null, faces(0, 500));

        assert.equal(geometryTabs(rows).find(tab => tab.id === 'faces')?.count, 1000);
    });

    it('says nothing about the tables it has not read', () => {
        const rows = appendPage(null, faces(0, 500));

        assert.equal(geometryTabs(rows).find(tab => tab.id === 'vertices')?.count, null);
    });
});

describe('GEOMETRY_PAGE', () => {
    it('asks for as much as the server will give', () => {
        // `PreviewSubMeshGeometry.MaxPage` is 500 and it is a cap, not a suggestion. Asking for
        // less only means more round trips for the same rows.
        assert.equal(GEOMETRY_PAGE, 500);
    });
});
