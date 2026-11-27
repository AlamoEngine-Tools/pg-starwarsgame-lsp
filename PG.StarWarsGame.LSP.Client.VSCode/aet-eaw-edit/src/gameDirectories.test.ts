// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { test } from "node:test";
import * as assert from "node:assert/strict";
import { gameDirectoryProblem } from "./gameDirectories";

const base = "aet-eaw-edit.lsp.source.baseGameDirectory";
const steam =
    "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Star Wars Empire at War";

// The folders that exist, for the check to look in.
const folders =
    (...existing: string[]) =>
    (folder: string) =>
        existing.some(
            (e) =>
                e.toLowerCase() ===
                folder.replace(/[\\/]+/g, "\\").toLowerCase(),
        );

test("a game directory that holds Data, or none set, is no problem", () => {
    const exists = folders(`${steam}\\GameData\\Data`);
    assert.equal(
        gameDirectoryProblem(base, `${steam}\\GameData`, exists),
        undefined,
    );
    assert.equal(
        gameDirectoryProblem(base, `${steam}\\GameData\\`, exists),
        undefined,
    );
    assert.equal(gameDirectoryProblem(base, "", exists), undefined);
});

// The reported setup (#393): the setting named Data itself, so the game was looked for in Data\Data.
test("a game directory that is a Data folder names the folder above", () => {
    const exists = folders(`${steam}\\GameData\\Data`);
    assert.equal(
        gameDirectoryProblem(base, `${steam}\\GameData\\Data`, exists),
        `EaWEdit: "${base}" points at the Data folder itself - ${steam}\\GameData\\Data. ` +
            `Set it to the folder above - ${steam}\\GameData`,
    );
    assert.equal(
        gameDirectoryProblem(base, `${steam}\\corruption\\data\\`, folders()),
        `EaWEdit: "${base}" points at the Data folder itself - ${steam}\\corruption\\data\\. ` +
            `Set it to the folder above - ${steam}\\corruption`,
    );
});

test("a game directory that holds no Data folder is reported", () => {
    assert.equal(
        gameDirectoryProblem(base, steam, folders(`${steam}\\GameData\\Data`)),
        `EaWEdit: "${base}" holds no Data folder - ${steam}. Fix it, or clear it to find the game by itself`,
    );
});

// A Linux install names its folders with forward slashes.
test("a Data folder is told apart with either separator", () => {
    assert.equal(
        gameDirectoryProblem(
            base,
            "/home/a/games/eaw/GameData/Data",
            folders(),
        ),
        `EaWEdit: "${base}" points at the Data folder itself - /home/a/games/eaw/GameData/Data. ` +
            "Set it to the folder above - /home/a/games/eaw/GameData",
    );
});
