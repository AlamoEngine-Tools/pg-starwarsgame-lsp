// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Which of the server's watch directories this client has to watch itself.
 *
 * `createFileSystemWatcher` with a bare glob watches the open workspace FOLDERS and nothing else,
 * so a project reached through `projectReferences` - which normally sits outside all of them - was
 * invisible: editing its XML, its scripts, its localisation or its own `.pgproj` produced no
 * `didChangeWatchedFiles`, so nothing re-indexed until the server was restarted. Those directories
 * need a watcher built from an absolute `RelativePattern` instead.
 *
 * Pure: no VS Code and no file system, so it runs under the node test harness.
 */

/** Forward slashes, no trailing separator, lower case - the only form these are compared in. */
function canonical(path: string): string {
    return path.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
}

/**
 * True when `outer` is `inner` or an ancestor of it. The separator check is what keeps `Rev2` from
 * counting as covered by a folder open on `Rev`.
 */
function covers(outer: string, inner: string): boolean {
    return inner === outer || inner.startsWith(outer + '/');
}

/**
 * The reported directories that no workspace folder already covers, in the order the server gave
 * them, each appearing once and keeping the spelling the server used.
 */
export function directoriesNeedingOwnWatcher(
    reported: readonly string[],
    workspaceFolders: readonly string[],
): string[] {
    const folders = workspaceFolders.map(canonical);
    const seen = new Set<string>();
    const need: string[] = [];

    for (const directory of reported) {
        const key = canonical(directory);
        if (key.length === 0 || seen.has(key)) {
            continue;
        }
        seen.add(key);
        if (!folders.some(folder => covers(folder, key))) {
            need.push(directory);
        }
    }

    return need;
}
