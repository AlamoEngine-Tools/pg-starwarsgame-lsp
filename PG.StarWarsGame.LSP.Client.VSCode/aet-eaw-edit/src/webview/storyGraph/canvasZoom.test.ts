// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    CANVAS_ZOOM_MAX,
    CANVAS_ZOOM_MIN,
    stepZoom,
    zoomAboutCentre,
} from "./canvasZoom";

describe("zoomAboutCentre", () => {
    /**
     * The graph point under the middle of the view stays there. Zooming from the foot has no
     * pointer to zoom about, and a zoom about the canvas's corner throws the reader's place off
     * the screen.
     */
    it("keeps the graph point at the middle of the view where it is", () => {
        const view = { width: 800, height: 600 };
        const before = { k: 0.5, x: 120, y: -40 };
        const next = 1.25;

        const { ox, oy } = zoomAboutCentre(before, view, next);
        const after = { k: next, x: before.x + ox, y: before.y + oy };

        const graphX = (view.width / 2 - before.x) / before.k;
        const graphY = (view.height / 2 - before.y) / before.k;
        assert.ok(Math.abs(after.x + graphX * after.k - view.width / 2) < 1e-9);
        assert.ok(
            Math.abs(after.y + graphY * after.k - view.height / 2) < 1e-9,
        );
    });

    it("moves nothing when the zoom does not change", () => {
        assert.deepEqual(
            zoomAboutCentre(
                { k: 0.8, x: 10, y: 20 },
                { width: 400, height: 300 },
                0.8,
            ),
            { ox: 0, oy: 0 },
        );
    });
});

describe("stepZoom", () => {
    it("zooms in and out by a quarter, so a press in and a press out come back", () => {
        const k = 0.4;
        assert.ok(Math.abs(stepZoom(k, 1) - 0.5) < 1e-9);
        assert.ok(Math.abs(stepZoom(stepZoom(k, 1), -1) - k) < 1e-9);
    });

    it("stops at the ends of the range", () => {
        assert.equal(stepZoom(CANVAS_ZOOM_MAX, 1), CANVAS_ZOOM_MAX);
        assert.equal(stepZoom(CANVAS_ZOOM_MIN, -1), CANVAS_ZOOM_MIN);
    });

    /** A wheel can zoom past the range; the first press brings it back inside rather than further. */
    it("brings a zoom from outside the range back into it", () => {
        assert.equal(stepZoom(CANVAS_ZOOM_MAX * 3, 1), CANVAS_ZOOM_MAX);
        assert.equal(stepZoom(CANVAS_ZOOM_MIN / 3, -1), CANVAS_ZOOM_MIN);
    });
});
