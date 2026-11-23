// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where the servers of a window write their log files: one folder per workspace in the user's own
// store, `<home>/.aetswg/logs/<workspace id>/`, never inside a project (#461). A log written into
// the project sat in the mod's folder, in its version control and in anything packaged from it.
// Kept free of `vscode` so the unit harness can run it.

import * as fs from "fs";
import * as path from "path";

/** A workspace's folder nothing wrote to for this many days is removed at startup. */
export const STALE_AFTER_DAYS = 30;

const DAY = 24 * 60 * 60 * 1000;

/** The folder of a window that has no workspace storage: no folder is open in it. */
const NO_WORKSPACE = "no-workspace";

/**
 * The workspace's id. VS Code files the extension's workspace storage under
 * `<user data>/workspaceStorage/<workspace id>/<extension id>`, keeps that id for as long as the
 * workspace exists and gives two workspaces two ids, so it is used as it is rather than an id made
 * up here.
 *
 * @param storagePath The extension's workspace storage folder, or undefined with no workspace.
 */
export function workspaceIdOf(storagePath: string | undefined): string {
    if (storagePath === undefined) {
        return NO_WORKSPACE;
    }
    const id = path
        .basename(path.dirname(storagePath))
        .replace(/[^A-Za-z0-9._-]/g, "_");
    return id === "" || id === "." || id === ".." ? NO_WORKSPACE : id;
}

/** The user's log store, beside the schema and baseline caches the server keeps there. */
export function logStoreOf(home: string): string {
    return path.join(home, ".aetswg", "logs");
}

/** The folder every server of one workspace logs into. */
export function logFolderOf(home: string, workspaceId: string): string {
    return path.join(logStoreOf(home), workspaceId);
}

/**
 * The folder as a report names it: from the home directory, whoever's that is. The bug report goes
 * into public issues, and the folder's own path carries the user's name.
 */
export function logFolderLabel(workspaceId: string): string {
    return `~/.aetswg/logs/${workspaceId}`;
}

/**
 * Removes every workspace's folder in the store that nothing wrote to for
 * {@link STALE_AFTER_DAYS}: a workspace that is gone leaves its folder behind for good otherwise.
 * A folder that cannot be read or removed is left for the next start.
 *
 * @param keep The id of the workspace that is open; its folder stays whatever its age.
 * @returns The names of the folders removed.
 */
export function removeStaleLogFolders(
    store: string,
    keep: string,
    now: Date,
): string[] {
    let entries: fs.Dirent[];
    try {
        entries = fs.readdirSync(store, { withFileTypes: true });
    } catch {
        return [];
    }

    const oldest = now.getTime() - STALE_AFTER_DAYS * DAY;
    const removed: string[] = [];
    for (const entry of entries) {
        if (!entry.isDirectory() || entry.name === keep) {
            continue;
        }
        const folder = path.join(store, entry.name);
        try {
            if (lastWriteOf(folder) < oldest) {
                fs.rmSync(folder, { recursive: true, force: true });
                removed.push(entry.name);
            }
        } catch {
            // Held open by a server of another window, or gone already.
        }
    }
    return removed;
}

/** The newest write in a folder: its newest file's, or its own when it holds none. */
function lastWriteOf(folder: string): number {
    let newest = fs.statSync(folder).mtimeMs;
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
        if (entry.isFile()) {
            newest = Math.max(
                newest,
                fs.statSync(path.join(folder, entry.name)).mtimeMs,
            );
        }
    }
    return newest;
}
