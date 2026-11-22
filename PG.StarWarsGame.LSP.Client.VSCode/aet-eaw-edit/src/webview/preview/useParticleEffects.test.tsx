// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import {
    type AlamoParticleContent,
    PREVIEW_PARTICLE_GATE,
    type PreviewParticle,
} from "../../protocol/modelPreview";
import { emitter, testSystem } from "./particleFixture";
import { PreviewAssetLoader } from "./previewAssetLoader";
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

/** The hook over a real loader, with what it posted and what it put in the viewport. */
function effects(): {
    hook: ParticleEffects;
    posted: { type: string; name: string }[];
    started: { how: string; id: string; scale: number | undefined }[];
    /** The ids put in the viewport by each batch the hook opened, and those it put in outside one. */
    changes: { batches: string[][]; loose: string[] };
} {
    const posted: { type: string; name: string }[] = [];
    const started: { how: string; id: string; scale: number | undefined }[] =
        [];
    const changes: { batches: string[][]; loose: string[] } = {
        batches: [],
        loose: [],
    };
    let open: string[] | null = null;
    const noted = (id: string): void => {
        (open ?? changes.loose).push(id);
    };
    const viewport = {
        appliesGameRules: () => true,
        hasPart: () => true,
        particleTextureNames: () => [],
        shaderNames: () => [],
        removeParticleSystem: () => undefined,
        setParticleSystemVisible: () => undefined,
        batch: (work: () => void) => {
            // A batch inside a batch is the same change.
            if (open !== null) {
                work();
                return;
            }

            open = [];
            try {
                work();
            } finally {
                changes.batches.push(open);
                open = null;
            }
        },
        playOnce: (
            id: string,
            _system: AlamoParticleContent,
            _part?: string,
            _bone?: string,
            scale?: number,
        ) => {
            noted(id);
            started.push({ how: "once", id, scale });
        },
        addParticleSystem: (
            id: string,
            _system: AlamoParticleContent,
            placement: { scaleFactor?: number },
        ) => {
            noted(id);
            started.push({ how: "kept", id, scale: placement.scaleFactor });
        },
    } as unknown as PreviewViewport;
    const assets = new PreviewAssetLoader(() => null, {
        post: (message) => posted.push(message),
        ledgersChanged: () => undefined,
        shaderRequested: () => undefined,
        shaderSourceFound: () => undefined,
        shaderSourcesForgotten: () => undefined,
        translated: () => undefined,
        problem: () => undefined,
    });

    let seen: ParticleEffects | undefined;
    const Probe = (): null => {
        seen = useParticleEffects({
            assets,
            refreshStats: () => undefined,
            setHiddenEmitters: () => undefined,
            viewportRef: { current: viewport },
        });
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return { hook: seen, posted, started, changes };
}

const EXPLOSION = "Large_Explosion_Space";

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

    describe("an effect started by name", () => {
        // Measured on a mod's capital ship: 145 hardpoints destroyed at once asked for the one
        // explosion 145 times, and the answers came back one by one over ten seconds.
        it("is asked for once however many are waiting on it, and all of them play when it arrives", () => {
            const { hook, posted, started } = effects();

            hook.playEffect("explosion:a", EXPLOSION, "hull", "HP_A", true);
            hook.playEffect("explosion:b", EXPLOSION, "hull", "HP_B", true);
            hook.playEffect("wreck:c", EXPLOSION, "wreck", undefined, false);

            assert.deepEqual(posted, [
                { type: "requestParticleSystem", name: EXPLOSION },
            ]);
            assert.deepEqual(started, []);

            hook.takeSystem(EXPLOSION, testSystem([emitter()]), 2);

            assert.deepEqual(started, [
                { how: "once", id: "explosion:a", scale: 2 },
                { how: "once", id: "explosion:b", scale: 2 },
                { how: "kept", id: "wreck:c", scale: 2 },
            ]);
        });

        it("plays at once, and asks for nothing, when its system is in hand", () => {
            const { hook, posted, started } = effects();
            hook.takeSystem(EXPLOSION, testSystem([emitter()]), 1);
            posted.length = 0;

            hook.playEffect("explosion:a", EXPLOSION, "hull", "HP_A", true);

            assert.deepEqual(
                posted.filter((p) => p.type === "requestParticleSystem"),
                [],
            );
            assert.deepEqual(started, [
                { how: "once", id: "explosion:a", scale: 1 },
            ]);
        });

        // The other half of loading up front: a system that arrives with nothing waiting on it is
        // kept for later and puts no emitter in the scene. A unit must not blow up on opening.
        it("starts nothing when its system arrives with nothing waiting on it", () => {
            const { hook, started } = effects();

            hook.takeSystem(EXPLOSION, testSystem([emitter()]), 1);

            assert.deepEqual(started, []);
        });
    });

    describe("the effects a scene can start", () => {
        it("are all asked for when the scene arrives, each once", () => {
            const { hook, posted } = effects();

            hook.prefetchEffects({
                hardpoints: [
                    { deathExplosionParticles: EXPLOSION },
                    { deathExplosionParticles: EXPLOSION },
                ],
                damagedSmokeAsset: "Damage_Smoke",
                deathExplosions: "Huge_Explosion_Space",
            });

            assert.deepEqual(posted, [
                { type: "requestParticleSystem", name: EXPLOSION },
                { type: "requestParticleSystem", name: "Damage_Smoke" },
                { type: "requestParticleSystem", name: "Huge_Explosion_Space" },
            ]);
        });

        it("play without a request once they have arrived", () => {
            const { hook, posted, started } = effects();
            hook.prefetchEffects({
                hardpoints: [{ deathExplosionParticles: EXPLOSION }],
            });
            hook.takeSystem(EXPLOSION, testSystem([emitter()]), 1);
            posted.length = 0;

            hook.playEffect("explosion:a", EXPLOSION, "hull", "HP_A", true);

            assert.deepEqual(posted, []);
            assert.equal(started.length, 1);
        });
    });

    describe("the effects a wreck or a death clone carries", () => {
        // With a scene's effects fetched up front their systems are in hand when the wreck is
        // dropped, so no answer is on its way to attach them: they attach as they are queued.
        it("attach at once when their systems are in hand", () => {
            const { hook, posted, started } = effects();
            hook.takeSystem("p_wreck_fire", testSystem([emitter()]), 1);
            posted.length = 0;

            hook.queuePassiveEffects(
                [particle({ systemRef: "p_wreck_fire" })],
                "wreck:1",
            );

            assert.deepEqual(started, [
                { how: "kept", id: "wreck:1#0", scale: 1 },
            ]);
        });

        it("wait for a system that is not, asked for once", () => {
            const { hook, posted, started } = effects();

            hook.queuePassiveEffects(
                [
                    particle({ systemRef: "p_wreck_fire" }),
                    particle({ systemRef: "P_Wreck_Fire" }),
                ],
                "wreck:1",
            );

            assert.deepEqual(started, []);
            assert.deepEqual(posted, [
                { type: "requestParticleSystem", name: "p_wreck_fire" },
            ]);

            hook.takeSystem("p_wreck_fire", testSystem([emitter()]), 1);

            assert.equal(started.length, 2);
        });
    });

    // Measured on a mod's capital ship: every system put in the scene re-ran the row chain and
    // measured the model, one 3.7 second frame while the unit loaded.
    describe("the systems put in the scene together", () => {
        const startedIn = (batches: string[][]): string[][] =>
            batches.filter((ids) => ids.length > 0);

        it("are one change when they were waiting on a system that arrives", () => {
            const { hook, changes } = effects();
            hook.playEffect("explosion:a", EXPLOSION, "hull", "HP_A", true);
            hook.playEffect("explosion:b", EXPLOSION, "hull", "HP_B", true);
            hook.playEffect("wreck:c", EXPLOSION, "wreck", undefined, false);

            hook.takeSystem(EXPLOSION, testSystem([emitter()]), 1);

            assert.deepEqual(changes.loose, []);
            assert.deepEqual(startedIn(changes.batches), [
                ["explosion:a", "explosion:b", "wreck:c"],
            ]);
        });

        it("are one change when a part's own effects attach", () => {
            const { hook, changes } = effects();
            hook.takeSystem("p_wreck_fire", testSystem([emitter()]), 1);

            hook.queuePassiveEffects(
                [
                    particle({ systemRef: "p_wreck_fire" }),
                    particle({ systemRef: "p_wreck_fire" }),
                ],
                "wreck:1",
            );

            assert.deepEqual(changes.loose, []);
            assert.deepEqual(startedIn(changes.batches), [
                ["wreck:1#0", "wreck:1#1"],
            ]);
        });
    });

    describe("an effect that could not be read", () => {
        // Asked for once per scene, so nothing will ever answer for it again. Waiting on it would
        // keep every later explosion queued - and play them all together if it ever did arrive.
        it("is not waited on again", () => {
            const { hook, started } = effects();
            hook.playEffect("explosion:a", EXPLOSION, "hull", "HP_A", true);

            hook.systemMissing(EXPLOSION);
            hook.playEffect("explosion:b", EXPLOSION, "hull", "HP_B", true);
            hook.takeSystem(EXPLOSION, testSystem([emitter()]), 1);

            assert.deepEqual(started, []);
        });
    });

    describe("a new scene", () => {
        // What was waiting belongs to the parts of the scene before. Its system arriving for the
        // new scene - it is asked for again up front - must not set the old explosions off.
        it("forgets the effects still waiting and the systems in hand", () => {
            const { hook, posted, started } = effects();
            hook.takeSystem("Damage_Smoke", testSystem([emitter()]), 1);
            hook.playEffect("explosion:a", EXPLOSION, "hull", "HP_A", true);

            hook.forgetSystems();
            posted.length = 0;
            hook.takeSystem(EXPLOSION, testSystem([emitter()]), 1);
            hook.playEffect(
                "smoke:1",
                "Damage_Smoke",
                "hull",
                undefined,
                false,
            );

            assert.deepEqual(started, []);
            assert.deepEqual(posted, [
                { type: "requestParticleSystem", name: "Damage_Smoke" },
            ]);
        });
    });
});
