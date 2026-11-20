// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { DockFoot, type DockFootProps } from "./DockFoot";

/** The Model lens's foot over a subject with `boneCount` bones and nothing else running. */
function foot(boneCount: number): string {
    const props = {
        abilityState: { activeAbilities: new Set() },
        boneCount,
        combat: { destroyed: new Set() },
        mode: "model",
        particleReady: false,
        particleSpeed: 1,
        particleTransport: {
            run: "playing",
            togglePlay: () => undefined,
            stop: () => undefined,
            restart: () => undefined,
        },
        player: { animation: null, animations: [] },
        scene: null,
        setMode: () => undefined,
        setParticleSpeed: () => undefined,
        setSkeletonMode: () => undefined,
        skeletonMode: "off",
        viewportRef: { current: null },
    } as unknown as DockFootProps;

    return renderToStaticMarkup(<DockFoot {...props} />);
}

/** The Skeleton field's buttons, as rendered. */
const skeletonButtons = (html: string): string[] => {
    const field =
        /<div class="field skeleton-field">[\s\S]*?<\/span><\/div>/.exec(html);
    assert.ok(field, html);
    return [...field[0].matchAll(/<button[^>]*>/g)].map((m) => m[0]);
};

describe("DockFoot", () => {
    // Measured: a particle file has no bones - 0 on P_fire_med00 and P_explosion_big00, and none of
    // the 987 particle .alo files in eaw and foc carries a skeleton chunk. The control stays, and says
    // why it cannot act; gated on the bones, not on the scene kind, so a boneless model says the same.
    it("disables the skeleton control and says why when the subject has no bones", () => {
        const buttons = skeletonButtons(foot(0));

        assert.equal(buttons.length, 3);
        for (const button of buttons) {
            assert.match(button, /disabled=""/);
            assert.match(button, /title="No skeleton loaded"/);
        }
    });

    it("offers the skeleton control when the subject has bones", () => {
        const buttons = skeletonButtons(foot(11));

        assert.equal(buttons.length, 3);
        for (const button of buttons) {
            assert.equal(/disabled/.test(button), false, button);
        }
    });
});
