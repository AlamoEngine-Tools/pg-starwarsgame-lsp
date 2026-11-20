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

    // Project discovery - a workspace folder added or removed, a .pgproj appearing or going -
    // brings the client set in line with the workspace. It shares the clients with a restart, so it
    // runs in the same queue: never beside a restart, never beside another discovery.
    describe("project discovery", () => {
        /**
         * A window whose clients follow the projects of its workspace. The stop and the discovery
         * each take a few turns, as the real ones do while processes start and stop, and every
         * client stopped or started is recorded. `overlaps` counts any step that began while another
         * was still running.
         */
        function windowHarness(projects: string[]): {
            host: RestartHost;
            events: string[];
            state: {
                workspace: Set<string>;
                clients: Set<string>;
                valid: boolean;
                settings: string;
                overlaps: number;
            };
        } {
            const events: string[] = [];
            const state = {
                workspace: new Set(projects),
                clients: new Set(projects),
                valid: true,
                settings: "first",
                overlaps: 0,
            };
            let busy = 0;

            const step = async (work: () => Promise<void>): Promise<void> => {
                if (busy > 0) {
                    state.overlaps++;
                }
                busy++;
                try {
                    await work();
                } finally {
                    busy--;
                }
            };

            const host: RestartHost = {
                running: () => state.clients.size > 0,
                validate: () => {
                    events.push(state.valid ? "valid" : "invalid, reported");
                    return state.valid;
                },
                stop: () =>
                    step(async () => {
                        for (const client of [...state.clients].sort()) {
                            events.push(`stop ${client}`);
                        }
                        await yieldNow();
                        state.clients.clear();
                    }),
                // Reads the workspace when it begins, as discovery reads the folders and finds the
                // project files before it changes anything.
                start: () =>
                    step(async () => {
                        const wanted = new Set(state.workspace);
                        await yieldNow();
                        await yieldNow();
                        for (const client of [...state.clients].sort()) {
                            if (!wanted.has(client)) {
                                events.push(`stop ${client}`);
                                state.clients.delete(client);
                            }
                        }
                        for (const client of [...wanted].sort()) {
                            if (!state.clients.has(client)) {
                                events.push(
                                    `start ${client} on ${state.settings}`,
                                );
                                state.clients.add(client);
                            }
                        }
                    }),
                log: (line) => events.push(`log ${line}`),
                refresh: () => undefined,
            };

            return { host, events, state };
        }

        it("starts a new project and stops a gone one, leaving the rest and validating nothing", async () => {
            const { host, events, state } = windowHarness(["alpha", "beta"]);
            state.workspace = new Set(["alpha", "gamma"]);

            await new RestartCoordinator(host).request({
                causes: ["discovery"],
            });

            assert.deepEqual(events, ["stop beta", "start gamma on first"]);
            assert.deepEqual([...state.clients].sort(), ["alpha", "gamma"]);
        });

        it("(a) runs a discovery that arrives mid-restart after the restart, never beside it", async () => {
            const { host, events, state } = windowHarness(["alpha"]);
            const coordinator = new RestartCoordinator(host);

            const restart = coordinator.request({ causes: ["startup"] });
            await yieldNow();
            // The restart's stop is under way when a project folder is added.
            assert.deepEqual(events.at(-1), "stop alpha");
            state.workspace = new Set(["alpha", "beta"]);
            const discovery = coordinator.request({ causes: ["discovery"] });
            await Promise.all([restart, discovery]);

            assert.equal(state.overlaps, 0);
            assert.deepEqual([...state.clients].sort(), ["alpha", "beta"]);
            // Every client started once per run that wanted it, none twice at once.
            assert.equal(
                events.filter((e) => e.startsWith("start alpha")).length,
                1,
            );
            assert.equal(
                events.filter((e) => e.startsWith("start beta")).length,
                1,
            );
        });

        it("(b) holds a settings change that arrives mid-discovery until the discovery is done", async () => {
            const { host, events, state } = windowHarness(["alpha"]);
            const coordinator = new RestartCoordinator(host);
            state.workspace = new Set(["alpha", "beta"]);

            const discovery = coordinator.request({ causes: ["discovery"] });
            await yieldNow();
            state.settings = "latest";
            const restart = coordinator.request({ causes: ["features"] });
            await Promise.all([discovery, restart]);

            assert.equal(state.overlaps, 0);
            assert.deepEqual(events, [
                // The discovery finished on its own, before the restart began.
                "start beta on latest",
                "valid",
                FEATURES_LINE,
                "stop alpha",
                "stop beta",
                "start alpha on latest",
                "start beta on latest",
            ]);
            assert.deepEqual([...state.clients].sort(), ["alpha", "beta"]);
        });

        it("(c) runs two discoveries back to back one after the other, ending on the last workspace", async () => {
            const { host, events, state } = windowHarness(["alpha"]);
            const coordinator = new RestartCoordinator(host);

            state.workspace = new Set(["alpha", "beta"]);
            const first = coordinator.request({ causes: ["discovery"] });
            await yieldNow();
            await yieldNow();
            // The first is under way and has read the workspace when the second change lands.
            state.workspace = new Set(["beta", "gamma"]);
            const second = coordinator.request({ causes: ["discovery"] });
            await Promise.all([first, second]);

            assert.equal(state.overlaps, 0);
            assert.deepEqual(events, [
                "start beta on first",
                "stop alpha",
                "start gamma on first",
            ]);
            assert.deepEqual([...state.clients].sort(), ["beta", "gamma"]);
        });

        it("folds a discovery into a restart that has not begun, whose start discovers anyway", async () => {
            const { host, events, state } = windowHarness(["alpha"]);
            const coordinator = new RestartCoordinator(host);
            state.workspace = new Set(["alpha", "beta"]);

            await Promise.all([
                coordinator.request({ causes: ["startup"] }),
                coordinator.request({ causes: ["discovery"] }),
            ]);

            assert.deepEqual(events, [
                "valid",
                STARTUP_LINE,
                "stop alpha",
                "start alpha on first",
                "start beta on first",
            ]);
        });

        it("still discovers on an invalid configuration, without reporting it again", async () => {
            const { host, events, state } = windowHarness(["alpha"]);
            const coordinator = new RestartCoordinator(host);
            state.valid = false;
            await coordinator.request({ causes: ["startup"] });

            // A project file saved while a setting is incomplete: its server starts, the others
            // keep running, and the incomplete setting is not reported once per save.
            state.workspace = new Set(["alpha", "beta"]);
            await coordinator.request({ causes: ["discovery"] });

            assert.deepEqual(events, [
                "invalid, reported",
                "start beta on first",
            ]);
            assert.deepEqual([...state.clients].sort(), ["alpha", "beta"]);
        });
    });
});
