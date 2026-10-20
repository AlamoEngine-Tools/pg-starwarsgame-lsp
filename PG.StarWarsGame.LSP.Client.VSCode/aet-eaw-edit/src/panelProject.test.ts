// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { projectSubjectKey, projectTitle } from "./panelProject";

describe("projectSubjectKey", () => {
    it("keeps one subject of two projects apart", () => {
        assert.notEqual(
            projectSubjectKey(
                "c:/dev/eawx/rev/rev.pgproj",
                "data/text/mastertextfile_english.dat",
            ),
            projectSubjectKey(
                "c:/dev/eawx/fotr/fotr.pgproj",
                "data/text/mastertextfile_english.dat",
            ),
        );
    });

    it("never collapses to the subject alone", () => {
        assert.notEqual(projectSubjectKey("c:/dev/mod/mod.pgproj", "x"), "x");
    });
});

describe("projectTitle", () => {
    it("is the panel's own title while the window runs one project", () => {
        assert.equal(projectTitle("Encyclopedia", "Rev", 1), "Encyclopedia");
    });

    it("names the project once the window runs several", () => {
        assert.equal(
            projectTitle("Encyclopedia", "Rev", 2),
            "Encyclopedia - Rev",
        );
    });

    it("stays the panel's own title when no project is named", () => {
        assert.equal(projectTitle("Encyclopedia", "", 2), "Encyclopedia");
    });
});
