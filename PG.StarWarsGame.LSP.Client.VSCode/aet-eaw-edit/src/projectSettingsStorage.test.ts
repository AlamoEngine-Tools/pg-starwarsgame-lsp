// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    initProjectSettingsStorage,
    readProjectSettings,
    saveProjectSettings,
} from "./projectSettingsStorage";
import { DEFAULT_PROJECT_SETTINGS } from "./webview/preview/projectSettings";

/** The two members of `vscode.Memento` the store uses, over a plain map. */
function memento() {
    const values = new Map<string, unknown>();
    return {
        values,
        keys: () => [...values.keys()],
        get: <T>(key: string, defaultValue?: T) =>
            (values.has(key) ? values.get(key) : defaultValue) as T,
        update: (key: string, value: unknown) => {
            values.set(key, value);
            return Promise.resolve();
        },
    };
}

describe("project settings storage", () => {
    it("keeps one project's settings apart from another's in the same window", async () => {
        const store = memento();
        initProjectSettingsStorage(store);

        saveProjectSettings("d:/mods/a/a.pgproj", { faction: "Empire" });
        saveProjectSettings("d:/mods/b/b.pgproj", { faction: "Rebel" });
        await Promise.resolve();

        assert.equal(
            readProjectSettings("d:/mods/a/a.pgproj").faction,
            "Empire",
        );
        assert.equal(
            readProjectSettings("d:/mods/b/b.pgproj").faction,
            "Rebel",
        );
    });

    it("answers a project that stored nothing with the defaults", () => {
        initProjectSettingsStorage(memento());

        assert.deepEqual(
            readProjectSettings("d:/mods/c/c.pgproj"),
            DEFAULT_PROJECT_SETTINGS,
        );
    });

    it("normalises what it stores, so a bad value never comes back", async () => {
        const store = memento();
        initProjectSettingsStorage(store);

        saveProjectSettings("d:/mods/a/a.pgproj", {
            faction: 42,
            presets: "no",
        });
        await Promise.resolve();

        const settings = readProjectSettings("d:/mods/a/a.pgproj");
        assert.equal(settings.faction, null);
        assert.deepEqual(settings.presets, []);
    });
});
