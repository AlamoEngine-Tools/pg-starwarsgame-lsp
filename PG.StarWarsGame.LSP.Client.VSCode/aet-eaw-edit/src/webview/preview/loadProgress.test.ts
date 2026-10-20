// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    AssetLedger,
    isSubjectSystem,
    loadState,
    type LoadTally,
} from "./loadProgress";

const tally = (over: Partial<LoadTally> = {}): LoadTally => ({
    expectedParts: 4,
    arrivedParts: 4,
    requestedAssets: 10,
    settledAssets: 10,
    timedOut: false,
    ...over,
});

describe("AssetLedger", () => {
    it("counts a name the first time it is asked for", () => {
        const ledger = new AssetLedger();

        assert.equal(ledger.request("hull.tga"), true);
        assert.equal(ledger.requested, 1);
    });

    // The host answers each name ONCE and drops repeats without a reply, so a second request that
    // counted would be a reply this side waits for forever - and the cover would never lift.
    it("does not count the same name twice", () => {
        const ledger = new AssetLedger();

        ledger.request("hull.tga");

        assert.equal(ledger.request("hull.tga"), false);
        assert.equal(ledger.requested, 1);
    });

    // Keyed exactly as the host keys it. Two parts naming the same texture in different casings is
    // one request there, so it has to be one request here.
    it("treats a different casing as the same name", () => {
        const ledger = new AssetLedger();

        ledger.request("Hull.TGA");

        assert.equal(ledger.request("hull.tga"), false);
        assert.equal(ledger.requested, 1);
    });

    // The host returns early on an empty name without replying, so counting one would hang the wait.
    it("ignores an empty name, which is never answered", () => {
        const ledger = new AssetLedger();

        assert.equal(ledger.request(""), false);
        assert.equal(ledger.requested, 0);
    });

    it("settles a name it was waiting for", () => {
        const ledger = new AssetLedger();

        ledger.request("hull.tga");
        ledger.settle("hull.tga");

        assert.equal(ledger.settled, 1);
    });

    // The reply carries whatever casing the FIRST caller used, which need not be the casing this
    // side happens to compare against.
    it("settles regardless of the casing the reply came back in", () => {
        const ledger = new AssetLedger();

        ledger.request("hull.tga");
        ledger.settle("Hull.TGA");

        assert.equal(ledger.settled, 1);
    });

    // A texture that did not resolve is still an ANSWER: the host replies with no data, and the
    // wait is over either way. Nothing here distinguishes them, and that is the point.
    it("counts an answer once, however many times it arrives", () => {
        const ledger = new AssetLedger();

        ledger.request("hull.tga");
        ledger.settle("hull.tga");
        ledger.settle("hull.tga");

        assert.equal(ledger.settled, 1);
    });

    it("does not count an answer to something never asked for", () => {
        const ledger = new AssetLedger();

        ledger.settle("stowaway.tga");

        assert.equal(ledger.settled, 0);
        assert.equal(ledger.requested, 0);
    });

    it("forgets everything when a new scene starts", () => {
        const ledger = new AssetLedger();

        ledger.request("hull.tga");
        ledger.settle("hull.tga");
        ledger.clear();

        assert.equal(ledger.requested, 0);
        assert.equal(ledger.settled, 0);
    });
});

describe("loadState", () => {
    it("waits on the scene before anything has been said", () => {
        const state = loadState(
            tally({ expectedParts: null, arrivedParts: 0 }),
        );

        assert.equal(state.stage, "scene");
        assert.equal(state.covered, true);
        assert.equal(state.detail, null);
    });

    // The one that matters most: a scene where nothing resolved sends no GLB request, so no `glb`
    // ever arrives. Waiting for parts here would cover the viewport for the rest of the session.
    it("is ready at once when the scene has no parts to load", () => {
        const state = loadState(
            tally({
                expectedParts: 0,
                arrivedParts: 0,
                requestedAssets: 0,
                settledAssets: 0,
            }),
        );

        assert.equal(state.stage, "ready");
        assert.equal(state.covered, false);
    });

    it("counts the geometry in", () => {
        const state = loadState(tally({ expectedParts: 5, arrivedParts: 2 }));

        assert.equal(state.stage, "geometry");
        assert.equal(state.covered, true);
        assert.equal(state.detail, "2 of 5");
    });

    // Textures are only asked for once the geometry that samples them exists, so this stage cannot
    // start until the last part is in - which is exactly why the model was appearing undressed.
    it("counts the materials in once every part has arrived", () => {
        const state = loadState(
            tally({
                expectedParts: 4,
                arrivedParts: 4,
                requestedAssets: 30,
                settledAssets: 12,
            }),
        );

        assert.equal(state.stage, "materials");
        assert.equal(state.covered, true);
        assert.equal(state.detail, "12 of 30");
    });

    it("uncovers the viewport once everything has been answered", () => {
        const state = loadState(tally());

        assert.equal(state.stage, "ready");
        assert.equal(state.covered, false);
    });

    it("is ready when a model samples no textures at all", () => {
        const state = loadState(
            tally({ requestedAssets: 0, settledAssets: 0 }),
        );

        assert.equal(state.stage, "ready");
        assert.equal(state.covered, false);
    });

    // The safety net. An asset the server never answers would otherwise hold the cover down over a
    // model that is perfectly visible underneath it - a worse failure than the pop-in this fixes.
    it("gives up and shows the model when the wait is abandoned", () => {
        const state = loadState(
            tally({
                expectedParts: 9,
                arrivedParts: 1,
                settledAssets: 0,
                timedOut: true,
            }),
        );

        assert.equal(state.stage, "ready");
        assert.equal(state.covered, false);
    });

    it("names what it is waiting for, in words", () => {
        assert.match(loadState(tally({ expectedParts: null })).label, /scene/i);
        assert.match(
            loadState(tally({ expectedParts: 5, arrivedParts: 1 })).label,
            /geometry/i,
        );
        assert.match(
            loadState(tally({ settledAssets: 3 })).label,
            /texture|material/i,
        );
    });
});

/**
 * A particle file opened on its own is a scene with ONE resolved part - the server builds it the
 * same shape as a model so the client has one thing to render - but that part is asked for as a
 * particle system, never as a GLB. The cover counts geometry by GLB replies, so it waited on a
 * reply that was never coming and sat over the field until the idle deadline gave up.
 */
describe("isSubjectSystem", () => {
    it("counts a particle scene's own system as its one part", () => {
        assert.equal(
            isSubjectSystem(
                { kind: "Particle", subject: "P_asteroid_field00" },
                "P_asteroid_field00",
            ),
            true,
        );
    });

    it("matches the subject whatever casing the reply came back in", () => {
        assert.equal(
            isSubjectSystem(
                { kind: "Particle", subject: "P_asteroid_field00" },
                "p_asteroid_field00",
            ),
            true,
        );
    });

    it("does not count a system a model mounts, which is not geometry", () => {
        assert.equal(
            isSubjectSystem(
                { kind: "Model", subject: "ev_star_destroyer" },
                "ev_star_destroyer",
            ),
            false,
        );
    });

    it("does not count an effect asked for by name on a particle scene", () => {
        assert.equal(
            isSubjectSystem(
                { kind: "Particle", subject: "P_asteroid_field00" },
                "Large_Explosion_Space",
            ),
            false,
        );
    });
});
