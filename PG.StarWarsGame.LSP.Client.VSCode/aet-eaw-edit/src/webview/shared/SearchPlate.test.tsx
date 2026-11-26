// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { SearchPlate, type SearchPlateProps } from "./SearchPlate";

const noop = (): void => undefined;

const plate = (props: Partial<SearchPlateProps> = {}): string =>
    renderToStaticMarkup(
        <SearchPlate
            open={false}
            pattern=""
            mode="text"
            filtered={false}
            placeholder="Search by either name"
            title="Search the types"
            onPattern={noop}
            onMode={noop}
            onOpen={noop}
            onReset={noop}
            {...props}
        >
            <button type="button">kind</button>
        </SearchPlate>,
    );

/** The filter icon's button, the last on the icon row. */
const filterButton = (html: string): string =>
    /<button[^>]*search-plate-reset[^>]*>.*?<\/button>/.exec(html)?.[0] ?? "";

describe("SearchPlate", () => {
    it("is a magnifier, the list's own toggles and the filter icon while closed, with no field", () => {
        const html = plate();

        assert.match(html, /^<div class="search-plate floating">/);
        assert.doesNotMatch(html, /<input/);
        assert.match(
            html,
            /<div class="search-plate-icons"><button[^>]*title="Search the types"[^>]*>/,
        );
        assert.match(html, /aria-expanded="false"/);
        // Magnifier, toggles, filter icon: in that order, and nothing after.
        assert.match(
            html,
            /<button type="button">kind<\/button><button[^>]*search-plate-reset[^>]*>.*<\/button><\/div><\/div>$/,
        );
    });

    /** With nothing set the filter icon is the plain glyph and cannot be pressed. */
    it("shows the plain filter glyph, disabled, while nothing is set", () => {
        const button = filterButton(plate());

        assert.match(button, /disabled=""/);
        assert.doesNotMatch(button, /search-plate-reset set/);
        assert.match(button, /title="Clear all filters - no filters are set"/);
        assert.match(button, /tabler-icon-filter(?!-)/);
    });

    /**
     * Closing does not reset. A pattern stays in force behind a closed plate, and the filter icon
     * is what says so: it becomes the reset, and names the pattern nobody can see.
     */
    it("keeps its field closed over a pattern, and turns the filter icon into the reset", () => {
        const html = plate({ pattern: "HP_", filtered: true });
        const button = filterButton(html);

        assert.doesNotMatch(html, /<input/);
        assert.match(html, /^<div class="search-plate floating">/);
        assert.match(button, /search-plate-reset set/);
        assert.doesNotMatch(button, /disabled/);
        assert.match(button, /title="Clear all filters - HP_"/);
        assert.match(button, /tabler-icon-filter-off/);
    });

    /** A toggle switched off is a filter too, and has no pattern to name. */
    it("is the reset for a switched-off toggle as well", () => {
        const button = filterButton(plate({ filtered: true }));

        assert.match(button, /search-plate-reset set/);
        assert.match(button, /title="Clear all filters"/);
    });

    /** The field opens BEFORE the icons, which are one item: nothing the reader pointed at moves. */
    it("opens its field before the icons, which stay one group", () => {
        const html = plate({ open: true, pattern: "elapsed" });

        assert.match(html, /^<div class="search-plate floating open">/);
        assert.match(
            html,
            /^<div[^>]*><div class="search-plate-box[^"]*">.*<\/div><div class="search-plate-icons">/,
        );
        assert.match(html, /<input[^>]*class="search-plate-field"/);
        assert.match(html, /value="elapsed"/);
        assert.match(html, /placeholder="Search by either name"/);
    });

    /** How the pattern is read is chosen inside the field, as an editor's find widget has it. */
    it("carries the three pattern modes inside the field, the chosen one checked", () => {
        const html = plate({ open: true, mode: "wildcard" });
        const box =
            /<div class="search-plate-box[^"]*">.*?<\/div><div class="search-plate-icons">/.exec(
                html,
            )?.[0] ?? "";

        assert.match(box, /role="radiogroup"/);
        assert.equal(box.match(/role="radio"/g)?.length, 3);
        assert.match(
            box,
            /aria-checked="true"[^>]*title="Wildcard: \* matches any text, \? matches one character"/,
        );
        assert.equal(box.match(/aria-checked="true"/g)?.length, 1);
    });

    /**
     * Each mode is drawn as the characters it means, not as an icon: `ab` for plain text, `*?` for
     * wildcards, `.*` for a regular expression. A letter-case icon said "case sensitive" for a
     * search that is not, and a star said nothing about wildcards.
     */
    it("draws the modes as written marks: ab, *? and .*", () => {
        const html = plate({ open: true });
        const modes =
            /<div class="search-plate-modes">.*?<\/span><\/div>/.exec(
                html,
            )?.[0] ?? "";

        assert.deepEqual(
            [
                ...modes.matchAll(
                    /<span class="mode-mark"[^>]*>([^<]*)<\/span>/g,
                ),
            ].map((m) => m[1]),
            ["ab", "*?", ".*"],
        );
        assert.doesNotMatch(modes, /<svg/);
    });

    /** A regex that does not compile empties the list; the field says why. */
    it("marks the field invalid and says why when the pattern does not compile", () => {
        const html = plate({
            open: true,
            mode: "regex",
            pattern: "(",
            error: "Unterminated group",
        });

        assert.match(html, /<input[^>]*aria-invalid="true"/);
        assert.match(html, /<input[^>]*title="Unterminated group"/);
    });

    it("turns the magnifier into the way to close it", () => {
        const html = plate({ open: true });

        assert.match(html, /title="Close the search"/);
        assert.match(html, /aria-expanded="true"/);
        assert.match(html, /class="[^"]*search-plate-toggle active[^"]*"/);
    });

    /** The magnifier's accent means the field is open, and nothing else. */
    it("does not light the magnifier for a pattern behind a closed plate", () => {
        assert.doesNotMatch(
            plate({ pattern: "HP_", filtered: true }),
            /search-plate-toggle active/,
        );
    });

    it("takes its place from a variant: on a list's corner, or at the foot of a scroller", () => {
        const sticky = plate({ variant: "sticky", title: undefined });

        assert.match(sticky, /^<div class="search-plate sticky">/);
        // Without a tooltip of its own the magnifier says what the field will ask for.
        assert.match(sticky, /title="Search by either name"/);
    });

    /** A plate in a dock opens its field beside itself, over the stage, where there is room. */
    it("marks a field that opens beside the plate", () => {
        const html = plate({ open: true, beside: true });

        assert.match(html, /^<div class="search-plate floating open beside">/);
        assert.match(html, /<div class="search-plate-box beside"/);
    });

    /**
     * Dropdown filters are pills on a line of their own ABOVE the field, at full length, and only
     * while the plate is open: the closed plate is one row, and the filter icon is what says a
     * pill is set.
     */
    it("puts its dropdown filters on a line above the field while open, and nowhere while closed", () => {
        const pills = <span className="a-pill">All fields</span>;
        const open = plate({ open: true, pills });

        assert.match(
            open,
            /^<div class="search-plate floating open"><div class="search-plate-above"><div class="search-plate-pills"><span class="a-pill">All fields<\/span><\/div><\/div><div class="search-plate-box">/,
        );
        assert.doesNotMatch(plate({ pills }), /a-pill|search-plate-above/);
    });

    /**
     * Why a pattern does not compile, in words above the field. Without it the list just empties,
     * which reads as "no matches" - and a tooltip is nothing a reader finds while typing.
     */
    it("says in words above the field why the pattern does not compile", () => {
        const html = plate({
            open: true,
            mode: "regex",
            pattern: "(",
            error: "Unterminated group",
        });

        assert.match(
            html,
            /<div class="search-plate-above"><div class="search-plate-error" role="alert">Unterminated group<\/div><\/div>/,
        );
        assert.doesNotMatch(plate({ open: true }), /search-plate-above/);
    });

    /** A field beside the plate has no line above it to write on; its tooltip carries the reason. */
    it("keeps the reason on the field alone where the field opens beside the plate", () => {
        const html = plate({
            open: true,
            beside: true,
            mode: "regex",
            pattern: "(",
            error: "Unterminated group",
        });

        assert.doesNotMatch(html, /search-plate-above/);
        assert.match(html, /<input[^>]*title="Unterminated group"/);
    });
});
