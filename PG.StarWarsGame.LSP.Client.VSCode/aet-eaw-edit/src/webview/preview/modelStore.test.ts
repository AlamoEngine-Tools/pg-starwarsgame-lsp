// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { ModelStore } from "./modelStore";

const HULL = { partId: "hull", modelReference: "Hull.alo" };
const TURRET = { partId: "turret", modelReference: "Turret.alo" };
const glb = (...bytes: number[]): Uint8Array => new Uint8Array(bytes);

/** A store that has asked for the hull and been sent it at version v1. */
function holdingTheHull(): ModelStore<string> {
    const store = new ModelStore<string>();
    store.asking(HULL);
    store.take({ partId: "hull", version: "v1", data: glb(1, 2, 3) });
    return store;
}

describe("ModelStore", () => {
    it("holds no version for a model it was never sent", () => {
        const store = new ModelStore();

        assert.equal(store.asking(HULL), undefined);
    });

    it("keeps the bytes a reply carries and says they are new", () => {
        const store = new ModelStore();
        store.asking(HULL);

        const taken = store.take({
            partId: "hull",
            version: "v1",
            data: glb(1, 2, 3),
        });

        assert.deepEqual([...(taken?.bytes ?? [])], [1, 2, 3]);
        assert.equal(taken?.fresh, true);
        assert.equal(taken?.modelReference, "Hull.alo");
        assert.equal(store.size, 1);
    });

    // A refresh asks with the version held; the server's "still yours" rebuilds the part from the
    // bytes kept, with nothing sent.
    it("asks again with the version it holds, and answers 'unchanged' from the bytes kept", () => {
        const store = holdingTheHull();

        const have = store.asking(HULL);
        const taken = store.take({
            partId: "hull",
            version: "v1",
            unchanged: true,
        });

        assert.equal(have, "v1");
        assert.deepEqual([...(taken?.bytes ?? [])], [1, 2, 3]);
        assert.equal(taken?.fresh, false);
    });

    it("takes a changed model's new bytes in place of the old", () => {
        const store = holdingTheHull();
        store.asking(HULL);

        const taken = store.take({
            partId: "hull",
            version: "v2",
            data: glb(9),
        });

        assert.deepEqual([...(taken?.bytes ?? [])], [9]);
        assert.equal(taken?.fresh, true);
        assert.equal(taken?.replaced, true);
        assert.equal(store.asking(HULL), "v2");
        assert.equal(store.size, 1);
    });

    it("holds one copy for two parts of the same model", () => {
        const store = holdingTheHull();

        const have = store.asking({
            partId: "second",
            modelReference: "HULL.ALO",
        });
        const taken = store.take({ partId: "second", unchanged: true });

        assert.equal(have, "v1");
        assert.equal(taken?.fresh, false);
        assert.equal(store.size, 1);
    });

    // The same file baked with other clips is another GLB.
    it("holds a model baked with other clips apart", () => {
        const store = holdingTheHull();

        const have = store.asking({
            partId: "wreck",
            modelReference: "Hull.alo",
            animations: ["Hull_die_00.ala"],
        });

        assert.equal(have, undefined);
    });

    it("has nothing for a model that did not load", () => {
        const store = new ModelStore();
        store.asking(HULL);

        assert.equal(store.take({ partId: "hull" }), null);
        assert.equal(store.size, 0);
    });

    it("has nothing for a reply nobody asked for", () => {
        const store = holdingTheHull();

        assert.equal(store.take({ partId: "hull", unchanged: true }), null);
    });

    // An XML edit can drop a hardpoint and the model it attached: that model is let go, the hull
    // stays.
    it("lets go of a model the new scene has no part for", () => {
        const store = holdingTheHull();
        store.asking(TURRET);
        store.take({ partId: "turret", version: "t1", data: glb(7) });

        store.retain([HULL]);

        assert.equal(store.size, 1);
        assert.equal(store.asking(HULL), "v1");
        assert.equal(store.asking(TURRET), undefined);
    });

    it("lets go of everything for a scene with no parts", () => {
        const store = holdingTheHull();

        store.retain([]);

        assert.equal(store.size, 0);
    });

    // A reply that was on its way when the scene changed belongs to the scene before.
    it("drops a reply that a new scene overtook", () => {
        const store = holdingTheHull();
        store.asking(HULL);

        store.retain([HULL]);

        assert.equal(store.take({ partId: "hull", unchanged: true }), null);
    });

    describe("a model's detail", () => {
        // The detail is asked for beside the model and is often answered first.
        it("survives the model's first arrival", () => {
            const store = new ModelStore<string>();
            store.asking(HULL);
            store.keepDetail("Hull.alo", "bones");

            const taken = store.take({
                partId: "hull",
                version: "v1",
                data: glb(1),
            });

            assert.equal(taken?.replaced, false);
            assert.equal(store.detailOf("Hull.alo"), "bones");
        });

        it("is kept across a refresh that leaves the model unchanged", () => {
            const store = holdingTheHull();
            store.keepDetail("Hull.alo", "bones");

            store.retain([HULL]);
            store.asking(HULL);
            store.take({ partId: "hull", version: "v1", unchanged: true });

            assert.equal(store.detailOf("hull.ALO"), "bones");
        });

        it("goes when the model's bytes change", () => {
            const store = holdingTheHull();
            store.keepDetail("Hull.alo", "bones");
            store.asking(HULL);

            store.take({ partId: "hull", version: "v2", data: glb(9) });

            assert.equal(store.detailOf("Hull.alo"), null);
        });

        it("goes when the scene stops using the model", () => {
            const store = holdingTheHull();
            store.keepDetail("Hull.alo", "bones");

            store.retain([TURRET]);

            assert.equal(store.detailOf("Hull.alo"), null);
        });
    });
});
