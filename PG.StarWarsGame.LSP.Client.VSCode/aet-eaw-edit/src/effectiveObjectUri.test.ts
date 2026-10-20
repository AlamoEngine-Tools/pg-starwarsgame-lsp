// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    effectiveObjectPath,
    effectiveObjectQuery,
    parseEffectiveObjectQuery,
} from "./effectiveObjectUri";

describe("effective object URI", () => {
    it("carries the object and its project through a round trip", () => {
        const ref = {
            objectId: "Star_Destroyer",
            projectKey: "c:/dev/eawx/rev/rev.pgproj",
        };
        assert.deepEqual(
            parseEffectiveObjectQuery(effectiveObjectQuery(ref)),
            ref,
        );
    });

    it("gives one object of two projects two documents", () => {
        assert.notEqual(
            effectiveObjectQuery({
                objectId: "Star_Destroyer",
                projectKey: "c:/dev/eawx/rev/rev.pgproj",
            }),
            effectiveObjectQuery({
                objectId: "Star_Destroyer",
                projectKey: "c:/dev/eawx/fotr/fotr.pgproj",
            }),
        );
    });

    it("reads an older document's query as the object alone", () => {
        assert.deepEqual(parseEffectiveObjectQuery("Star_Destroyer"), {
            objectId: "Star_Destroyer",
            projectKey: "",
        });
    });

    it("names the project in the tab once the window runs several", () => {
        assert.equal(
            effectiveObjectPath("Star_Destroyer", "Rev", 1),
            "/Star_Destroyer (effective preview, read-only).xml",
        );
        assert.equal(
            effectiveObjectPath("Star_Destroyer", "Rev", 2),
            "/Star_Destroyer - Rev (effective preview, read-only).xml",
        );
    });
});
