// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { beforeEach, describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { RightDock } from "./RightDock";
import {
    applyStoredLayout,
    resetPanelLayoutForTests,
} from "../../shared/panelLayout";

const dial = <i>dial</i>;

describe("RightDock", () => {
    beforeEach(() => resetPanelLayoutForTests());

    it("draws its three levels in order: header, content, foot", () => {
        const html = renderToStaticMarkup(
            <RightDock
                header={<i>h</i>}
                content={<i>c</i>}
                overview={<i>f</i>}
            />,
        );

        const header = html.indexOf('class="dock-header"');
        const content = html.indexOf('class="dock-content"');
        const foot = html.indexOf('class="dock-overview"');
        assert.ok(header > 0 && header < content && content < foot, html);
    });

    /** The dial's header is the tall one, so it is the one worth folding away. */
    it("gives a header that holds the dial a fold in its corner", () => {
        const html = renderToStaticMarkup(
            <RightDock header={dial} headerHoldsDial content={<i>c</i>} />,
        );

        assert.match(html, /class="dock-header with-dial"/);
        assert.match(
            html,
            /<button type="button" class="icon-btn header-fold" title="Fold the dock header away" aria-label="Fold the dock header away" aria-expanded="true">/,
        );
        assert.match(html, /<i>dial<\/i>/);
    });

    it("gives a header without the dial no fold", () => {
        const html = renderToStaticMarkup(
            <RightDock header={<i>h</i>} content={<i>c</i>} />,
        );

        assert.equal(html.includes("header-fold"), false, html);
    });

    it("opens folded where the reader left it folded, and offers to open it", () => {
        applyStoredLayout({ "story.dock.headerFolded": 1 });

        const html = renderToStaticMarkup(
            <RightDock
                memoKey="story.dock"
                header={dial}
                headerHoldsDial
                content={<i>c</i>}
            />,
        );

        assert.match(html, /class="dock-header with-dial folded"/);
        assert.match(
            html,
            /class="icon-btn header-fold" title="Open the dock header" aria-label="Open the dock header" aria-expanded="false"/,
        );
        // Unmounted, like a folded section's body: nothing of the dial is left to tab into.
        assert.equal(html.includes("<i>dial</i>"), false, html);
    });

    /** Each editor's dock is its own question: one folded header folds no other. */
    it("keeps one dock's fold apart from another's", () => {
        applyStoredLayout({ "story.dock.headerFolded": 1 });

        const html = renderToStaticMarkup(
            <RightDock
                memoKey="preview.dock"
                header={dial}
                headerHoldsDial
                content={<i>c</i>}
            />,
        );

        assert.equal(html.includes("folded"), false, html);
        assert.match(html, /<i>dial<\/i>/);
    });
});
