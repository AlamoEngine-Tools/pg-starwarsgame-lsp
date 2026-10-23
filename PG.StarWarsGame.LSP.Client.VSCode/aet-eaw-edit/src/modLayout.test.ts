// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { isModFolder, offersProject } from "./modLayout";

function tree(directories: Record<string, string[]>) {
    return (directory: string) => directories[directory];
}

describe("isModFolder", () => {
    it("takes a folder holding Data/XML", () => {
        const list = tree({
            "c:/mod": ["Data", "readme.txt"],
            "c:/mod/Data": ["XML", "Art"],
        });

        assert.equal(isModFolder("c:/mod", list), true);
    });

    it("takes a window opened at Data, which holds XML", () => {
        assert.equal(
            isModFolder("c:/mod/Data", tree({ "c:/mod/Data": ["XML"] })),
            true,
        );
    });

    it("matches the folder names in any case, as the game reads them", () => {
        const list = tree({ "c:/mod": ["data"], "c:/mod/data": ["xml"] });

        assert.equal(isModFolder("c:/mod", list), true);
    });

    it("refuses a folder with no XML folder where a mod keeps it", () => {
        const list = tree({
            "c:/notes": ["Data", "todo.xml"],
            "c:/notes/Data": ["Art"],
        });

        assert.equal(isModFolder("c:/notes", list), false);
    });

    it("refuses a folder it cannot read", () => {
        assert.equal(isModFolder("c:/gone", tree({})), false);
    });
});

describe("offersProject", () => {
    const files = (entries: Record<string, string[]>) => (directory: string) =>
        entries[directory];

    it("offers a project for a folder holding Data/XML", () => {
        const dirs = tree({ "c:/mod": ["Data"], "c:/mod/Data": ["XML"] });

        assert.equal(offersProject("c:/mod", dirs, files({})), true);
    });

    /** The project file goes beside Data; a window opened at Data has no such place. */
    it("offers none for a window opened at Data", () => {
        const dirs = tree({ "c:/mod/Data": ["XML"] });

        assert.equal(offersProject("c:/mod/Data", dirs, files({})), false);
    });

    /** The game's own folder is laid out the same way, and it is not a mod. */
    it("offers none for the game's own folder", () => {
        const dirs = tree({ "c:/eaw": ["Data"], "c:/eaw/Data": ["XML"] });

        for (const exe of [
            "sweaw.exe",
            "swfoc.exe",
            "StarWarsG.exe",
            "StarWarsI.exe",
        ]) {
            assert.equal(
                offersProject("c:/eaw", dirs, files({ "c:/eaw": [exe] })),
                false,
                exe,
            );
        }
    });
});
