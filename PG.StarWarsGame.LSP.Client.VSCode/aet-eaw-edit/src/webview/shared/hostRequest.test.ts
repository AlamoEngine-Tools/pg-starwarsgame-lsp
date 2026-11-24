// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { createRequester } from "./hostRequest";
import type { Transport } from "./vscodeApi";

/** A transport that records what the page posts and lets the test answer as the host. */
function fakeTransport(): Transport & {
    posted: unknown[];
    answer(message: unknown): void;
} {
    const listeners = new Set<(message: unknown) => void>();
    const posted: unknown[] = [];
    return {
        posted,
        post: (message) => posted.push(message),
        listen(handler) {
            listeners.add(handler);
            return () => listeners.delete(handler);
        },
        answer: (message) => listeners.forEach((l) => l(message)),
    };
}

/** Timers the test fires by hand. */
function fakeTimers(): {
    setTimer(fn: () => void, ms: number): number;
    clearTimer(handle: number): void;
    fire(): void;
    pending(): number;
} {
    const timers = new Map<number, () => void>();
    let next = 1;
    return {
        setTimer(fn) {
            timers.set(next, fn);
            return next++;
        },
        clearTimer: (handle) => void timers.delete(handle),
        fire() {
            const due = [...timers.values()];
            timers.clear();
            due.forEach((fn) => fn());
        },
        pending: () => timers.size,
    };
}

describe("createRequester", () => {
    it("posts the method and params under a request id", () => {
        const transport = fakeTransport();
        const request = createRequester(transport);
        void request("aet/getSymbolPreview", { page: "units", id: "AT_AT" });
        assert.deepEqual(transport.posted, [
            {
                type: "request",
                id: 1,
                method: "aet/getSymbolPreview",
                params: { page: "units", id: "AT_AT" },
            },
        ]);
    });

    it("resolves with the result of the response carrying its id", async () => {
        const transport = fakeTransport();
        const request = createRequester(transport);
        const first = request("aet/getLanguages");
        const second = request("aet/listModels");
        transport.answer({ type: "response", id: 2, result: 20 });
        transport.answer({ type: "response", id: 1, result: 10 });
        assert.deepEqual(await Promise.all([first, second]), [10, 20]);
    });

    it("ignores what is not a response to one of its requests", async () => {
        const transport = fakeTransport();
        const timers = fakeTimers();
        const request = createRequester(transport, timers);
        const pending = request("aet/getLanguages");
        transport.answer({ type: "outline", id: 1, result: "no" });
        transport.answer({ type: "response", id: 99, result: "no" });
        transport.answer({ type: "response", id: "1", result: "no" });
        transport.answer({ type: "response", id: 1, result: "yes" });
        assert.equal(await pending, "yes");
    });

    it("resolves null when the host answers nothing", async () => {
        const transport = fakeTransport();
        const request = createRequester(transport);
        const pending = request("aet/getLanguages");
        transport.answer({ type: "response", id: 1 });
        assert.equal(await pending, null);
    });

    it("resolves null at the timeout, and a late answer changes nothing", async () => {
        const transport = fakeTransport();
        const timers = fakeTimers();
        const request = createRequester(transport, timers);
        const pending = request("aet/getStorySchema");
        timers.fire();
        transport.answer({ type: "response", id: 1, result: 5 });
        assert.equal(await pending, null);
    });

    it("clears the timer of a request that was answered", async () => {
        const transport = fakeTransport();
        const timers = fakeTimers();
        const request = createRequester(transport, timers);
        const pending = request("aet/getLanguages");
        assert.equal(timers.pending(), 1);
        transport.answer({ type: "response", id: 1, result: 1 });
        await pending;
        assert.equal(timers.pending(), 0);
    });

    it("gives each requester its own id sequence", () => {
        const transport = fakeTransport();
        void createRequester(transport)("aet/getLanguages");
        void createRequester(transport)("aet/listModels");
        assert.deepEqual(
            transport.posted.map((m) => (m as { id: number }).id),
            [1, 1],
        );
    });
});
