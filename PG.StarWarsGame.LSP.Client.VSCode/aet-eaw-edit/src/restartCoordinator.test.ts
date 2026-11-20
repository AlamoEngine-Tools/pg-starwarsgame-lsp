// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { type RestartHost, RestartCoordinator } from "./restartCoordinator";

/** Lets every other pending step run, as a real stop or start does while it waits on a process. */
const yieldNow = (): Promise<void> =>
    new Promise((resolve) => setImmediate(resolve));

/** A host whose servers stop and start over a few turns, recording every step in order. */
function harness(): {
    host: RestartHost;
    events: string[];
    state: { running: boolean; valid: boolean; settings: string };
} {
    const events: string[] = [];
    const state = { running: true, valid: true, settings: "first" };

    const host: RestartHost = {
        running: () => state.running,
        validate: () => {
            events.push(state.valid ? "valid" : "invalid, reported");
            return state.valid;
        },
        stop: async () => {
            events.push("stop");
            await yieldNow();
            await yieldNow();
            state.running = false;
            events.push("stopped");
        },
        start: async () => {
            events.push(`start on ${state.settings}`);
            await yieldNow();
            await yieldNow();
            state.running = true;
            events.push("started");
        },
        log: (line) => events.push(`log ${line}`),
        refresh: (effects, assets) =>
            events.push(`refresh effects=${effects} assets=${assets}`),
    };

    return { host, events, state };
}

/** The stops and starts alone, in order. */
function lifecycle(events: readonly string[]): string[] {
    return events.filter(
        (e) =>
            e === "stop" ||
            e === "stopped" ||
            e.startsWith("start on") ||
            e === "started",
    );
}

const FEATURES_LINE =
    "log Experimental features or executables changed - restarting LSP server.";
const FOLDERS_LINE =
    "log Shader or game folders changed - restarting LSP server.";
const STARTUP_LINE =
    "log Server startup settings changed - restarting LSP server.";

describe("RestartCoordinator", () => {
    describe("a valid change", () => {
        it("stops and starts the servers, then sends the previews to read again", async () => {
            const { host, events } = harness();

            await new RestartCoordinator(host).request({
                causes: ["folders"],
                effects: true,
                assets: true,
            });

            assert.deepEqual(events, [
                "valid",
                FOLDERS_LINE,
                "stop",
                "stopped",
                "start on first",
                "started",
                "refresh effects=true assets=true",
            ]);
        });

        it("starts the servers when none is running, without a stop", async () => {
            const { host, events, state } = harness();
            state.running = false;

            await new RestartCoordinator(host).request({ causes: ["start"] });

            assert.deepEqual(events, [
                "valid",
                "log Starting LSP server after configuration change.",
                "start on first",
                "started",
                "refresh effects=false assets=false",
            ]);
        });
    });

    // #395: a baseline or schema type set to local before its path stopped every server and left
    // them stopped until the path was set.
    describe("an invalid change", () => {
        it("keeps the running servers and reports the configuration", async () => {
            const { host, events, state } = harness();
            state.valid = false;

            await new RestartCoordinator(host).request({
                causes: ["startup"],
            });

            assert.deepEqual(events, ["invalid, reported"]);
            assert.equal(state.running, true);
        });

        it("restarts on the change that makes the configuration valid, with what was held", async () => {
            const { host, events, state } = harness();
            const coordinator = new RestartCoordinator(host);
            state.valid = false;
            await coordinator.request({ causes: ["folders"], assets: true });

            state.valid = true;
            state.settings = "completed";
            await coordinator.request({ causes: ["startup"] });

            assert.deepEqual(events, [
                "invalid, reported",
                "valid",
                FOLDERS_LINE,
                STARTUP_LINE,
                "stop",
                "stopped",
                "start on completed",
                "started",
                "refresh effects=false assets=true",
            ]);
        });
    });

    describe("changes back to back", () => {
        it("runs one stop and start at a time, and coalesces what arrives meanwhile into one more", async () => {
            const { host, events, state } = harness();
            const coordinator = new RestartCoordinator(host);

            const first = coordinator.request({ causes: ["features"] });
            // The first restart is under way - its stop is waiting on the process - when the next
            // two changes arrive.
            await yieldNow();
            assert.deepEqual(lifecycle(events), ["stop"]);
            state.settings = "second";
            const second = coordinator.request({
                causes: ["folders"],
                effects: true,
            });
            state.settings = "latest";
            const third = coordinator.request({
                causes: ["startup"],
                assets: true,
            });
            await Promise.all([first, second, third]);

            assert.deepEqual(lifecycle(events), [
                "stop",
                "stopped",
                "start on latest",
                "started",
                "stop",
                "stopped",
                "start on latest",
                "started",
            ]);

            // The second run carries the union of what arrived while the first ran.
            const firstRefresh = events.findIndex((e) =>
                e.startsWith("refresh"),
            );
            assert.equal(
                events[firstRefresh],
                "refresh effects=false assets=false",
            );
            const secondRun = events.slice(firstRefresh + 1);
            assert.deepEqual(secondRun, [
                "valid",
                FOLDERS_LINE,
                STARTUP_LINE,
                "stop",
                "stopped",
                "start on latest",
                "started",
                "refresh effects=true assets=true",
            ]);
            assert.equal(events.filter((e) => e === FEATURES_LINE).length, 1);
            assert.equal(state.running, true);
        });

        it("coalesces the Restart command into a configuration restart already running", async () => {
            const { host, events } = harness();
            const coordinator = new RestartCoordinator(host);

            const running = coordinator.request({ causes: ["features"] });
            await yieldNow();
            await Promise.all([
                running,
                coordinator.request({ causes: ["command"] }),
                coordinator.request({ causes: ["command"] }),
            ]);

            assert.equal(lifecycle(events).length, 8);
        });

        it("folds changes made in one turn into a single restart", async () => {
            const { host, events } = harness();
            const coordinator = new RestartCoordinator(host);

            await Promise.all([
                coordinator.request({ causes: ["features"] }),
                coordinator.request({ causes: ["startup"] }),
            ]);

            assert.equal(lifecycle(events).length, 4);
        });

        it("settles every caller only once its restart, or the one it joined, is done", async () => {
            const { host, events } = harness();
            const coordinator = new RestartCoordinator(host);

            const first = coordinator.request({ causes: ["features"] });
            const second = coordinator.request({ causes: ["startup"] });
            await second;

            assert.equal(events.at(-2), "started");
            await first;
        });
    });
});
