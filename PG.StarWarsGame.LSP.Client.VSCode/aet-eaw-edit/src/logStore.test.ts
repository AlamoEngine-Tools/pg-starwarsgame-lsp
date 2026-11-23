// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";

import {
    STALE_AFTER_DAYS,
    logFolderLabel,
    logFolderOf,
    logStoreOf,
    removeStaleLogFolders,
    workspaceIdOf,
} from "./logStore";

const DAY = 24 * 60 * 60 * 1000;

describe("workspaceIdOf", () => {
    it("is the folder VS Code files the extension's workspace storage under", () => {
        const storage = path.join(
            "C:",
            "Users",
            "someone",
            "AppData",
            "Roaming",
            "Code",
            "User",
            "workspaceStorage",
            "3f9c1a7e5b2d4c6f",
            "alamoenginetools.aet-eaw-edit",
        );

        assert.equal(workspaceIdOf(storage), "3f9c1a7e5b2d4c6f");
    });

    it("is the same for the same storage folder, so a restart logs where the last run did", () => {
        const storage = path.join(
            "data",
            "workspaceStorage",
            "1731000000000",
            "ext",
        );

        assert.equal(workspaceIdOf(storage), workspaceIdOf(storage));
        assert.equal(workspaceIdOf(storage), "1731000000000");
    });

    it("differs between two workspaces", () => {
        assert.notEqual(
            workspaceIdOf(path.join("data", "workspaceStorage", "aaaa", "ext")),
            workspaceIdOf(path.join("data", "workspaceStorage", "bbbb", "ext")),
        );
    });

    it("names a window without a workspace rather than failing", () => {
        assert.equal(workspaceIdOf(undefined), "no-workspace");
    });

    it("keeps only what a folder name holds", () => {
        assert.equal(
            workspaceIdOf(
                path.join("data", "workspaceStorage", "a b:c", "ext"),
            ),
            "a_b_c",
        );
    });
});

describe("logFolderOf", () => {
    it("is the workspace's folder in the user's log store", () => {
        const home = path.join("C:", "Users", "someone");

        assert.equal(logStoreOf(home), path.join(home, ".aetswg", "logs"));
        assert.equal(
            logFolderOf(home, "3f9c"),
            path.join(home, ".aetswg", "logs", "3f9c"),
        );
    });

    it("is named from the home directory in a report, never with the user's own path", () => {
        assert.equal(logFolderLabel("3f9c"), "~/.aetswg/logs/3f9c");
    });
});

describe("removeStaleLogFolders", () => {
    let store: string;
    const now = new Date(2026, 10, 23, 12, 0, 0);

    function folder(name: string, files: Record<string, number>): string {
        const directory = path.join(store, name);
        fs.mkdirSync(directory, { recursive: true });
        for (const [file, ageInDays] of Object.entries(files)) {
            const written = new Date(now.getTime() - ageInDays * DAY);
            fs.writeFileSync(path.join(directory, file), "line");
            fs.utimesSync(path.join(directory, file), written, written);
        }
        return directory;
    }

    function aged(directory: string, ageInDays: number): void {
        const written = new Date(now.getTime() - ageInDays * DAY);
        fs.utimesSync(directory, written, written);
    }

    beforeEach(() => {
        store = fs.mkdtempSync(path.join(os.tmpdir(), "aet-logstore-"));
    });

    afterEach(() => {
        fs.rmSync(store, { recursive: true, force: true });
    });

    it("removes a workspace's folder nothing wrote to for the period", () => {
        const old = folder("old", {
            "aetswg-Mod-20260101.log": STALE_AFTER_DAYS + 1,
        });
        aged(old, STALE_AFTER_DAYS + 1);

        assert.deepEqual(removeStaleLogFolders(store, "current", now), ["old"]);
        assert.equal(fs.existsSync(old), false);
    });

    it("keeps a folder with one file written inside the period", () => {
        const used = folder("used", {
            "aetswg-Mod-20260101.log": STALE_AFTER_DAYS + 20,
            "aetswg-Mod-20261120.log": 3,
        });
        aged(used, STALE_AFTER_DAYS + 20);

        assert.deepEqual(removeStaleLogFolders(store, "current", now), []);
        assert.equal(fs.existsSync(used), true);
    });

    it("never removes the folder of the workspace that is open", () => {
        const current = folder("current", {
            "aetswg-Mod-20260101.log": STALE_AFTER_DAYS + 90,
        });
        aged(current, STALE_AFTER_DAYS + 90);

        assert.deepEqual(removeStaleLogFolders(store, "current", now), []);
        assert.equal(fs.existsSync(current), true);
    });

    it("removes an empty folder by its own age", () => {
        const empty = folder("empty", {});
        aged(empty, STALE_AFTER_DAYS + 1);
        const fresh = folder("fresh", {});
        aged(fresh, 1);

        assert.deepEqual(removeStaleLogFolders(store, "current", now), [
            "empty",
        ]);
        assert.equal(fs.existsSync(fresh), true);
    });

    it("leaves a file beside the folders alone", () => {
        fs.writeFileSync(path.join(store, "note.txt"), "kept");
        const written = new Date(now.getTime() - 400 * DAY);
        fs.utimesSync(path.join(store, "note.txt"), written, written);

        assert.deepEqual(removeStaleLogFolders(store, "current", now), []);
        assert.equal(fs.existsSync(path.join(store, "note.txt")), true);
    });

    it("does nothing when the store does not exist yet", () => {
        assert.deepEqual(
            removeStaleLogFolders(path.join(store, "missing"), "current", now),
            [],
        );
    });
});
