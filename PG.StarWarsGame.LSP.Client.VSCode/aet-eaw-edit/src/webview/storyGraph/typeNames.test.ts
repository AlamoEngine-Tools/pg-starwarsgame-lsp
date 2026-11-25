// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    breakPieces,
    matchesType,
    otherName,
    tileName,
    typeNameMode,
    type TypeNames,
} from "./typeNames";

const labels = new Map([
    ["STORY_ELAPSED", "Time elapsed"],
    ["STORY_MOVIE_DONE", "Movie finished"],
]);

const display: TypeNames = { mode: "display", labels };
const technical: TypeNames = { mode: "technical", labels };

describe("tileName", () => {
    it("shows the display name where the reader asked for names", () => {
        assert.deepEqual(tileName(display, "STORY_ELAPSED", ""), {
            text: "Time elapsed",
            technical: false,
        });
    });

    it("shows the technical name where the reader asked for it", () => {
        assert.deepEqual(tileName(technical, "STORY_ELAPSED", ""), {
            text: "STORY_ELAPSED",
            technical: true,
        });
    });

    /** A type the schema has not named is the one tile that keeps its identifier. */
    it("falls back to the technical name for a type without a display name", () => {
        assert.deepEqual(tileName(display, "STORY_UNNAMED", ""), {
            text: "STORY_UNNAMED",
            technical: true,
        });
    });

    /**
     * Typing `done` finds Movie finished by its technical name. Left as it is, the tile would
     * stand in the results with nothing on it that says what was typed.
     */
    it("shows the technical name while a search matches only that", () => {
        assert.deepEqual(tileName(display, "STORY_MOVIE_DONE", "done"), {
            text: "STORY_MOVIE_DONE",
            technical: true,
        });
    });

    it("keeps the display name while the search matches it", () => {
        assert.equal(
            tileName(display, "STORY_MOVIE_DONE", "movie").text,
            "Movie finished",
        );
        // Both names answer `elapsed`: the display name is what the reader chose to see.
        assert.equal(
            tileName(display, "STORY_ELAPSED", "elapsed").text,
            "Time elapsed",
        );
    });
});

describe("otherName", () => {
    it("is the name the tile does not show, for its tooltip", () => {
        assert.equal(otherName(display, "STORY_ELAPSED", ""), "STORY_ELAPSED");
        assert.equal(otherName(technical, "STORY_ELAPSED", ""), "Time elapsed");
        assert.equal(
            otherName(display, "STORY_MOVIE_DONE", "done"),
            "Movie finished",
        );
    });

    it("is nothing for a type with one name only", () => {
        assert.equal(otherName(display, "STORY_UNNAMED", ""), null);
        assert.equal(otherName(technical, "STORY_UNNAMED", ""), null);
    });
});

describe("matchesType", () => {
    it("finds a type by either name, whichever the tiles show", () => {
        for (const names of [display, technical]) {
            assert.equal(matchesType(names, "STORY_ELAPSED", "time"), true);
            assert.equal(matchesType(names, "STORY_ELAPSED", "story_el"), true);
            assert.equal(matchesType(names, "STORY_ELAPSED", "movie"), false);
        }
    });

    it("ignores case and matches everything while nothing is typed", () => {
        assert.equal(matchesType(display, "STORY_ELAPSED", "TIME EL"), true);
        assert.equal(matchesType(display, "STORY_ELAPSED", ""), true);
    });
});

describe("breakPieces", () => {
    /** A browser breaks a line at a space and not at an underscore, so the tile says where. */
    it("cuts a technical name after each underscore", () => {
        assert.deepEqual(breakPieces("STORY_MOVIE_DONE"), [
            "STORY_",
            "MOVIE_",
            "DONE",
        ]);
        assert.deepEqual(breakPieces("CREDITS"), ["CREDITS"]);
    });
});

describe("typeNameMode", () => {
    it("reads the setting, and takes anything else as the default", () => {
        assert.equal(typeNameMode("technical"), "technical");
        assert.equal(typeNameMode("display"), "display");
        assert.equal(typeNameMode(undefined), "display");
        assert.equal(typeNameMode("names"), "display");
    });
});
