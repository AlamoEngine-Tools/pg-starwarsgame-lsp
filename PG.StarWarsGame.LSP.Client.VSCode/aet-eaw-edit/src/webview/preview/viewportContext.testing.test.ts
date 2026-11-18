// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { standInContext } from "./viewportContext.testing";

describe("standInContext", () => {
    it("hands back what the test gave", () => {
        const levels = { alt: 2 };
        const context = standInContext({ levels });

        assert.equal(context.levels, levels);
    });

    it("names the member a layer read that the test did not give", () => {
        const context = standInContext({});

        assert.throws(
            () => context.camera,
            /The stand-in context has no 'camera'\./,
        );
    });
});
