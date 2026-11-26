// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { SearchPlate, searchPlateOpen } from "./SearchPlate";

const noop = (): void => undefined;

const plate = (open: boolean, pattern = ""): string =>
    renderToStaticMarkup(
        <SearchPlate
            open={open}
            pattern={pattern}
            placeholder="Search by either name"
            title="Search the types"
            onPattern={noop}
            onOpen={noop}
        >
            <button type="button">kind</button>
        </SearchPlate>,
    );

describe("searchPlateOpen", () => {
    it("is open when the magnifier was pressed", () => {
        assert.equal(searchPlateOpen(true, ""), true);
        assert.equal(searchPlateOpen(false, ""), false);
    });

    /** A filter nobody can see is a filter nobody can undo. */
    it("stays open while it holds a pattern, however the magnifier was left", () => {
        assert.equal(searchPlateOpen(false, "HP_"), true);
        // One space matches nothing and empties the list: the loudest version of the hazard.
        assert.equal(searchPlateOpen(false, " "), true);
    });
});

describe("SearchPlate", () => {
    it("is a magnifier and the list's own toggles while closed, with no field", () => {
        const html = plate(false);

        assert.match(html, /^<div class="search-plate floating">/);
        assert.doesNotMatch(html, /<input/);
        assert.match(
            html,
            /<div class="search-plate-icons"><button[^>]*title="Search the types"[^>]*>/,
        );
        assert.match(html, /aria-expanded="false"/);
        assert.match(
            html,
            /<button type="button">kind<\/button><\/div><\/div>$/,
        );
    });

    /** The field opens BEFORE the icons, which are one item: nothing the reader pointed at moves. */
    it("opens its field before the icons, which stay one group", () => {
        const html = plate(true, "elapsed");

        assert.match(html, /^<div class="search-plate floating open">/);
        assert.match(
            html,
            /^<div[^>]*><input[^>]*class="search-plate-field"[^>]*><div class="search-plate-icons">/,
        );
        assert.match(html, /value="elapsed"/);
        assert.match(html, /placeholder="Search by either name"/);
    });

    it("turns the magnifier into the way to close it", () => {
        const html = plate(true);

        assert.match(html, /title="Close the filter"/);
        assert.match(html, /aria-expanded="true"/);
        assert.match(html, /class="[^"]*search-plate-toggle active[^"]*"/);
    });

    it("takes its place from a variant: on a list, at the foot of a scroller, or in the flow", () => {
        const sticky = renderToStaticMarkup(
            <SearchPlate
                open={false}
                pattern=""
                placeholder="Search"
                variant="sticky"
                onPattern={noop}
                onOpen={noop}
            />,
        );

        assert.match(sticky, /^<div class="search-plate sticky">/);
        // Without a tooltip of its own the magnifier says what the field will ask for.
        assert.match(sticky, /title="Search"/);
    });
});
