// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { modelRequestsOf, ModelStore } from "./modelStore";

const HULL = { partId: "hull", modelReference: "Hull.alo" };
const TURRET = { partId: "turret", modelReference: "Turret.alo" };
const glb = (...bytes: number[]): Uint8Array => new Uint8Array(bytes);

/** A store whose reader makes a list of a model's bytes, and every reading it did. */
function newStore(): {
    store: ModelStore<string, number[]>;
    read: number[][];
} {
    const read: number[][] = [];
    const store = new ModelStore<string, number[]>((bytes) => {
        const model = [...bytes];
        read.push(model);
        return model;
    });
    return { store, read };
}

/** A store that has asked for the hull and been sent it at version v1. */
function holdingTheHull(): ModelStore<string, number[]> {
    const store = newStore().store;
    store.asking(HULL);
    store.take({ partId: "hull", version: "v1", data: glb(1, 2, 3) });
    return store;
}

describe("ModelStore", () => {
    it("holds no version for a model it was never sent", () => {
        const store = newStore().store;

        assert.equal(store.asking(HULL), undefined);
    });

    it("reads the bytes a reply carries, keeps the model and says it is new", () => {
        const store = newStore().store;
        store.asking(HULL);

        const taken = store.take({
            partId: "hull",
            version: "v1",
            data: glb(1, 2, 3),
        });

        assert.deepEqual(taken?.model, [1, 2, 3]);
        assert.equal(taken?.fresh, true);
        assert.equal(taken?.modelReference, "Hull.alo");
        assert.equal(store.size, 1);
    });

    // A refresh asks with the version held; the server's "still yours" rebuilds the part from the
    // model kept, with nothing sent and nothing read.
    it("asks again with the version it holds, and answers 'unchanged' with the model kept", () => {
        const { store, read } = newStore();
        store.asking(HULL);
        const first = store.take({
            partId: "hull",
            version: "v1",
            data: glb(1, 2, 3),
        });

        const have = store.asking(HULL);
        const taken = store.take({
            partId: "hull",
            version: "v1",
            unchanged: true,
        });

        assert.equal(have, "v1");
        assert.equal(taken?.model, first?.model);
        assert.equal(taken?.fresh, false);
        assert.equal(read.length, 1);
    });

    // Every part of a scene asks before any reply is in, so each of them is sent the model.
    // Every part of a scene asks before any reply is in. Twenty turrets of one model were twenty
    // requests, and the model was sent twenty times.
    describe("several parts of one model in one scene", () => {
        const SECOND = { partId: "second", modelReference: "HULL.ALO" };

        it("sends the first part's request and keeps the others", () => {
            const { store } = newStore();
            store.asking(HULL);
            store.asking(SECOND);
            store.asking(TURRET);

            assert.equal(store.rides("hull"), false);
            assert.equal(store.rides("second"), true);
            assert.equal(store.rides("turret"), false);
        });

        it("answers every part with the one reading of the one reply", () => {
            const { store, read } = newStore();
            store.asking(HULL);
            store.asking(SECOND);

            const first = store.take({
                partId: "hull",
                version: "v1",
                data: glb(1, 2, 3),
            });
            const waiting = store.answeredWith("hull");
            const second = store.take({
                partId: "second",
                request: store.requestOf("second"),
            });

            assert.deepEqual(waiting, [SECOND]);
            assert.equal(read.length, 1);
            assert.deepEqual(first?.model, [1, 2, 3]);
            assert.equal(second?.model, first?.model);
            assert.equal(second?.modelReference, "HULL.ALO");
            assert.equal(second?.fresh, false);
            assert.equal(second?.replaced, false);
            assert.equal(store.size, 1);
            assert.deepEqual(store.answeredWith("hull"), []);
        });

        it("answers every part of a model that came without a version", () => {
            const { store, read } = newStore();
            store.asking(HULL);
            store.asking(SECOND);

            const first = store.take({ partId: "hull", data: glb(4) });
            store.answeredWith("hull");
            const second = store.take({ partId: "second" });

            assert.equal(read.length, 1);
            assert.equal(second?.model, first?.model);
        });

        it("answers every part with the model held when it still stands", () => {
            const store = holdingTheHull();
            store.retain([HULL]);
            store.asking(HULL);
            store.asking(SECOND);

            const first = store.take({ partId: "hull", unchanged: true });
            const waiting = store.answeredWith("hull");
            const second = store.take({ partId: "second" });

            assert.equal(store.rides("second"), false);
            assert.deepEqual(waiting, [SECOND]);
            assert.deepEqual(first?.model, [1, 2, 3]);
            assert.equal(second?.model, first?.model);
        });

        // A part that is not coming still has to be counted, or the cover waits on it.
        it("has nothing for any of them when the model did not load", () => {
            const { store } = newStore();
            store.asking(HULL);
            store.asking(SECOND);

            assert.equal(store.take({ partId: "hull" }), null);
            assert.deepEqual(store.answeredWith("hull"), [SECOND]);
            assert.equal(store.awaits({ partId: "second" }), true);
            assert.equal(store.take({ partId: "second" }), null);
        });

        // The reply to its first request is no longer the one it waits on, so that request cannot
        // stand for the second.
        it("sends a part's request again when it asks again before its reply is in", () => {
            const { store } = newStore();
            store.asking(HULL);
            const first = store.requestOf("hull");
            store.asking(SECOND);
            store.asking(HULL);

            assert.equal(store.rides("hull"), false);
            assert.equal(store.rides("second"), true);
            assert.equal(
                store.take({
                    partId: "hull",
                    request: first,
                    version: "v1",
                    data: glb(1),
                }),
                null,
            );
            assert.deepEqual(store.answeredWith("hull"), []);

            store.take({
                partId: "hull",
                request: store.requestOf("hull"),
                version: "v2",
                data: glb(2),
            });

            assert.deepEqual(store.answeredWith("hull"), [SECOND]);
            assert.deepEqual(store.take({ partId: "second" })?.model, [2]);
        });

        it("sends a request for the next part once the model's reply is in", () => {
            const { store } = newStore();
            store.asking(HULL);
            store.take({ partId: "hull", version: "v1", data: glb(1) });

            const have = store.asking(SECOND);

            assert.equal(store.rides("second"), false);
            assert.equal(have, "v1");
        });

        // The same file baked with other clips is another GLB.
        it("sends a request of its own for the model baked with other clips", () => {
            const { store } = newStore();
            store.asking(HULL);
            store.asking({
                partId: "wreck",
                modelReference: "Hull.alo",
                animations: ["Hull_die_00.ala"],
            });

            assert.equal(store.rides("wreck"), false);
        });

        // A reply still on its way belongs to the scene that asked.
        it("asks for itself in the next scene", () => {
            const { store } = newStore();
            store.asking(HULL);
            store.asking(SECOND);

            store.retain([HULL]);
            store.asking(SECOND);
            store.asking(HULL);

            assert.equal(store.rides("second"), false);
            assert.equal(store.rides("hull"), true);
        });
    });

    it("reads a changed model's new bytes and keeps that model in place of the old", () => {
        const { store, read } = newStore();
        store.asking(HULL);
        store.take({ partId: "hull", version: "v1", data: glb(1, 2, 3) });
        store.asking(HULL);

        const taken = store.take({
            partId: "hull",
            version: "v2",
            data: glb(9),
        });

        assert.deepEqual(taken?.model, [9]);
        assert.equal(read.length, 2);
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
        const store = newStore().store;
        store.asking(HULL);

        assert.equal(store.take({ partId: "hull" }), null);
        assert.equal(store.size, 0);
    });

    it("has nothing for a reply nobody asked for", () => {
        const store = holdingTheHull();

        assert.equal(store.take({ partId: "hull", unchanged: true }), null);
    });

    describe("two refreshes close together", () => {
        /** A store holding v1 that has asked for the hull twice, a scene apart. */
        function askedTwice(): {
            store: ModelStore<string, number[]>;
            first: number | undefined;
            second: number | undefined;
        } {
            const store = holdingTheHull();
            store.asking(HULL);
            const first = store.requestOf("hull");
            store.retain([HULL]);
            store.asking(HULL);
            const second = store.requestOf("hull");
            return { store, first, second };
        }

        it("numbers each request", () => {
            const { first, second } = askedTwice();

            assert.notEqual(first, undefined);
            assert.notEqual(second, undefined);
            assert.notEqual(first, second);
        });

        // The first reply was read off the file while it was still being written. Taken for the
        // answer to the second request, it left the second reply with nothing to answer.
        it("does not take the first reply for the answer to the second request", () => {
            const { store, first, second } = askedTwice();
            const older = {
                partId: "hull",
                request: first,
                version: "v2",
                data: glb(2),
            };
            const newer = {
                partId: "hull",
                request: second,
                version: "v3",
                data: glb(3),
            };

            assert.equal(store.awaits(older), false);
            assert.equal(store.take(older), null);
            assert.equal(store.awaits(newer), true);
            const taken = store.take(newer);

            assert.deepEqual(taken?.model, [3]);
            assert.equal(store.asking(HULL), "v3");
        });

        it("keeps the newer bytes when the older reply arrives last", () => {
            const { store, first, second } = askedTwice();

            store.take({
                partId: "hull",
                request: second,
                version: "v3",
                data: glb(3),
            });
            const late = {
                partId: "hull",
                request: first,
                version: "v2",
                data: glb(2),
            };

            assert.equal(store.awaits(late), false);
            assert.equal(store.take(late), null);
            assert.equal(store.asking(HULL), "v3");
        });
    });

    describe("the models a scene asks for", () => {
        const WRECK = {
            partId: "breakoff:left",
            modelReference: "Wreck.alo",
            animations: ["Wreck_spin_00.ala"],
        };
        const CLONE = {
            partId: "clone:Hull_Death_Clone",
            modelReference: "Hull_d.alo",
            animations: ["Hull_d_die_00.ala"],
        };
        const scene = {
            parts: [
                { resolved: true, modelRef: "Hull.alo" },
                { resolved: false, modelRef: "Gone.alo" },
            ],
            breakoffProps: [
                { modelRef: "Wreck.alo", animations: ["Wreck_spin_00.ala"] },
                { modelRef: null, animations: [] },
            ],
            deathClones: [
                { modelFile: "Hull_d.alo", animations: ["Hull_d_die_00.ala"] },
            ],
        };

        it("are its resolved parts, and its wrecks and death clones with their own clips", () => {
            assert.deepEqual(modelRequestsOf(scene), [
                { modelReference: "Hull.alo" },
                {
                    modelReference: "Wreck.alo",
                    animations: ["Wreck_spin_00.ala"],
                },
                {
                    modelReference: "Hull_d.alo",
                    animations: ["Hull_d_die_00.ala"],
                },
            ]);
        });

        // A wreck and a death clone play clips of their own, so they are held under another key
        // than a part of the same file. Retained by the parts' keys alone they were let go on
        // every scene and sent again.
        it("keeps a wreck and a death clone across a scene that still names them", () => {
            const store = holdingTheHull();
            store.asking(WRECK);
            store.take({ partId: WRECK.partId, version: "w1", data: glb(7) });
            store.asking(CLONE);
            store.take({ partId: CLONE.partId, version: "c1", data: glb(8) });

            store.retain(modelRequestsOf(scene));

            assert.equal(store.size, 3);
            assert.equal(store.asking(WRECK), "w1");
            assert.equal(store.asking(CLONE), "c1");
        });
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
            const store = newStore().store;
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
