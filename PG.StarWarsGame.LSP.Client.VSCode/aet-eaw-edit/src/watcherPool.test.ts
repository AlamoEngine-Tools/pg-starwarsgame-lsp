// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { WatcherPool, type FileEvent } from "./watcherPool";

/** A pool over fake watchers that records what was created and lets a test fire events. */
function fakePool() {
    const created: string[] = [];
    const disposed: string[] = [];
    const fire = new Map<string, (event: FileEvent) => void>();
    const pool = new WatcherPool((directory, glob, emit) => {
        const key = `${directory}|${glob}`;
        created.push(key);
        fire.set(key, emit);
        return { dispose: () => disposed.push(key) };
    });
    return { pool, created, disposed, fire };
}

/**
 * Watchers over directories outside the workspace folders, shared between servers (#229).
 *
 * Two projects referencing one dependency both need its changes; one OS watcher per directory and
 * glob serves both, and it goes when the last server that asked for it lets go.
 */
describe("WatcherPool", () => {
    it("creates one watcher per directory and glob, however many servers ask", () => {
        const { pool, created } = fakePool();

        pool.acquire("c:/dev/EaWX/Data", ["**/*.xml"], () => undefined);
        pool.acquire("c:/dev/EaWX/Data", ["**/*.xml"], () => undefined);

        assert.deepEqual(created, ["c:/dev/EaWX/Data|**/*.xml"]);
    });

    it("tells every server that asked", () => {
        const { pool, fire } = fakePool();
        const rev: FileEvent[] = [];
        const fotr: FileEvent[] = [];
        pool.acquire("c:/dev/EaWX/Data", ["**/*.xml"], (e) => rev.push(e));
        pool.acquire("c:/dev/EaWX/Data", ["**/*.xml"], (e) => fotr.push(e));

        fire.get("c:/dev/EaWX/Data|**/*.xml")!({
            path: "c:/dev/EaWX/Data/a.xml",
            type: 2,
        });

        assert.equal(rev.length, 1);
        assert.equal(fotr.length, 1);
    });

    it("keeps the watcher until the last server lets go", () => {
        const { pool, disposed, fire } = fakePool();
        const rev: FileEvent[] = [];
        const first = pool.acquire("c:/d", ["**/*.xml"], (e) => rev.push(e));
        const second = pool.acquire("c:/d", ["**/*.xml"], () => undefined);

        first.dispose();
        fire.get("c:/d|**/*.xml")!({ path: "c:/d/a.xml", type: 1 });
        assert.deepEqual(disposed, []);
        assert.equal(rev.length, 0);

        second.dispose();
        assert.deepEqual(disposed, ["c:/d|**/*.xml"]);
    });

    it("treats one directory spelled two ways as one", () => {
        const { pool, created } = fakePool();

        pool.acquire("c:/dev/Data", ["**/*.lua"], () => undefined);
        pool.acquire("C:\\dev\\Data", ["**/*.lua"], () => undefined);

        assert.equal(created.length, 1);
    });
});
