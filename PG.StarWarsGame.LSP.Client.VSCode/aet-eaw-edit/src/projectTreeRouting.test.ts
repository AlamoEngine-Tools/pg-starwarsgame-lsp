// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { routeChildren } from "./projectTreeRouting";

describe("routeChildren", () => {
    it("lists a single project's tree at the top, without a project row", () => {
        assert.deepEqual(routeChildren([""], undefined, true), {
            kind: "tree",
            key: "",
            fromRoot: true,
        });
    });

    it("opens a row of the window's one tree, filed under the empty key", () => {
        assert.deepEqual(routeChildren([""], "", false), {
            kind: "tree",
            key: "",
            fromRoot: false,
        });
    });

    it("lists the projects at the top when the window holds several", () => {
        assert.deepEqual(routeChildren(["a", "b"], undefined, true), {
            kind: "projects",
        });
    });

    it("opens a project row into that project's tree", () => {
        assert.deepEqual(routeChildren(["a", "b"], undefined, false, "b"), {
            kind: "tree",
            key: "b",
            fromRoot: true,
        });
    });

    it("gives a row no tree produced no children", () => {
        assert.deepEqual(routeChildren(["a"], undefined, false), {
            kind: "none",
        });
    });
});
