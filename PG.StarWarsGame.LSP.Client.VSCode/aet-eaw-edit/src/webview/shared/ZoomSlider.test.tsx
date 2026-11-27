// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { sliderOf, ZoomSlider, zoomOf } from "./ZoomSlider";

const noop = (): void => undefined;

describe("ZoomSlider", () => {
    it("is a range and the zoom in percent beside it", () => {
        const html = renderToStaticMarkup(
            <ZoomSlider
                value={3}
                min={1}
                max={5}
                step={0.25}
                label="Zoom"
                title="Drag, or scroll over the preview"
                onZoom={noop}
            />,
        );

        assert.match(
            html,
            /^<div class="zoom-slider"><input type="range"[^>]*aria-label="Zoom"[^>]*title="Drag, or scroll over the preview"[^>]*>/,
        );
        assert.match(html, /min="1"/);
        assert.match(html, /max="5"/);
        assert.match(html, /step="0.25"/);
        assert.match(html, /value="3"/);
        assert.match(html, /<span class="zoom-readout">300%<\/span><\/div>$/);
    });

    /** Rounded: a percentage with decimals is noise while the slider is being dragged. */
    it("rounds the readout to a whole percent", () => {
        assert.match(
            renderToStaticMarkup(
                <ZoomSlider
                    value={0.0846}
                    min={0.01}
                    max={2}
                    logarithmic
                    label="Zoom"
                    onZoom={noop}
                />,
            ),
            /<span class="zoom-readout">8%<\/span>/,
        );
    });

    /**
     * A range from 1% to 200% read linearly gives the whole of a large campaign's useful zooms
     * the first few pixels of the track. Logarithmic, each doubling takes the same length.
     */
    it("lays a logarithmic range out by doublings, to within one of its thousand steps", () => {
        const range = { min: 0.01, max: 2 };
        const quarter = sliderOf(0.25, range);
        const half = sliderOf(0.5, range);
        const whole = sliderOf(1, range);

        assert.ok(Math.abs(half - quarter - (whole - half)) <= 1);
        assert.equal(sliderOf(range.min, range), 0);
        assert.equal(sliderOf(range.max, range), 1000);
        // One step is a factor of 200^(1/1000), about half a percent.
        assert.ok(
            Math.abs(zoomOf(sliderOf(0.37, range), range) / 0.37 - 1) < 0.006,
        );
    });

    it("holds a zoom from outside the range at the end of the track", () => {
        const range = { min: 0.01, max: 2 };
        assert.equal(sliderOf(5, range), 1000);
        assert.equal(sliderOf(0.001, range), 0);
    });
});
