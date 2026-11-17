// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import {
    type OverlayPills,
    type OverlayPillsInputs,
    useOverlayPills,
} from "./useOverlayPills";

const noop = (): void => undefined;

function inputs(overrides: Partial<OverlayPillsInputs>): OverlayPillsInputs {
    return {
        emitters: [],
        fireArcs: false,
        floor: true,
        grid: true,
        groups: [],
        hiddenWeapons: new Set(),
        mode: "model",
        particlesOn: true,
        reticleState: "enemy",
        reticlesOn: false,
        scene: null,
        setFireArcs: noop,
        setFloor: noop,
        setGrid: noop,
        setHiddenWeapons: noop,
        setParticlesOn: noop,
        setReticlesOn: noop,
        setWireframe: noop,
        weapons: [],
        wireframe: false,
        ...overrides,
    };
}

/** What the pills hold on the first render, through a component that only calls it. */
function firstRender(given: OverlayPillsInputs): OverlayPills {
    let seen: OverlayPills | undefined;
    const Probe = (): null => {
        seen = useOverlayPills(given);
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useOverlayPills", () => {
    it("keeps Effects on the strip, disabled and off, when the model has no effect", () => {
        const { overlays } = firstRender(inputs({}));

        assert.deepEqual(
            overlays.map((pill) => pill.id),
            ["grid", "floor", "wireframe", "particles"],
        );
        const effects = overlays[3];
        assert.equal(effects.disabled, true);
        assert.equal(effects.on, false);
    });

    it("puts Arcs and Reticles last in Gameplay, disabled with nothing to draw", () => {
        const { overlays } = firstRender(inputs({ mode: "gameplay" }));

        assert.deepEqual(
            overlays.map((pill) => pill.id),
            ["grid", "floor", "wireframe", "particles", "arcs", "reticles"],
        );
        assert.equal(overlays[4].disabled, true);
        assert.equal(overlays[5].disabled, true);
    });
});
