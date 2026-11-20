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

/** The effect decider on the first render, in a preview that applies the game's rules or not. */
function decider(gameRules: boolean): ParticleEffects["effectDrawsNow"] {
    let seen: ParticleEffects | undefined;
    const viewport = {
        appliesGameRules: () => gameRules,
    } as unknown as PreviewViewport;
    const Probe = (): null => {
        seen = useParticleEffects({
            assets: {} as PreviewAssetLoader,
            refreshStats: () => undefined,
            setHiddenEmitters: () => undefined,
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
        const drawsNow = decider(false);

        assert.equal(drawsNow(particle()), true, "a dust cloud is held back");
        assert.equal(
            drawsNow(particle({ systemRef: "pte_engines" })),
            true,
            "a turbo engine is held back",
        );
        assert.equal(drawsNow(particle({ startsVisible: false })), false);
    });

    /**
     * Measured in the game: a unit is created with every attached effect running that no switch
     * hides. The switches - the name groups among them - are the unit's rules' writes, over this.
     */
    it("runs every effect the file marks visible in a unit preview too", () => {
        const drawsNow = decider(true);

        assert.equal(drawsNow(particle()), true, "a dust cloud is held back");
        assert.equal(drawsNow(particle({ startsVisible: false })), false);
    });
});
