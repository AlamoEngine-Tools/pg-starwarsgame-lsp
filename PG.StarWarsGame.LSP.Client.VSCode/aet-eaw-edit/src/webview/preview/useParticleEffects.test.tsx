// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import {
    PREVIEW_PARTICLE_GATE,
    type PreviewParticle,
} from "../../protocol/modelPreview";
import { type PreviewAssetLoader } from "./previewAssetLoader";
import { type ParticleEffects, useParticleEffects } from "./useParticleEffects";
import { type PreviewViewport } from "./viewport";

const particle = (over: Partial<PreviewParticle> = {}): PreviewParticle => ({
    id: "hull#0",
    systemRef: "p_dust_cloud",
    partId: "hull",
    bone: "p_dust",
    boneIndex: 3,
    gate: PREVIEW_PARTICLE_GATE.always,
    startsVisible: true,
    ...over,
});

/**
 * The effect decider on the first render, in a preview that applies the game's rules or not, where
 * `unbound` is what the unit's abilities say can never be switched on.
 */
function decider(
    gameRules: boolean,
    unbound: ReadonlySet<string> = new Set(),
): ParticleEffects["effectDrawsNow"] {
    let seen: ParticleEffects | undefined;
    const viewport = {
        appliesGameRules: () => gameRules,
    } as unknown as PreviewViewport;
    const Probe = (): null => {
        seen = useParticleEffects({
            abilityProxyIdsRef: { current: new Map() },
            activeAbilitiesRef: { current: new Set() },
            assets: {} as PreviewAssetLoader,
            destroyedRef: { current: new Set() },
            refreshStats: () => undefined,
            replacedEffectsRef: { current: new Set() },
            setHiddenEmitters: () => undefined,
            unboundEffectsRef: { current: unbound },
            viewportRef: { current: viewport },
        });
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen.effectDrawsNow;
}

describe("useParticleEffects", () => {
    it("runs every effect a model file marks visible in a model preview, and no other", () => {
        const drawsNow = decider(false, new Set(["hull#1"]));

        assert.equal(drawsNow(particle()), true, "a dust cloud is held back");
        assert.equal(
            drawsNow(particle({ id: "hull#1", systemRef: "pte_engines" })),
            true,
            "an effect claiming an undeclared ability is held back",
        );
        assert.equal(drawsNow(particle({ startsVisible: false })), false);
    });

    it("applies the unit's rules over the file in a unit preview", () => {
        const drawsNow = decider(true, new Set(["hull#1"]));

        assert.equal(drawsNow(particle({ id: "hull#1" })), false);
        // Held back on open, until #448 brings the unit's opening in line with the game.
        assert.equal(drawsNow(particle()), false);
    });
});
