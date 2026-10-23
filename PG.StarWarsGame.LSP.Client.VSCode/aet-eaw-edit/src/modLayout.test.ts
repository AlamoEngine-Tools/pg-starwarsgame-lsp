// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { isModFolder, offersProject } from "./modLayout";

function tree(directories: Record<string, string[]>) {
    return (directory: string) => directories[directory];
}

const files = tree;
const none = tree({});

/** The Data folders a mod may hold without any XML of its own. */
const MOD_DATA_FOLDERS = ["XML", "Scripts", "Art", "Audio", "Text"];

const GAME_EXECUTABLES = [
    "sweaw.exe",
    "swfoc.exe",
    "StarWarsG.exe",
    "StarWarsI.exe",
];

describe("isModFolder", () => {
    it("takes a folder holding Data/XML", () => {
        const list = tree({
            "c:/mod": ["Data", "readme.txt"],
            "c:/mod/Data": ["XML", "Art"],
        });

        assert.equal(isModFolder("c:/mod", list, none), true);
    });

    it("takes a folder whose Data holds any one game folder", () => {
        for (const folder of MOD_DATA_FOLDERS) {
            const list = tree({ "c:/mod": ["Data"], "c:/mod/Data": [folder] });

            assert.equal(isModFolder("c:/mod", list, none), true, folder);
        }
    });

    it("takes a folder whose Data holds only MEG archives", () => {
        const list = tree({ "c:/mod": ["Data"], "c:/mod/Data": [] });
        const held = files({ "c:/mod/Data": ["MegaFiles.xml", "Mod.MEG"] });

        assert.equal(isModFolder("c:/mod", list, held), true);
    });

    it("takes a folder holding modinfo.json and no Data", () => {
        const held = files({ "c:/mod": ["modinfo.json"] });

        assert.equal(isModFolder("c:/mod", tree({ "c:/mod": [] }), held), true);
    });

    it("takes a window opened at Data, which holds XML", () => {
        assert.equal(
            isModFolder("c:/mod/Data", tree({ "c:/mod/Data": ["XML"] }), none),
            true,
        );
    });

    it("takes a window opened at Data, which holds Scripts", () => {
        const list = tree({ "c:\\mod\\data": ["Scripts"] });

        assert.equal(isModFolder("c:\\mod\\data", list, none), true);
    });

    it("matches the folder names in any case, as the game reads them", () => {
        const list = tree({ "c:/mod": ["data"], "c:/mod/data": ["xml"] });

        assert.equal(isModFolder("c:/mod", list, none), true);
    });

    /** A bare xml folder is common in any repository; a mod keeps it under Data. */
    it("refuses a folder holding XML that is not named Data", () => {
        const list = tree({ "c:/repo": ["xml", "src"] });

        assert.equal(isModFolder("c:/repo", list, none), false);
    });

    it("refuses a folder whose Data holds no game folder", () => {
        const list = tree({
            "c:/notes": ["Data", "todo.xml"],
            "c:/notes/Data": ["Docs"],
        });

        assert.equal(isModFolder("c:/notes", list, none), false);
    });

    /** The game's own folder is laid out the same way, and it is not a mod. */
    it("refuses the game's own folder and its Data", () => {
        const list = tree({ "c:/eaw": ["Data"], "c:/eaw/Data": ["XML"] });

        for (const exe of GAME_EXECUTABLES) {
            const held = files({ "c:/eaw": [exe] });

            assert.equal(isModFolder("c:/eaw", list, held), false, exe);
            assert.equal(isModFolder("c:/eaw/Data", list, held), false, exe);
        }
    });

    it("refuses a folder it cannot read", () => {
        assert.equal(isModFolder("c:/gone", none, none), false);
    });
});

describe("offersProject", () => {
    it("offers a project for a folder holding Data/XML", () => {
        const dirs = tree({ "c:/mod": ["Data"], "c:/mod/Data": ["XML"] });

        assert.equal(offersProject("c:/mod", dirs, none), true);
    });

    it("offers a project for a mod with no XML of its own", () => {
        const dirs = tree({ "c:/mod": ["Data"], "c:/mod/Data": ["Scripts"] });

        assert.equal(offersProject("c:/mod", dirs, none), true);
    });

    /** The project file goes beside Data; a window opened at Data has no such place. */
    it("offers none for a window opened at Data", () => {
        const dirs = tree({ "c:/mod/Data": ["XML"] });

        assert.equal(offersProject("c:/mod/Data", dirs, none), false);
    });

    /** The game's own folder is laid out the same way, and it is not a mod. */
    it("offers none for the game's own folder", () => {
        const dirs = tree({ "c:/eaw": ["Data"], "c:/eaw/Data": ["XML"] });

        for (const exe of GAME_EXECUTABLES) {
            assert.equal(
                offersProject("c:/eaw", dirs, files({ "c:/eaw": [exe] })),
                false,
                exe,
            );
        }
    });
});
