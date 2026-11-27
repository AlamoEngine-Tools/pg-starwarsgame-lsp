// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    PLATE_FIELD_MARGIN,
    PLATE_FIELD_WIDTH,
    placePlateField,
} from "./searchPlateField";

describe("placePlateField", () => {
    /** The field's box meets the plate's left edge, one pixel under it, at the plate's height. */
    it("stands to the left of the plate, at its height and full width", () => {
        const at = placePlateField({ left: 900, top: 500, height: 28 });

        assert.deepEqual(at, {
            left: 900 + 1 - PLATE_FIELD_WIDTH,
            top: 500,
            width: PLATE_FIELD_WIDTH,
            height: 28,
        });
    });

    /** A plate near the window's left edge: the field takes what is there, and stays on screen. */
    it("narrows to the room there is, keeping a margin to the window's edge", () => {
        const at = placePlateField({ left: 120, top: 40, height: 28 });

        assert.equal(at.left, PLATE_FIELD_MARGIN);
        assert.equal(at.width, 120 + 1 - PLATE_FIELD_MARGIN);
        assert.equal(at.left + at.width, 121);
    });

    it("never has a negative width", () => {
        assert.equal(placePlateField({ left: 2, top: 0, height: 28 }).width, 0);
    });
});
