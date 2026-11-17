// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import type { PreviewScene } from "../../protocol/modelPreview";
import {
    type CombatLens,
    type CombatLensInputs,
    useCombatLens,
} from "./useCombatLens";

const noop = (): void => undefined;

/** A scene with only what the lens reads at render time; the rest of a real one is not looked at. */
function sceneWith(fields: object): PreviewScene {
    return {
        subject: "Probe",
        hardpoints: [],
        parts: [],
        particles: [],
        ...fields,
    } as unknown as PreviewScene;
}

function inputs(overrides: Partial<CombatLensInputs>): CombatLensInputs {
    return {
        abilityProxyIds: new Map(),
        activeAbilities: new Set(),
        animation: null,
        breakoffsRef: { current: [] },
        destroyedRef: { current: new Set() },
        dropPassiveEffects: noop,
        effectDrawsNow: () => true,
        fireArcs: false,
        mode: "model",
        playEffect: noop,
        playhead: { time: 0, duration: 0, running: false },
        scene: null,
        sceneRef: { current: null },
        setAlt: noop,
        setTreeItems: noop,
        viewportRef: { current: null },
        ...overrides,
    } as CombatLensInputs;
}

/** What the lens holds on the first render, through a component that only calls it. */
function firstRender(given: CombatLensInputs): CombatLens {
    let seen: CombatLens | undefined;
    const Probe = (): null => {
        seen = useCombatLens(given);
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useCombatLens", () => {
    it("opens on an undamaged unit with the hull targeted", () => {
        const lens = firstRender(
            inputs({ scene: sceneWith({ defence: { tacticalHealth: 400 } }) }),
        );

        assert.deepEqual(lens.hull, { current: 400, max: 400 });
        assert.equal(lens.fireTarget, "hull");
        assert.equal(lens.destroyed.size, 0);
        assert.deepEqual(lens.damageLog, []);
    });

    it("hands the damage stage to the hull only in Gameplay, and only with a damage table", () => {
        const table = { damageTable: [{}] };

        assert.equal(
            firstRender(inputs({ mode: "gameplay", scene: sceneWith(table) }))
                .altDrivenByHull,
            true,
        );
        assert.equal(
            firstRender(inputs({ mode: "model", scene: sceneWith(table) }))
                .altDrivenByHull,
            false,
        );
        assert.equal(
            firstRender(inputs({ mode: "gameplay", scene: sceneWith({}) }))
                .altDrivenByHull,
            false,
        );
    });

    it("has nothing to fire at and nothing to carry without a scene", () => {
        const lens = firstRender(inputs({}));

        assert.deepEqual(lens.hull, { current: 0, max: 0 });
        assert.deepEqual(lens.cards, []);
        assert.deepEqual(lens.weapons, []);
    });

    it("writes the destroyed hardpoints into the ref it is handed", () => {
        const destroyedRef = {
            current: new Set(["stale"]) as ReadonlySet<string>,
        };
        const lens = firstRender(inputs({ destroyedRef }));

        assert.equal(destroyedRef.current, lens.destroyed);
        assert.equal(destroyedRef.current.size, 0);
    });
});
