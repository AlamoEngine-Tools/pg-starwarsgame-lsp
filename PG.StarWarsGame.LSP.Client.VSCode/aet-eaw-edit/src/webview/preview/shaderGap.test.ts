// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { shaderGapNotice } from "./shaderGap";

describe("shaderGapNotice", () => {
    it("explains the gap when translation is on, shaders were asked for and none came", () => {
        assert.equal(shaderGapNotice(true, true, false), true);
    });

    /**
     * A particle system asks for no effect at all - its sprites are drawn by the preview's own
     * shaders - so there is no gap to explain. Opening an asteroid field used to show "No shader
     * sources are reachable" with nothing to translate in the first place.
     */
    it("says nothing when no shader was ever asked for", () => {
        assert.equal(shaderGapNotice(true, false, false), false);
    });

    it("says nothing once a source has arrived", () => {
        assert.equal(shaderGapNotice(true, true, true), false);
    });

    it("says nothing while translation is off", () => {
        assert.equal(shaderGapNotice(false, true, false), false);
    });
});
