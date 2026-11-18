// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import type { PreviewScene } from "../../protocol/modelPreview";
import { AnimationTile } from "./AnimationTile";
import type { AnimationAction } from "./animationNames";
import {
    type AnimationTransport,
    type AnimationTransportInputs,
    cardPress,
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

describe("cardPress", () => {
    const takes = ["Probe_DIE_00", "Probe_DIE_01"];

    it("plays a card whose clip is not the current one", () => {
        assert.equal(
            cardPress(takes, "Probe_IDLE_00", { running: true, atEnd: false }),
            "play",
        );
        assert.equal(
            cardPress(takes, null, { running: false, atEnd: false }),
            "play",
        );
    });

    it("pauses the current clip while it runs", () => {
        assert.equal(
            cardPress(takes, "Probe_DIE_01", { running: true, atEnd: false }),
            "pause",
        );
    });

    it("plays the current clip again once it has ended", () => {
        assert.equal(
            cardPress(takes, "Probe_DIE_00", { running: false, atEnd: true }),
            "replay",
        );
    });

    it("resumes the current clip where it was paused", () => {
        assert.equal(
            cardPress(takes, "Probe_DIE_00", { running: false, atEnd: false }),
            "resume",
        );
    });
});

describe("AnimationTile", () => {
    const action = {
        label: "Die",
        takes: [
            { name: "Probe_DIE_00", label: "00" },
            { name: "Probe_DIE_01", label: "01" },
        ],
    } as unknown as AnimationAction;

    const tile = (running: boolean): string =>
        renderToStaticMarkup(
            <AnimationTile
                action={action}
                playing="Probe_DIE_00"
                running={running}
                onPress={() => undefined}
                onPressTake={() => undefined}
            />,
        );

    it("reads as playing only while its clip runs", () => {
        assert.match(tile(true), /aria-pressed="true"/);
        assert.match(tile(true), /player-pause/);

        // Ended or paused: still the current clip, but nothing is playing.
        assert.match(tile(false), /aria-pressed="false"/);
        assert.match(tile(false), /player-play/);
        assert.match(tile(false), /anim-card active/);
    });
});
