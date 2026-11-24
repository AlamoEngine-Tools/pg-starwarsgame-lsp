// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import type { MementoScope } from "./mementoSlot";
import { DEFAULT_ENCYCLOPEDIA_SCREEN } from "./shared/encyclopediaScreens";
import { DEFAULT_PROJECT_SETTINGS } from "./shared/projectSettings";
import {
    createStores,
    encyclopediaScreenParams,
    readProjectSettings,
    saveDialogGeometry,
    savePanelSize,
    saveProjectSettings,
} from "./stores";

/** A memento over a plain map. */
function memento(initial: Record<string, unknown> = {}) {
    const values = new Map<string, unknown>(Object.entries(initial));
    const scope: MementoScope = {
        get: <T>(key: string) => values.get(key) as T | undefined,
        update: (key, value) => {
            values.set(key, value);
            return Promise.resolve();
        },
    };
    return { values, scope };
}

function stores() {
    const global = memento();
    const workspace = memento();
    return {
        global: global.values,
        workspace: workspace.values,
        stores: createStores(global.scope, workspace.scope),
    };
}

describe("stores", () => {
    /**
     * A value written under another key, or into the other memento, is a value every existing
     * install loses on the update.
     */
    it("keeps every value under the key and in the memento it always had", () => {
        const { stores: s, global, workspace } = stores();

        saveDialogGeometry(s, "credits", {
            xRatio: 0.1,
            yRatio: 0.2,
            width: 400,
            height: 300,
        });
        savePanelSize(s, "storyGraph.dock", 320);
        s.viewerSettings.save({});
        saveProjectSettings(s, "d:/mods/a/a.pgproj", {});
        s.encyclopediaScreen.save(DEFAULT_ENCYCLOPEDIA_SCREEN);
        s.chosenProject.save("d:/mods/a/a.pgproj");
        s.projectOfferDeclined.save(["d:/mods/b"]);
        s.whatsNewSeenVersion.save("0.6.0");

        assert.deepEqual([...global.keys()].sort(), [
            "aet-eaw-edit.encyclopediaScreen",
            "aet-eaw-edit.panelLayout",
            "aet-eaw-edit.previewViewerSettings",
            "aet-eaw-edit.projectOfferDeclined",
            "whatsNew.seenVersion",
        ]);
        assert.deepEqual([...workspace.keys()].sort(), [
            "aet-eaw-edit.chosenProject",
            "aet-eaw-edit.dialogGeometry",
            "aet-eaw-edit.previewProjectSettings.byProject",
        ]);
    });
});

describe("project settings", () => {
    it("keeps one project's settings apart from another's in the same window", () => {
        const { stores: s } = stores();

        saveProjectSettings(s, "d:/mods/a/a.pgproj", { faction: "Empire" });
        saveProjectSettings(s, "d:/mods/b/b.pgproj", { faction: "Rebel" });

        assert.equal(
            readProjectSettings(s, "d:/mods/a/a.pgproj").faction,
            "Empire",
        );
        assert.equal(
            readProjectSettings(s, "d:/mods/b/b.pgproj").faction,
            "Rebel",
        );
    });

    it("answers a project that stored nothing with the defaults", () => {
        const { stores: s } = stores();

        assert.deepEqual(
            readProjectSettings(s, "d:/mods/c/c.pgproj"),
            DEFAULT_PROJECT_SETTINGS,
        );
    });

    it("normalises what it stores, so a bad value never comes back", () => {
        const { stores: s } = stores();

        saveProjectSettings(s, "d:/mods/a/a.pgproj", {
            faction: 42,
            presets: "no",
        });

        const settings = readProjectSettings(s, "d:/mods/a/a.pgproj");
        assert.equal(settings.faction, null);
        assert.deepEqual(settings.presets, []);
    });
});

describe("dialog geometry", () => {
    const placed = { xRatio: 0.1, yRatio: 0.2, width: 400, height: 300 };

    it("keeps each dialog's placement apart", () => {
        const { stores: s } = stores();

        saveDialogGeometry(s, "credits", placed);
        saveDialogGeometry(s, "convert", { ...placed, width: 600 });

        assert.deepEqual(s.dialogGeometry.read(), {
            credits: placed,
            convert: { ...placed, width: 600 },
        });
    });

    it("keeps the placement a dialog had when a broken one arrives", () => {
        const { stores: s } = stores();
        saveDialogGeometry(s, "credits", placed);

        saveDialogGeometry(s, "credits", { ...placed, width: Number.NaN });
        saveDialogGeometry(s, "credits", "wide");

        assert.deepEqual(s.dialogGeometry.read(), { credits: placed });
    });

    it("never hands back a broken placement that was stored", () => {
        const workspace = memento({
            "aet-eaw-edit.dialogGeometry": {
                credits: placed,
                convert: { xRatio: 0, yRatio: 0, width: "wide", height: 1 },
            },
        });
        const s = createStores(memento().scope, workspace.scope);

        assert.deepEqual(s.dialogGeometry.read(), { credits: placed });
    });
});

describe("panel layout", () => {
    it("records one size and leaves the others", () => {
        const { stores: s } = stores();

        savePanelSize(s, "storyGraph.dock", 320);
        savePanelSize(s, "preview.dock", 280);

        assert.deepEqual(s.panelLayout.read(), {
            "storyGraph.dock": 320,
            "preview.dock": 280,
        });
    });
});

describe("encyclopedia screen", () => {
    it("names the stored screen as the request takes it", () => {
        const { stores: s } = stores();

        assert.deepEqual(encyclopediaScreenParams(s), {
            screenWidth: DEFAULT_ENCYCLOPEDIA_SCREEN.width,
            screenHeight: DEFAULT_ENCYCLOPEDIA_SCREEN.height,
        });
    });
});

describe("window values", () => {
    it("has no chosen project and no seen version until one is stored", () => {
        const { stores: s } = stores();

        assert.equal(s.chosenProject.read(), undefined);
        assert.equal(s.whatsNewSeenVersion.read(), undefined);
        assert.deepEqual(s.projectOfferDeclined.read(), []);
    });

    it("reads back the folders declined, dropping what is not a folder", () => {
        const global = memento({
            "aet-eaw-edit.projectOfferDeclined": ["d:\\mods\\a", 7, null],
        });
        const s = createStores(global.scope, memento().scope);

        assert.deepEqual(s.projectOfferDeclined.read(), ["d:\\mods\\a"]);
    });
});
