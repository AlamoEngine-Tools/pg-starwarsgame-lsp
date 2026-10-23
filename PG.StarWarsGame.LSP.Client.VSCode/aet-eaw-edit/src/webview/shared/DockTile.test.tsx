// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { DockTile } from "./DockTile";

const none = (): void => undefined;

describe("DockTile", () => {
    it("is a glyph over a label, titled with what it does", () => {
        const html = renderToStaticMarkup(
            <DockTile
                glyph={<span className="g" />}
                label="Go to definition"
                title="Go to definition"
                onClick={none}
            />,
        );

        assert.match(
            html,
            /<button type="button" class="dock-tile" title="Go to definition"><span class="tile-glyph"><span class="g"><\/span><\/span><span class="tile-label">Go to definition<\/span>/,
        );
    });

    it("stays when it cannot act, disabled and titled with why", () => {
        const html = renderToStaticMarkup(
            <DockTile
                glyph={<span />}
                label="Show effective object"
                title="Show effective object"
                disabled
                disabledReason="Not a variant"
                onClick={none}
            />,
        );

        assert.match(html, /disabled=""/);
        assert.match(html, /title="Not a variant"/);
        assert.match(html, /<span class="tile-badge">Not a variant<\/span>/);
    });
});
