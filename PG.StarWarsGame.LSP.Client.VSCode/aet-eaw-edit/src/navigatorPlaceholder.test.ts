// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";
import * as l10n from "@vscode/l10n";

import { emptyNavigatorMessage } from "./navigatorPlaceholder";

describe("emptyNavigatorMessage", () => {
    const none = "No localisation files found in this workspace.";

    // The bug: the tree announced an empty workspace while the scan was still running, then
    // refreshed into the real file list.
    it("does not claim there are none before the workspace has been read", () => {
        assert.equal(emptyNavigatorMessage(false, none), "Loading...");
    });

    it("states it plainly once the workspace has been read", () => {
        assert.equal(emptyNavigatorMessage(true, none), none);
    });

    // Each view words its own whole sentence: a noun glued into one gives a translator nothing to work with.
    it("reads its own text from the catalogue", () => {
        l10n.config({ contents: { "Loading...": "TRANSLATED LOADING" } });
        try {
            assert.equal(
                emptyNavigatorMessage(false, none),
                "TRANSLATED LOADING",
            );
        } finally {
            l10n.config({ contents: {} });
        }
    });
});
