// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { TransportBar } from "./TransportBar";

describe("TransportBar", () => {
    /** A CD player's row with the clock at its far end, the scrub under it, the speed last. */
    it("lays out the row, the clock, the middle and the speed in that order", () => {
        const html = renderToStaticMarkup(
            <TransportBar
                controls={<button type="button">play</button>}
                clock="1.00 / 2.00s"
                middle={<input className="player-scrub" type="range" />}
                speed={{
                    readout: "1.00x",
                    slider: <input type="range" title="Playback speed" />,
                }}
            />,
        );

        assert.match(
            html,
            /^<div class="player"><div class="player-row"><button type="button">play<\/button><span class="player-time">1.00 \/ 2.00s<\/span><\/div><input class="player-scrub" type="range"\/><div class="field">/,
        );
        assert.match(
            html,
            /<span class="field-label">Speed<span class="section-count">1.00x<\/span><\/span><input type="range" title="Playback speed"\/><\/div><\/div>$/,
        );
    });

    /** A transport with no timeline - a run that is ticked, a particle system - has no clock. */
    it("leaves the clock out when there is none", () => {
        const html = renderToStaticMarkup(
            <TransportBar
                controls={<button type="button">play</button>}
                speed={{ readout: "x1", slider: <input type="range" /> }}
            />,
        );

        assert.equal(html.includes("player-time"), false);
        assert.match(
            html,
            /<div class="player-row"><button type="button">play<\/button><\/div><div class="field">/,
        );
    });

    it("carries a variant after the base class", () => {
        const html = renderToStaticMarkup(
            <TransportBar
                className="sim-transport"
                controls={null}
                speed={{ readout: "x1", slider: null }}
            />,
        );

        assert.match(html, /^<div class="player sim-transport">/);
    });
});
