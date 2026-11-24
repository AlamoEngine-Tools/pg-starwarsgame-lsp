// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    previewAssetsChanged,
    previewEffectsChanged,
    restartReason,
} from "./settingsRestart";

/** A configuration change touching exactly these settings, as `affectsConfiguration` answers it. */
function change(...changed: string[]): (section: string) => boolean {
    return (section) =>
        changed.some(
            (setting) =>
                setting === section || setting.startsWith(section + "."),
        );
}

/** The experimental flags, and the two executables picked when the process starts. */
const FEATURES = [
    "aet-eaw-edit.experimental.storyEditing",
    "aet-eaw-edit.experimental.storySimulator",
    "aet-eaw-edit.lsp.executable",
    "aet-eaw-edit.lua.analyzerPath",
];

/** Where effects, models and textures are read from (#395). */
const SHADER_FOLDER = "aet-eaw-edit.shaders.directory";
const GAME_FOLDERS = [
    "aet-eaw-edit.lsp.source.baseGameDirectory",
    "aet-eaw-edit.lsp.source.expansionDirectory",
];

/** The rest of what travels in the initialization options only (#395). */
const STARTUP = [
    "aet-eaw-edit.lsp.localisation.language",
    "aet-eaw-edit.lsp.schema.source",
    "aet-eaw-edit.lsp.schema.url",
    "aet-eaw-edit.lsp.schema.localPath",
    "aet-eaw-edit.lsp.source.baseline.type",
    "aet-eaw-edit.lsp.source.baseline.url",
    "aet-eaw-edit.lsp.source.baseline.localPath",
    "aet-eaw-edit.diagnostics.workspaceOnStartup",
];

describe("restartReason, as whether a change restarts the servers", () => {
    for (const setting of [
        ...FEATURES,
        SHADER_FOLDER,
        ...GAME_FOLDERS,
        ...STARTUP,
    ]) {
        it(`restarts for ${setting}`, () => {
            assert.notEqual(restartReason(change(setting)), null);
        });
    }

    it("does not restart for a setting the panels read themselves", () => {
        assert.equal(
            restartReason(change("aet-eaw-edit.features.preview.energyPool")),
            null,
        );
    });
});

describe("restartReason", () => {
    // The first two lines are matched by LUA-02, SET-08 and SET-20; they stay as they are.
    for (const setting of FEATURES) {
        it(`names ${setting} a feature or executable`, () => {
            assert.equal(restartReason(change(setting)), "features");
        });
    }

    for (const setting of [SHADER_FOLDER, ...GAME_FOLDERS]) {
        it(`names ${setting} a folder`, () => {
            assert.equal(restartReason(change(setting)), "folders");
        });
    }

    for (const setting of STARTUP) {
        it(`names ${setting} a startup setting`, () => {
            assert.equal(restartReason(change(setting)), "startup");
        });
    }

    it("names nothing for a change that needs no restart", () => {
        assert.equal(
            restartReason(change("aet-eaw-edit.features.preview.energyPool")),
            null,
        );
    });
});

describe("previewEffectsChanged", () => {
    for (const setting of [SHADER_FOLDER, ...GAME_FOLDERS]) {
        it(`is true for ${setting}`, () => {
            assert.equal(previewEffectsChanged(change(setting)), true);
        });
    }

    it("is not a restart for anything else", () => {
        for (const setting of [...FEATURES, ...STARTUP]) {
            assert.equal(
                previewEffectsChanged(change(setting)),
                false,
                setting,
            );
        }
    });
});

describe("previewAssetsChanged", () => {
    // Models and textures resolve through the game folders, so an open preview's subject has to be
    // read again in full; the shader folder holds effects only.
    for (const setting of GAME_FOLDERS) {
        it(`is true for ${setting}`, () => {
            assert.equal(previewAssetsChanged(change(setting)), true);
        });
    }

    it("is false for the shader folder and everything else", () => {
        for (const setting of [SHADER_FOLDER, ...FEATURES, ...STARTUP]) {
            assert.equal(previewAssetsChanged(change(setting)), false, setting);
        }
    });
});
