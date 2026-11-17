// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import type * as THREE from "three";

import type { TranslatedEffect, TranslationResult } from "./fx/effect";
import {
    type AssetLoaderEvents,
    type AssetViewport,
    PreviewAssetLoader,
} from "./previewAssetLoader";

/** What the loader did to the viewport and told the panel, in order. */
interface Recorded {
    posted: { type: string; name: string }[];
    calls: string[];
    problems: { severity: string; message: string }[];
    ledgerChanges: number;
    shaderRequested: number;
    sourceFound: number;
    coverage: { translated: number; total: number }[];
}

function harness(options?: {
    translate?: (
        source: string,
        read: (name: string) => string | null,
    ) => TranslationResult;
    decode?: () => THREE.Texture | null;
}): { loader: PreviewAssetLoader; seen: Recorded } {
    const seen: Recorded = {
        posted: [],
        calls: [],
        problems: [],
        ledgerChanges: 0,
        shaderRequested: 0,
        sourceFound: 0,
        coverage: [],
    };

    const viewport: AssetViewport = {
        applyTranslatedEffect: (name) => seen.calls.push(`translated ${name}`),
        translatedCoverage: () => ({ translated: 1, total: 2 }),
        shaderTextureProblems: () => [],
        applyShaderState: (name) => seen.calls.push(`state ${name}`),
        setTexture: (name) => seen.calls.push(`texture ${name}`),
        setParticleTexture: (name) =>
            seen.calls.push(`particle texture ${name}`),
        setParticleTextureMissing: (name) =>
            seen.calls.push(`particle texture missing ${name}`),
    };

    const events: AssetLoaderEvents = {
        post: (message) => seen.posted.push(message),
        ledgersChanged: () => seen.ledgerChanges++,
        shaderRequested: () => seen.shaderRequested++,
        shaderSourceFound: () => seen.sourceFound++,
        translated: (coverage) => seen.coverage.push(coverage),
        problem: (severity, message) =>
            seen.problems.push({ severity, message }),
    };

    const loader = new PreviewAssetLoader(() => viewport, events, {
        translate:
            options?.translate ??
            (() => ({ effect: null, refusal: null, missingIncludes: [] })),
        decode: options?.decode ?? (() => ({}) as THREE.Texture),
    });

    return { loader, seen };
}

describe("PreviewAssetLoader", () => {
    describe("a texture request", () => {
        // The host answers a repeat with nothing, so a name posted twice would leave the loading
        // cover waiting on a reply that never comes.
        it("posts a name once and counts it once", () => {
            const { loader, seen } = harness();

            loader.requestTexture("hull.dds");
            loader.requestTexture("hull.dds");

            assert.deepEqual(seen.posted, [
                { type: "requestTexture", name: "hull.dds" },
            ]);
            assert.equal(loader.requested, 1);
            assert.equal(loader.settled, 0);
        });

        it("settles on the reply and hands the texture to meshes and particles alike", () => {
            const { loader, seen } = harness();
            loader.requestTexture("hull.dds");

            loader.receiveTexture("hull.dds", { format: "dds", data: "AAAA" });

            assert.equal(loader.settled, 1);
            assert.deepEqual(seen.calls, [
                "texture hull.dds",
                "particle texture hull.dds",
            ]);
            assert.deepEqual(seen.problems, []);
        });

        // A texture that did not resolve is an answer too: the cover stops waiting, the emitters
        // show the missing marker, and the problems bar names the file.
        it("settles a missing texture, marks it and says so", () => {
            const { loader, seen } = harness();
            loader.requestTexture("gone.tga");

            loader.receiveTexture("gone.tga", { data: null });

            assert.equal(loader.settled, 1);
            assert.deepEqual(seen.calls, ["particle texture missing gone.tga"]);
            assert.equal(seen.problems.length, 1);
            assert.equal(seen.problems[0].severity, "warning");
            assert.match(seen.problems[0].message, /gone\.tga/);
        });
    });

    describe("a shader request", () => {
        it("posts a name once and says an effect was asked for", () => {
            const { loader, seen } = harness();

            loader.requestShader("MeshGloss.fx");
            loader.requestShader("MeshGloss.fx");

            assert.deepEqual(seen.posted, [
                { type: "requestShader", name: "MeshGloss.fx" },
            ]);
            assert.equal(seen.shaderRequested, 1);
            assert.equal(loader.requested, 1);
        });

        // A texture and an effect that share a name must not settle each other.
        it("keeps textures and shaders on separate ledgers", () => {
            const { loader } = harness();
            loader.requestTexture("shared");
            loader.requestShader("shared");

            loader.receiveShader("shared", null);

            assert.equal(loader.requested, 2);
            assert.equal(loader.settled, 1);
        });

        it("settles an effect that is not there without touching the viewport", () => {
            const { loader, seen } = harness();
            loader.requestShader("Missing.fx");

            loader.receiveShader("Missing.fx", null);

            assert.equal(loader.settled, 1);
            assert.equal(seen.sourceFound, 0);
            assert.deepEqual(seen.calls, []);
        });

        it("forgets what was asked for when the subject changes", () => {
            const { loader, seen } = harness();
            loader.requestTexture("hull.dds");
            loader.requestShader("MeshGloss.fx");

            loader.clearLedgers();
            loader.requestTexture("hull.dds");

            assert.equal(loader.requested, 1);
            assert.equal(seen.posted.length, 3);
        });
    });

    describe("a pending translation", () => {
        const effect = {} as TranslatedEffect;

        // The effect arrives before the header it includes: it asks for the header, draws with
        // the archetype meanwhile, and translates the moment the header lands.
        it("asks for a missing header and translates once it arrives", () => {
            const { loader, seen } = harness({
                translate: (_source, read) =>
                    read("common.fxh") === null
                        ? {
                              effect: null,
                              refusal: null,
                              missingIncludes: ["common.fxh"],
                          }
                        : { effect, refusal: null, missingIncludes: [] },
            });

            loader.receiveShader("MeshGloss.fx", "#include common.fxh");

            assert.deepEqual(seen.posted, [
                { type: "requestShader", name: "common.fxh" },
            ]);
            assert.deepEqual(seen.calls, []);

            loader.receiveShader("common.fxh", "float4 x;");

            assert.deepEqual(seen.calls, ["translated meshgloss.fx"]);
            assert.deepEqual(seen.coverage, [{ translated: 1, total: 2 }]);
        });

        it("translates an effect once however many headers arrive after it", () => {
            let runs = 0;
            const { loader, seen } = harness({
                translate: () => {
                    runs++;
                    return { effect, refusal: null, missingIncludes: [] };
                },
            });

            loader.receiveShader("MeshGloss.fx", "technique t {}");
            loader.receiveShader("a.fxh", "float a;");
            loader.receiveShader("b.fxh", "float b;");

            assert.equal(runs, 1);
            assert.deepEqual(seen.calls, ["translated meshgloss.fx"]);
        });

        // Nothing more is coming for an effect with no missing header that still did not
        // translate; it keeps the archetype and is not retried.
        it("gives up on an effect that cannot translate and is not waiting on anything", () => {
            let runs = 0;
            const { loader, seen } = harness({
                translate: () => {
                    runs++;
                    return { effect: null, refusal: null, missingIncludes: [] };
                },
            });

            loader.receiveShader("Odd.fx", "technique t {}");
            loader.receiveShader("late.fxh", "float c;");

            assert.equal(runs, 1);
            assert.deepEqual(seen.calls, []);
        });
    });
});
