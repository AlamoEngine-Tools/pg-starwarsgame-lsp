// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { DEFAULT_CAPTURE_SIZE } from "./capture";
import { type CaptureSettings, useCaptureSettings } from "./useCaptureSettings";

/** What the capture settings hold on the first render, through a component that only calls it. */
function firstRender(): CaptureSettings {
    let seen: CaptureSettings | undefined;
    const Probe = (): null => {
        seen = useCaptureSettings();
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useCaptureSettings", () => {
    it("opens at the default size, with a transparent background and no scenery", () => {
        const capture = firstRender();

        assert.equal(capture.captureSizeId, DEFAULT_CAPTURE_SIZE.id);
        assert.deepEqual(capture.captureDims, {
            width: DEFAULT_CAPTURE_SIZE.width,
            height: DEFAULT_CAPTURE_SIZE.height,
        });
        assert.equal(capture.captureTransparent, true);
        assert.equal(capture.captureScenery, false);
    });
});
