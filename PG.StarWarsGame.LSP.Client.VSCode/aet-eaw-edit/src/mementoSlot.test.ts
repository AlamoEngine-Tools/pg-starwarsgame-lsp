// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { mementoSlot, type MementoScope } from "./mementoSlot";

/** A memento over a plain map. */
function scope(initial: Record<string, unknown> = {}) {
    const values = new Map<string, unknown>(Object.entries(initial));
    const memento: MementoScope = {
        get: <T>(key: string) => values.get(key) as T | undefined,
        update: (key, value) => {
            values.set(key, value);
            return Promise.resolve();
        },
    };
    return { values, memento };
}

/** A count between 0 and 10, 3 when nothing usable is stored. */
function count(stored: unknown): number {
    return typeof stored === "number" && Number.isFinite(stored)
        ? Math.min(10, Math.max(0, stored))
        : 3;
}

describe("mementoSlot", () => {
    it("reads what was stored under its key", () => {
        const { memento } = scope({ "a.count": 7, "b.count": 1 });

        assert.equal(mementoSlot("a.count", count, memento).read(), 7);
    });

    it("reads the normaliser's default when nothing is stored", () => {
        const { memento } = scope();

        assert.equal(mementoSlot("a.count", count, memento).read(), 3);
    });

    it("never hands back a stored value the normaliser refuses", () => {
        const { memento } = scope({ "a.count": "seven" });

        assert.equal(mementoSlot("a.count", count, memento).read(), 3);
    });

    it("normalises a value before it is written", () => {
        const { memento, values } = scope();

        mementoSlot("a.count", count, memento).save(400);

        assert.equal(values.get("a.count"), 10);
    });

    it("reads back what it saved", () => {
        const { memento } = scope();
        const slot = mementoSlot("a.count", count, memento);

        slot.save(5);

        assert.equal(slot.read(), 5);
    });

    it("keeps two slots of one scope apart", () => {
        const { memento } = scope();
        const a = mementoSlot("a.count", count, memento);
        const b = mementoSlot("b.count", count, memento);

        a.save(1);
        b.save(2);

        assert.equal(a.read(), 1);
        assert.equal(b.read(), 2);
    });

    it("swallows a failed write", async () => {
        const failing: MementoScope = {
            get: () => undefined,
            update: () => Promise.reject(new Error("storage is full")),
        };
        let unhandled = false;
        const note = () => {
            unhandled = true;
        };
        process.once("unhandledRejection", note);

        mementoSlot("a.count", count, failing).save(5);
        await new Promise((resolve) => setImmediate(resolve));
        process.removeListener("unhandledRejection", note);

        assert.equal(unhandled, false);
    });
});
