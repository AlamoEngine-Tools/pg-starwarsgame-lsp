// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { PageToolbar } from "./PageToolbar";

const noop = (): void => undefined;
const verdict = <i>v</i>;

describe("PageToolbar", () => {
    it("is a toolbar of two ends: the page's actions left, the verdict right", () => {
        const html = renderToStaticMarkup(
            <PageToolbar actions={<i>a</i>} verdict={verdict} />,
        );

        assert.match(html, /^<div class="page-toolbar" role="toolbar"/);
        assert.match(
            html,
            /<span class="page-toolbar-start"><i>a<\/i><\/span><span class="page-toolbar-end"><i>v<\/i><\/span><\/div>$/,
        );
    });

    /**
     * A control stands in the same spot on every page it appears on. What only some pages have
     * grows inward, so the two ends never move.
     */
    it("keeps Save first and the verdict last, whatever else a page has", () => {
        const bare = renderToStaticMarkup(
            <PageToolbar
                save={{ title: "Save - write it", pending: 0, onSave: noop }}
                verdict={verdict}
            />,
        );
        const full = renderToStaticMarkup(
            <PageToolbar
                save={{ title: "Save - write it", pending: 0, onSave: noop }}
                actions={<i>a</i>}
                verdict={verdict}
            />,
        );

        for (const html of [bare, full]) {
            assert.match(
                html,
                /<span class="page-toolbar-start"><button[^>]*toolbar-save/,
            );
            assert.match(html, /<i>v<\/i><\/span><\/div>$/);
        }
    });

    it("puts Save first, then the page's own actions", () => {
        const html = renderToStaticMarkup(
            <PageToolbar
                save={{ title: "Save - write it", pending: 0, onSave: noop }}
                verdict={verdict}
                actions={<i>a</i>}
            />,
        );

        const save = html.indexOf("toolbar-save");
        const action = html.indexOf("<i>a</i>");
        assert.ok(save > 0 && save < action, html);
    });

    /** Icons first: the glyph means saving, so no word stands beside it. */
    it("draws Save as its glyph alone, named by its tooltip", () => {
        const html = renderToStaticMarkup(
            <PageToolbar
                save={{ title: "Save - write it", pending: 0, onSave: noop }}
                verdict={verdict}
            />,
        );

        assert.match(
            html,
            /<button type="button" class="icon-btn toolbar-save" title="Save - write it" aria-label="Save - write it"><svg[\s\S]*?<\/svg><\/button>/,
        );
    });

    /** A viewer stages nothing. A Save that could never be pressed there would say nothing true. */
    it("has no Save on a page that stages nothing", () => {
        const html = renderToStaticMarkup(<PageToolbar verdict={verdict} />);

        assert.equal(html.includes("toolbar-save"), false, html);
    });

    /** The tab's own cross closes the page; a second control for it would only take the room. */
    it("has no Close of its own", () => {
        const html = renderToStaticMarkup(
            <PageToolbar
                save={{ title: "Save - write it", pending: 0, onSave: noop }}
                verdict={verdict}
            />,
        );

        assert.equal(/Close/.test(html), false, html);
    });

    it("says why Save cannot be pressed while it cannot", () => {
        const html = renderToStaticMarkup(
            <PageToolbar
                save={{
                    title: "Save - write it",
                    pending: 0,
                    disabled: true,
                    disabledReason: "Save - nothing is staged",
                    onSave: noop,
                }}
                verdict={verdict}
            />,
        );

        assert.match(
            html,
            /class="icon-btn toolbar-save" title="Save - nothing is staged" aria-label="Save - nothing is staged" disabled=""/,
        );
    });

    it("fills Save and counts the staged changes beside it, and shows no zero", () => {
        const staged = renderToStaticMarkup(
            <PageToolbar
                save={{ title: "Save - write it", pending: 3, onSave: noop }}
                verdict={verdict}
            />,
        );
        const none = renderToStaticMarkup(
            <PageToolbar
                save={{ title: "Save - write it", pending: 0, onSave: noop }}
                verdict={verdict}
            />,
        );

        assert.match(staged, /class="icon-btn toolbar-save active"/);
        assert.match(staged, /<\/svg> 3<\/button>/);
        assert.equal(none.includes("active"), false, none);
        assert.match(none, /<\/svg><\/button>/);
    });
});
