// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { Panel } from "./Panel";

describe("Panel", () => {
    it("titles its body with the dock section heading, so it reads as one with the docks", () => {
        const html = renderToStaticMarkup(
            <Panel title="References">
                <p>body</p>
            </Panel>,
        );

        assert.match(
            html,
            /<section class="panel"[^>]*><div class="dock-section-title"><span class="dock-section-name">References/,
        );
        assert.match(html, /<div class="panel-body"><p>body<\/p>/);
    });

    it("puts its count and end content inside ONE title-end wrapper", () => {
        const html = renderToStaticMarkup(
            <Panel
                title="Problems"
                count={5}
                end={<button type="button">x</button>}
            >
                <p>body</p>
            </Panel>,
        );

        assert.equal(html.match(/title-end/g)?.length, 1);
        assert.match(
            html,
            /<span class="title-end"><span class="section-count">5<\/span><button/,
        );
    });

    it("lays a list or a table flush with its edges, and carries a foot when given one", () => {
        const html = renderToStaticMarkup(
            <Panel title="Samples" flush foot="15 samples">
                <ul />
            </Panel>,
        );

        assert.match(html, /class="panel-body flush"/);
        assert.match(html, /<div class="panel-foot">15 samples<\/div>/);
    });
});
