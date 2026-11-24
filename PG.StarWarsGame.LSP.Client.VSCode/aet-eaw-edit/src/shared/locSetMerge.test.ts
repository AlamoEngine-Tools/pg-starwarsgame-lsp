// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { MemberDocument, mergeMembers } from "./locSetMerge";

function member(
    filePath: string,
    language: string,
    entries: [string, string][],
): MemberDocument {
    return {
        filePath,
        languages: [language],
        rows: entries.map(([key, value], index) => ({
            index,
            key,
            values: [{ language, value }],
        })),
    };
}

const english = member("/t/mastertextfile_english.properties", "ENGLISH", [
    ["TEXT_A", "Alpha"],
    ["TEXT_B", "Beta"],
]);
const german = member("/t/mastertextfile_german.properties", "GERMAN", [
    ["TEXT_A", "Alfa"],
    ["TEXT_C", "Gamma"],
]);

/** A credits file: the key is a formatting directive, so it repeats on nearly every row. */
const credits: MemberDocument = {
    filePath: "/t/creditstext.csv",
    languages: ["ENGLISH"],
    rows: [
        {
            index: 0,
            key: "HEADER",
            values: [{ language: "ENGLISH", value: "Directed by" }],
        },
        {
            index: 1,
            key: "CENTER",
            values: [{ language: "ENGLISH", value: "ALICE" }],
        },
        {
            index: 2,
            key: "CENTER",
            values: [{ language: "ENGLISH", value: "BOB" }],
        },
        {
            index: 3,
            key: "CENTER",
            values: [{ language: "ENGLISH", value: "[TBL]" }],
        },
        {
            index: 4,
            key: "HEADER",
            values: [{ language: "ENGLISH", value: "Produced by" }],
        },
        {
            index: 5,
            key: "CENTER",
            values: [{ language: "ENGLISH", value: "CAROL" }],
        },
    ],
};

describe("mergeMembers, on a single file", () => {
    // The bug this pins: merging is keyed, and a credits file keys every row by a formatting
    // directive repeated hundreds of times. Merging one collapsed it to one row per distinct
    // directive - a file that is all CENTER became a single line.
    it("keeps every row of a credits file, duplicate keys and all", () => {
        const merged = mergeMembers([credits]);

        assert.equal(merged.rows.length, 6);
        assert.deepEqual(
            merged.rows.map((r) => r.values[0].value),
            ["Directed by", "ALICE", "BOB", "[TBL]", "Produced by", "CAROL"],
        );
    });

    // Worse than a display bug: credits edits address a row by its position, so a re-indexed
    // table sends `setCell` at the wrong line and writes over the wrong row.
    it("keeps each row at its own position in the file", () => {
        assert.deepEqual(
            mergeMembers([credits]).rows.map((r) => r.index),
            [0, 1, 2, 3, 4, 5],
        );
    });

    // A duplicate key is exactly what the translation validator reports. Folding the two rows
    // together hid the row the finding was about.
    it("keeps a duplicate key in a translation file visible", () => {
        const duplicated = member("/t/mastertextfile.csv", "ENGLISH", [
            ["TEXT_A", "first"],
            ["TEXT_A", "second"],
        ]);

        const merged = mergeMembers([duplicated]);

        assert.equal(merged.rows.length, 2);
        assert.deepEqual(
            merged.rows.map((r) => r.values[0].value),
            ["first", "second"],
        );
    });

    it("still reports the file's languages", () => {
        assert.deepEqual(mergeMembers([credits]).languages, ["ENGLISH"]);
    });
});

describe("mergeMembers", () => {
    it("gives every file a column", () => {
        assert.deepEqual(mergeMembers([english, german]).languages, [
            "ENGLISH",
            "GERMAN",
        ]);
    });

    // The first file's order is the order it is written in, and a translator reading down the
    // column expects it - sorting would shuffle a hand-ordered file.
    it("unions the keys in first-seen order", () => {
        assert.deepEqual(
            mergeMembers([english, german]).rows.map((r) => r.key),
            ["TEXT_A", "TEXT_B", "TEXT_C"],
        );
    });

    it("puts each language value in its own column", () => {
        const row = mergeMembers([english, german]).rows[0];

        assert.deepEqual(row.values, [
            { language: "ENGLISH", value: "Alpha" },
            { language: "GERMAN", value: "Alfa" },
        ]);
    });

    // The gap is the point: a key one language has and another does not is exactly what the merged
    // view exists to show.
    it("leaves a blank where a language has no entry for a key", () => {
        const rows = mergeMembers([english, german]).rows;

        assert.equal(rows.find((r) => r.key === "TEXT_B")!.values[1].value, "");
        assert.equal(rows.find((r) => r.key === "TEXT_C")!.values[0].value, "");
    });

    it("renumbers rows so the table is addressable", () => {
        assert.deepEqual(
            mergeMembers([english, german]).rows.map((r) => r.index),
            [0, 1, 2],
        );
    });

    it("handles a set of one, which is what a single file is", () => {
        assert.deepEqual(mergeMembers([english]).languages, ["ENGLISH"]);
        assert.equal(mergeMembers([english]).rows.length, 2);
    });

    it("has nothing to show for no members", () => {
        assert.deepEqual(mergeMembers([]), { languages: [], rows: [] });
    });
});
