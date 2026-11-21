// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    EMPTY_SUBJECT_STATE,
    lodOnArrival,
    lodToRecord,
    openingLod,
    subjectStateFrom,
} from "./subjectState";

describe("subjectStateFrom", () => {
    it("gives an empty state for a subject never opened this session", () => {
        assert.deepEqual(subjectStateFrom(undefined), EMPTY_SUBJECT_STATE);
        assert.deepEqual(subjectStateFrom(null), EMPTY_SUBJECT_STATE);
    });

    it("keeps what the reader had set", () => {
        const stored = subjectStateFrom({
            alt: 2,
            lod: 3,
            destroyed: ["HP_F-L"],
            hiddenEmitters: [0, 2],
            animation: "fly_00",
        });

        assert.equal(stored.alt, 2);
        assert.equal(stored.lod, 3);
        assert.deepEqual(stored.destroyed, ["HP_F-L"]);
        assert.deepEqual(stored.hiddenEmitters, [0, 2]);
        assert.equal(stored.animation, "fly_00");
    });

    /** Same reasoning as the viewer settings: one bad value costs its own control, not the panel. */
    it("refuses values of the wrong shape rather than passing them on", () => {
        const nonsense = subjectStateFrom({
            alt: "two",
            lod: null,
            destroyed: "HP_F-L",
            hiddenEmitters: [0, "x"],
            animation: 7,
        });

        assert.deepEqual(nonsense, EMPTY_SUBJECT_STATE);
    });

    it("survives a stored value that is not an object", () => {
        for (const junk of ["", 7, [], true]) {
            assert.deepEqual(subjectStateFrom(junk), EMPTY_SUBJECT_STATE);
        }
    });

    /**
     * Every model tags LOD 0, so a recorded 0 that nobody chose is indistinguishable from a real
     * choice once it is read back - and in Alamo it is the LOWEST detail.
     */
    it("holds no detail level for a subject that has not chosen one", () => {
        assert.equal(EMPTY_SUBJECT_STATE.lod, null);
        assert.equal(subjectStateFrom({ alt: 1 }).lod, null);
        assert.equal(subjectStateFrom({ lod: null }).lod, null);
        assert.equal(subjectStateFrom({ lod: 0 }).lod, 0);
    });

    /**
     * Meshes are on until switched off, so recording the hidden ones round-trips them. Particle
     * systems are the other way up: a model opens quiet, so almost every system starts off and it is
     * switching one ON that the reader will expect to find again. Recording only `hidden` lost every
     * effect anyone had deliberately lit.
     */
    it("keeps the rows switched on by hand as well as the ones switched off", () => {
        const stored = subjectStateFrom({
            hidden: ["mesh:hull:0:0"],
            shown: ["particle:hull#3"],
        });

        assert.deepEqual(stored.hidden, ["mesh:hull:0:0"]);
        assert.deepEqual(stored.shown, ["particle:hull#3"]);
    });

    /**
     * A level the model does not have would show nothing at all. The state is per subject, but the
     * subject can be EDITED between one open and the next - a mesh's `_ALT2` suffix removed - and
     * restoring a level that no longer exists is indistinguishable from a broken model.
     */
    it("drops a level the model no longer defines", () => {
        const stored = subjectStateFrom({ alt: 2, lod: 3 });

        assert.deepEqual(
            levelsWithin(stored, { alt: [0, 1], lod: [0, 1, 2, 3] }),
            { alt: null, lod: 3 },
        );
        assert.equal(openingLod([0, 1], stored.lod), 1);
    });

    it("keeps a level the model still defines", () => {
        const stored = subjectStateFrom({ alt: 1, lod: 0 });

        assert.deepEqual(levelsWithin(stored, { alt: [0, 1], lod: [0, 1] }), {
            alt: 1,
            lod: 0,
        });
    });
});

describe("openingLod", () => {
    /** Alamo numbers detail the other way up: LOD0 is the distant mesh, the last level the close-up. */
    it("opens a subject with nothing recorded at its highest detail level", () => {
        assert.equal(openingLod([0, 1, 2], null), 2);
        assert.equal(openingLod([0], null), 0);
    });

    it("restores a recorded level the geometry still defines", () => {
        assert.equal(openingLod([0, 1, 2], 1), 1);
        assert.equal(openingLod([0, 1, 2], 0), 0);
    });

    it("opens at the highest level when the recorded one is gone", () => {
        assert.equal(openingLod([0, 1], 3), 1);
    });
});

describe("lodOnArrival", () => {
    /**
     * A unit's parts arrive one by one. A hardpoint's model that tags no level, arriving before the
     * hull, defines only LOD 0; choosing once from that left the unit at its lowest detail.
     */
    it("follows the levels as the geometry arrives until the reader picks one", () => {
        assert.equal(lodOnArrival([0], 0, false, null), 0);
        assert.equal(lodOnArrival([0, 1, 2], 0, false, null), 2);
    });

    it("keeps the level the reader picked", () => {
        assert.equal(lodOnArrival([0, 1, 2], 0, true, null), 0);
        assert.equal(lodOnArrival([0, 1, 2, 3], 1, true, 2), 1);
    });

    it("restores a recorded pick once the geometry defines its level", () => {
        assert.equal(lodOnArrival([0], 0, false, 1), 0);
        assert.equal(lodOnArrival([0, 1, 2], 0, false, 1), 1);
    });
});

describe("lodToRecord", () => {
    /**
     * The 0 a subject shows while its geometry is on its way is a placeholder. Recorded, a preview
     * closed before the geometry arrived came back at LOD0 on the next open.
     */
    it("records nothing before the reader picks a level", () => {
        assert.equal(lodToRecord(0, false, null), null);
    });

    /**
     * The opening rule's choice is not recorded: it is made again on every open from the levels that
     * have arrived, and one made from part of the geometry would be restored as if it were a pick.
     */
    it("records nothing for the level the opening rule chose", () => {
        assert.equal(lodToRecord(1, false, null), null);
    });

    it("keeps the level recorded before until the reader picks again", () => {
        assert.equal(lodToRecord(0, false, 2), 2);
        assert.equal(lodToRecord(0, false, 0), 0);
    });

    it("records the level on screen once the reader picked it", () => {
        assert.equal(lodToRecord(2, true, null), 2);
        assert.equal(lodToRecord(0, true, 2), 0);
    });
});

/** Reads the two levels back, dropping either that the model no longer offers. */
function levelsWithin(
    state: { alt: number; lod: number | null },
    defined: { alt: number[]; lod: number[] },
): { alt: number | null; lod: number | null } {
    return {
        alt: defined.alt.includes(state.alt) ? state.alt : null,
        lod:
            state.lod !== null && defined.lod.includes(state.lod)
                ? state.lod
                : null,
    };
}
