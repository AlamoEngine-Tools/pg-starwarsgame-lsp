// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import type { PreviewScene } from "../../protocol/modelPreview";
import {
    type AnimationTransport,
    type AnimationTransportInputs,
    useAnimationTransport,
} from "./useAnimationTransport";
import type { ViewportStats } from "./viewport";

function inputs(
    overrides: Partial<AnimationTransportInputs>,
): AnimationTransportInputs {
    return {
        scene: null,
        stats: null,
        viewportRef: { current: null },
        ...overrides,
    };
}

/** What the transport holds on the first render, through a component that only calls it. */
function firstRender(given: AnimationTransportInputs): AnimationTransport {
    let seen: AnimationTransport | undefined;
    const Probe = (): null => {
        seen = useAnimationTransport(given);
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useAnimationTransport", () => {
    it("opens with no clip, looping, at the clip's own rate", () => {
        const player = firstRender(inputs({}));

        assert.equal(player.animation, null);
        assert.equal(player.animationLoop, true);
        assert.equal(player.animationSpeed, 1);
        assert.equal(player.playing, false);
        assert.equal(player.atEnd, false);
        assert.equal(player.roulette, null);
        assert.deepEqual(player.orderedClips, []);
    });

    it("steps through every clip of the library, each with the action it belongs to", () => {
        const clips = ["Probe_IDLE_00", "Probe_IDLE_01", "Probe_MOVE_00"];
        const player = firstRender(
            inputs({
                scene: {
                    animationSource: "Probe.alo",
                    parts: [],
                } as unknown as PreviewScene,
                stats: { animations: clips } as unknown as ViewportStats,
            }),
        );

        assert.deepEqual([...player.orderedClips].sort(), [...clips].sort());
        for (const clip of clips) {
            assert.ok(player.actionOfClip.has(clip), clip);
        }
    });
});
