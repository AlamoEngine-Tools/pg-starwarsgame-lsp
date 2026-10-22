// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { TabStrip } from "./TabStrip";

const none = (): void => undefined;

const strip = (active: string | null): string =>
    renderToStaticMarkup(
        <TabStrip
            label="Open entries"
            home="Game objects"
            tabs={[
                { key: "a", label: "Star_Destroyer", pinned: true },
                { key: "b", label: "Acclamator", pinned: false },
            ]}
            active={active}
            onHome={none}
            onSelect={none}
            onPin={none}
            onClose={none}
        />,
    );

describe("TabStrip", () => {
    it("draws the home tab first, then each tab by its label, as the editor's tab strip", () => {
        const html = strip(null);

        assert.match(html, /class="editor-tabs"[^>]*role="tablist"/);
        assert.ok(
            html.indexOf("Game objects") < html.indexOf("Star_Destroyer"),
        );
        assert.ok(html.indexOf("Star_Destroyer") < html.indexOf("Acclamator"));
    });

    it("marks only the active tab, the home tab when nothing else is", () => {
        assert.equal(strip(null).match(/aria-selected="true"/g)?.length, 1);
        assert.match(strip(null), /aria-selected="true"[^>]*>Game objects</);
        assert.match(strip("b"), /editor-tab on unpinned/);
    });

    it("sets the unpinned tab in italics with a pin, and gives the home tab no close", () => {
        const html = strip(null);

        assert.equal(html.match(/editor-tab[^"]*unpinned/g)?.length, 1);
        assert.equal(html.match(/title="Keep open"/g)?.length, 1);
        assert.equal(html.match(/title="Close"/g)?.length, 2);
        assert.match(html, /editor-tab only-name/);
    });
});
