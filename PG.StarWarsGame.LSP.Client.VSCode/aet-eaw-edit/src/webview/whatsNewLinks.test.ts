// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { DISCUSSIONS_URL, ISSUES_URL, releaseUrl } from "./whatsNewLinks";

describe("whatsNewLinks", () => {
    it("links the installed version's release by its tag", () => {
        assert.equal(
            releaseUrl("0.5.0"),
            "https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases/tag/v0.5.0",
        );
    });

    it("keeps a version that is not a plain version inside the tag segment", () => {
        assert.equal(
            releaseUrl("Unreleased/../x"),
            "https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases/tag/vUnreleased%2F..%2Fx",
        );
    });

    it("links the issues and the discussions of the repository", () => {
        assert.equal(
            ISSUES_URL,
            "https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues",
        );
        assert.equal(
            DISCUSSIONS_URL,
            "https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/discussions",
        );
    });
});
