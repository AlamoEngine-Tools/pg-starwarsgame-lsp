// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { DataTable, DataTableProps } from "./DataTable";
import { dockChromeCss } from "./dockChrome";

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
    // One tone over every cell left the rows and the columns to be told apart by alignment alone;
    // the reader asked for a line between the columns and a slightly lighter every other row.
    describe("its rules in dockChromeCss", () => {
        const rule = (selector: string): string | undefined =>
            dockChromeCss
                .split("}")
                .map((block) => block.trim())
                .find((block) => block.startsWith(selector));

        it("draws a line between the columns, none at the edges and none at the actions", () => {
            assert.match(
                rule(".data-table th + th, .data-table td + td {") ?? "",
                /border-left:/,
            );
            assert.match(
                rule(".data-table td.row-actions-cell {") ?? "",
                /border-left: 0/,
            );
        });

        it("shades every other row a little, under the hover and the selection", () => {
            const zebra = ".data-table tbody tr:nth-child(even) td";
            const hover = ".data-table tbody tr:hover td";
            assert.match(rule(`${zebra} {`) ?? "", /background:/);
            // Equal specificity: the later rule wins, so the hover and the selection must follow.
            assert.ok(
                dockChromeCss.indexOf(zebra) < dockChromeCss.indexOf(hover),
                "the alternating shade is declared before the hover colour",
            );
        });
    });

    it("draws a header cell per column and a row per entry", () => {
        const html = table();

        assert.match(
            html,
            /<table class="data-table fixed" aria-label="Game objects"/,
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

    // Sized to what they held, the columns jumped whenever a filter or a sort changed the rows shown.
    it("keeps its columns at fixed shares of its width, whatever rows it shows", () => {
        const shares = (html: string): string[] =>
            [...html.matchAll(/<col style="width:([\d.]+%)"/g)].map(
                (m) => m[1],
            );

        assert.match(table(), /<table class="data-table fixed"/);
        assert.deepEqual(shares(table()), ["50%", "50%"]);
        assert.deepEqual(shares(table({ rows: [ROWS[1]] })), ["50%", "50%"]);
        assert.deepEqual(
            shares(
                table({
                    columns: [
                        {
                            key: "name",
                            label: "Name",
                            cell: (r) => r.name,
                            share: 3,
                        },
                        { key: "type", label: "Type", cell: (r) => r.type },
                    ],
                }),
            ),
            ["75%", "25%"],
        );
    });
});
