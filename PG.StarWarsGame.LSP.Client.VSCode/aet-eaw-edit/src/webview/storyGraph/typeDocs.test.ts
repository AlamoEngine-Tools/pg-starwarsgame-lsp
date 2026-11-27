// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { noteLine, noteSeverity, paramKind, pillSeverity } from "./typeDocs";

describe("pillSeverity", () => {
    it("is plain information for a type the schema has nothing against", () => {
        assert.equal(pillSeverity([]), "info");
        assert.equal(pillSeverity(undefined), "info");
        assert.equal(pillSeverity([{ kind: "Remark", text: "Rare." }]), "info");
    });

    /** The pill is the tile's one corner mark: it opens the documentation AND says there is a problem. */
    it("marks a type the engine does not run as an error, an untested one as a warning", () => {
        assert.equal(pillSeverity([{ kind: "BuggedInEngine" }]), "error");
        assert.equal(pillSeverity([{ kind: "Untested" }]), "warning");
        assert.equal(pillSeverity([{ kind: "Deprecated" }]), "warning");
    });

    it("takes the first note, which the server has ranked worst", () => {
        assert.equal(
            pillSeverity([{ kind: "BuggedInEngine" }, { kind: "Remark" }]),
            "error",
        );
    });
});

describe("noteSeverity", () => {
    it("shows a kind this build has never heard of as information", () => {
        assert.equal(noteSeverity({ kind: "FromANewerSchema" }), "info");
    });
});

describe("noteLine", () => {
    it("says what kind of note it is, then the note", () => {
        assert.equal(
            noteLine({ kind: "BuggedInEngine", text: "Nothing raises it." }),
            "Does not work: Nothing raises it.",
        );
        assert.equal(
            noteLine({ kind: "Remark", text: "Used twice in vanilla." }),
            "Remark: Used twice in vanilla.",
        );
    });

    it("is the kind alone for a note without words", () => {
        assert.equal(noteLine({ kind: "Untested" }), "Untested");
    });

    /** A node drops the version a type appeared in; its documentation is where that belongs. */
    it("gives the version for a Since note", () => {
        assert.equal(
            noteLine({ kind: "Since", value: "FoC 1.1" }),
            "Since FoC 1.1",
        );
    });
});

describe("paramKind", () => {
    const param = { position: 0, optional: false };

    it("is the value type, and what it refers to where it refers to something", () => {
        assert.equal(paramKind({ ...param, valueType: "Int" }), "Int");
        assert.equal(
            paramKind({
                ...param,
                valueType: "NameReferenceList",
                referenceType: "Planet",
            }),
            "NameReferenceList - Planet",
        );
        assert.equal(
            paramKind({
                ...param,
                valueType: "DynamicEnumValue",
                enumName: "StoryEventFilter",
            }),
            "DynamicEnumValue - StoryEventFilter",
        );
    });
});
