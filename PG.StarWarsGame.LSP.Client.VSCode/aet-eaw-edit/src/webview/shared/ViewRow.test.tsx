// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { ViewRow } from "./ViewRow";

describe("ViewRow", () => {
    it("is a row of controls side by side", () => {
        assert.equal(
            renderToStaticMarkup(
                <ViewRow>
                    <i>a</i>
                </ViewRow>,
            ),
            '<span class="view-row"><i>a</i></span>',
        );
    });

    it("carries a variant after the base class", () => {
        assert.equal(
            renderToStaticMarkup(
                <ViewRow className="card-actions">
                    <i>a</i>
                </ViewRow>,
            ),
            '<span class="view-row card-actions"><i>a</i></span>',
        );
    });
});
