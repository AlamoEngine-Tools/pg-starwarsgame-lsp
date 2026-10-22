// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { TreeRow } from "./TreeRow";

const row = (props: Partial<Parameters<typeof TreeRow>[0]> = {}): string =>
    renderToStaticMarkup(
        <TreeRow
            id="bone:3"
            kind="bone"
            name="HP_Weapon_00"
            depth={2}
            expandable
            expanded={false}
            onToggle={() => undefined}
            {...props}
        />,
    );

describe("TreeRow", () => {
    it("is a twisty, the kind's picture and the name, indented by its depth", () => {
        const html = row();

        assert.match(
            html,
            /<li class="tree-row" data-row="bone:3" style="padding-left:26px"/,
        );
        assert.match(html, /<span class="twisty">/);
        assert.match(html, /<span class="row-kind kind-bone" title="Bones">/);
        assert.match(html, /<span class="tree-name">HP_Weapon_00<\/span>/);
    });

    it("keeps a leaf's twisty in its place, unseen, so the names line up", () => {
        assert.match(row({ expandable: false }), /<span class="twisty leaf">/);
    });

    it("carries the owner's own classes and the controls it puts at the row's end", () => {
        const html = row({
            className: "bone-row selected",
            children: <button type="button">eye</button>,
        });

        assert.match(html, /<li class="tree-row bone-row selected"/);
        assert.match(
            html,
            /<span class="tree-name">HP_Weapon_00<\/span><button type="button">eye<\/button><\/li>/,
        );
    });
});
