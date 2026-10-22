// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    closeTab,
    NO_TABS,
    openPreview,
    pinTab,
    showTable,
    Tabs,
} from "./modBrowserTabs";

/** The Mod Browser's tabs (#270): the table's own tab, and the previews opened from it. */
describe("tabs", () => {
    const keys = (tabs: Tabs<string>) =>
        tabs.previews.map((t) => `${t.key}${t.pinned ? "*" : ""}`);

    it("opens a preview in the foreground, the table's tab staying", () => {
        const tabs = openPreview(NO_TABS as Tabs<string>, "a", "A");

        assert.deepEqual(keys(tabs), ["a"]);
        assert.equal(tabs.active, "a");
        assert.equal(showTable(tabs).active, null);
    });

    it("reuses the one unpinned tab, in its place", () => {
        let tabs = openPreview(NO_TABS as Tabs<string>, "a", "A");
        // b takes over a's tab, then is pinned; c opens beside it and d takes c's.
        tabs = pinTab(openPreview(tabs, "b", "B"), "b");
        tabs = openPreview(tabs, "c", "C");
        tabs = openPreview(tabs, "d", "D");

        assert.deepEqual(keys(tabs), ["b*", "d"]);
        assert.equal(tabs.active, "d");
    });

    it("keeps a pinned tab and opens the next one beside it", () => {
        const tabs = openPreview(
            pinTab(openPreview(NO_TABS as Tabs<string>, "a", "A"), "a"),
            "b",
            "B",
        );

        assert.deepEqual(keys(tabs), ["a*", "b"]);
    });

    it("brings an entry that is already open to the front instead of opening it twice", () => {
        let tabs = pinTab(openPreview(NO_TABS as Tabs<string>, "a", "A"), "a");
        tabs = openPreview(tabs, "b", "B");
        tabs = openPreview(tabs, "a", "A");

        assert.deepEqual(keys(tabs), ["a*", "b"]);
        assert.equal(tabs.active, "a");
    });

    it("moves to a neighbour when the active tab closes, and to the table after the last", () => {
        let tabs = pinTab(openPreview(NO_TABS as Tabs<string>, "a", "A"), "a");
        tabs = openPreview(tabs, "b", "B");

        tabs = closeTab(tabs, "b");
        assert.deepEqual(keys(tabs), ["a*"]);
        assert.equal(tabs.active, "a");

        tabs = closeTab(tabs, "a");
        assert.deepEqual(keys(tabs), []);
        assert.equal(tabs.active, null);
    });

    it("leaves the active tab alone when another one closes", () => {
        let tabs = pinTab(openPreview(NO_TABS as Tabs<string>, "a", "A"), "a");
        tabs = openPreview(tabs, "b", "B");

        assert.equal(closeTab(tabs, "a").active, "b");
    });
});
