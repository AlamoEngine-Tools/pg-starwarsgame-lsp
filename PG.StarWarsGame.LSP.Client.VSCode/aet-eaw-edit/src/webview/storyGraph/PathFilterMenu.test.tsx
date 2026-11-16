// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { Icon } from "../shared/Icon";
import { PathDirectionMenu, PathFilterMenu } from "./PathFilterMenu";
import { PATH_DIRECTIONS } from "./pathDirection";

const noop = (): void => undefined;

/** The markup the icon seam draws for a meaning, to find in a rendered menu. */
const glyph = (name: Parameters<typeof Icon>[0]["name"]): string =>
    renderToStaticMarkup(<Icon name={name} />);

describe("PathFilterMenu", () => {
    // Closed it is exactly what it replaced: one filter icon in the node's header.
    it("is a single filter button until it is opened", () => {
        const html = renderToStaticMarkup(<PathFilterMenu onPick={noop} />);

        assert.ok(html.includes(glyph("filter")), html);
        assert.equal(html.includes(glyph("incoming")), false);
        assert.match(html, /aria-haspopup="menu"/);
        assert.match(html, /aria-expanded="false"/);
    });

    it("says what it does, since a filter icon alone does not", () => {
        const html = renderToStaticMarkup(<PathFilterMenu onPick={noop} />);

        assert.match(html, /title="Filter by path"/);
    });

    // The three choices are the module's, so the menu cannot drift from the wire values. The menu
    // body is rendered directly: the open menu is portalled, and a portal needs a document.
    it("lists every direction with its own icon and label", () => {
        const html = renderToStaticMarkup(<PathDirectionMenu onPick={noop} />);

        for (const choice of PATH_DIRECTIONS) {
            assert.ok(html.includes(glyph(choice.icon)), choice.direction);
            assert.match(
                html,
                new RegExp(choice.label.replace(/'/g, "&#x27;")),
                choice.label,
            );
        }
    });

    it("marks the direction already applied", () => {
        const html = renderToStaticMarkup(
            <PathDirectionMenu onPick={noop} active="Upstream" />,
        );

        const checked = html.indexOf('aria-checked="true"');
        assert.ok(checked >= 0, html);
        const item = html.slice(checked, html.indexOf("</button>", checked));
        assert.ok(item.includes(glyph("incoming")), item);
    });

    it("renders as a list of menu items", () => {
        const html = renderToStaticMarkup(<PathDirectionMenu onPick={noop} />);

        assert.match(html, /role="menu"/);
        assert.equal(
            (html.match(/role="menuitemradio"/g) ?? []).length,
            PATH_DIRECTIONS.length,
        );
    });

    // Hidden until it has been measured, so it never flashes at the window corner.
    it("hides itself until it has a placement", () => {
        assert.match(
            renderToStaticMarkup(<PathDirectionMenu onPick={noop} />),
            /visibility:hidden/,
        );
        assert.match(
            renderToStaticMarkup(
                <PathDirectionMenu
                    onPick={noop}
                    placement={{
                        left: 40,
                        top: 12,
                        side: "below",
                        arrowLeft: 0,
                    }}
                />,
            ),
            /left:40px/,
        );
    });
});
