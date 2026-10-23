// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { DataTable, DataTableProps } from "./DataTable";

interface Row {
    name: string;
    type: string;
    defined: boolean;
}

const ROWS: Row[] = [
    { name: "Star_Destroyer", type: "SpaceUnit", defined: true },
    { name: "Missing_Unit", type: "", defined: false },
    { name: "Acclamator", type: "SpaceUnit", defined: true },
];

const table = (props: Partial<DataTableProps<Row>> = {}): string =>
    renderToStaticMarkup(
        <DataTable<Row>
            label="Game objects"
            columns={[
                { key: "name", label: "Name", cell: (r) => r.name },
                {
                    key: "type",
                    label: "Type",
                    cell: (r) => r.type,
                    dim: true,
                },
            ]}
            rows={ROWS}
            rowKey={(r) => r.name}
            empty="Nothing matches"
            {...props}
        />,
    );

describe("DataTable", () => {
    it("draws a header cell per column and a row per entry", () => {
        const html = table();

        assert.match(
            html,
            /<table class="data-table" aria-label="Game objects"/,
        );
        assert.equal(html.match(/<th[ >]/g)?.length, 2);
        assert.equal(html.match(/<tr data-row-index/g)?.length, 3);
        assert.match(html, /<td class="dim">SpaceUnit<\/td>/);
    });

    it("puts ONE row in the tab order - the selected one, else the first - and the keys move it", () => {
        assert.equal(table().match(/tabindex="0"/g)?.length, 1);
        assert.match(table(), /data-row-index="0"[^>]*tabindex="0"/);
        assert.match(
            table({ selected: "Acclamator" }),
            /data-row-index="2"[^>]*tabindex="0"/,
        );
        assert.match(
            table({ selected: "Acclamator" }),
            /class="selected"[^>]*>.*Acclamator/,
        );
    });

    it("greys an inert row and says why on it, rather than leaving it out", () => {
        const html = table({
            inert: (r) => (r.defined ? undefined : "Nothing defines this name"),
        });

        assert.match(
            html,
            /class="inert"[^>]*title="Nothing defines this name"/,
        );
        assert.equal(html.match(/<tr data-row-index/g)?.length, 3);
    });

    it("marks a row with a finding by its severity", () => {
        const html = table({
            severity: (r) => (r.defined ? undefined : "error"),
        });

        assert.equal(html.match(/row-sev-error/g)?.length, 1);
    });

    it("gives a sortable header its state, and only the sorted column an arrow", () => {
        const html = table({
            sortable: ["name", "type"],
            sort: { column: "type", direction: "desc" },
            onSort: () => undefined,
        });

        assert.match(html, /<th class="sortable"[^>]*aria-sort="none"/);
        assert.match(html, /<th class="sortable"[^>]*aria-sort="descending"/);
        assert.equal(html.match(/<svg/g)?.length, 1);
    });

    it("draws each row's actions in a cell of their own at the row's end", () => {
        const html = table({
            actions: (r) => <button type="button">{`act ${r.name}`}</button>,
        });

        assert.equal(html.match(/row-actions-cell/g)?.length, 3);
        assert.match(
            html,
            /<span class="row-actions"><button[^>]*>act Star_Destroyer/,
        );
    });

    it("says why it is empty, and keeps its header", () => {
        const html = table({ rows: [] });

        assert.match(html, /<th/);
        assert.match(html, /class="data-table-empty"[^>]*>Nothing matches</);
    });

    it("carries a foot when given one", () => {
        assert.match(
            table({ foot: <span>Showing 3 of 3</span> }),
            /class="data-table-foot"><span>Showing 3 of 3/,
        );
        assert.equal(/data-table-foot/.test(table()), false);
    });
});
