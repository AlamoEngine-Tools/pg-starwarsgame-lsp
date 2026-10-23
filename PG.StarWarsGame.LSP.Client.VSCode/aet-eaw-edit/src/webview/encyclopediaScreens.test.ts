// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    DEFAULT_ENCYCLOPEDIA_SCREEN,
    ENCYCLOPEDIA_SCREENS,
    encyclopediaScreenFrom,
    encyclopediaScreenId,
} from "./encyclopediaScreens";

describe("encyclopediaScreens", () => {
    it("draws for 1920x1080 unless told otherwise", () => {
        assert.deepEqual(DEFAULT_ENCYCLOPEDIA_SCREEN, {
            width: 1920,
            height: 1080,
        });
        assert.ok(
            ENCYCLOPEDIA_SCREENS.some(
                (s) => encyclopediaScreenId(s) === "1920x1080",
            ),
        );
    });

    /** The game's own detail presets start at these; the rest are the common displays. */
    it("offers the game's preset resolutions among the common ones", () => {
        const ids = ENCYCLOPEDIA_SCREENS.map(encyclopediaScreenId);
        for (const preset of ["800x600", "1024x768", "1280x1024"]) {
            assert.ok(ids.includes(preset), preset);
        }
    });

    it("lists the screens by height, then width", () => {
        const sorted = [...ENCYCLOPEDIA_SCREENS].sort(
            (a, b) => a.height - b.height || a.width - b.width,
        );
        assert.deepEqual(ENCYCLOPEDIA_SCREENS, sorted);
    });

    it("reads back a listed screen, by object or by id", () => {
        assert.deepEqual(
            encyclopediaScreenFrom({ width: 2560, height: 1440 }),
            {
                width: 2560,
                height: 1440,
            },
        );
        assert.deepEqual(encyclopediaScreenFrom("1024x768"), {
            width: 1024,
            height: 768,
        });
    });

    /** A stored value outlives the build that wrote it: anything not listed is the default. */
    it("falls back to the default for anything else", () => {
        for (const stored of [
            undefined,
            null,
            "garbage",
            { width: 1234, height: 567 },
            { width: "1920", height: "1080" },
        ]) {
            assert.deepEqual(
                encyclopediaScreenFrom(stored),
                DEFAULT_ENCYCLOPEDIA_SCREEN,
            );
        }
    });
});
