// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import type * as THREE from "three";

import type { TranslatedEffect, TranslationResult } from "./fx/effect";
import { emitter, properties, testSystem } from "./particleFixture";
import {
    type AssetLoaderEvents,
    type AssetViewport,
    decodeTexture,
    PreviewAssetLoader,
} from "./previewAssetLoader";

/** A 2x2 8-bit RGBA PNG, written by Python's zlib. */
const TWO_BY_TWO_PNG = new Uint8Array([
    137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 2, 0,
    0, 0, 2, 8, 6, 0, 0, 0, 114, 182, 13, 36, 0, 0, 0, 26, 73, 68, 65, 84, 120,
    218, 99, 224, 18, 145, 251, 175, 97, 100, 243, 159, 193, 45, 32, 234, 127,
    74, 94, 69, 3, 0, 50, 91, 6, 138, 121, 232, 132, 18, 0, 0, 0, 0, 73, 69, 78,
    68, 174, 66, 96, 130,
]);

/** A 1x1 uncompressed 24-bit TGA. */
const ONE_PIXEL_TGA = new Uint8Array([
    0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 24, 0, 30, 20, 10,
]);

/** What the loader did to the viewport and told the panel, in order. */
interface Recorded {
    posted: { type: string; name: string; have?: string; request?: number }[];
    calls: string[];
    problems: { severity: string; message: string }[];
    ledgerChanges: number;
    shaderRequested: number;
    sourceFound: number;
    sourcesForgotten: number;
    coverage: { translated: number; total: number }[];
    /** Every heat strength handed to the viewport, null for "none declared". */
    heat: (number | null)[];
    /** How many textures were copied to the graphics card on arrival. */
    uploads: number;
    /** Asked for and not yet answered, as the panel would read it at each change. */
    outstanding: number[];
}

function harness(options?: {
    translate?: (
        source: string,
        read: (name: string) => string | null,
    ) => TranslationResult;
    decode?: (bytes: Uint8Array, format: string) => THREE.Texture | null;
}): { loader: PreviewAssetLoader; seen: Recorded } {
    const seen: Recorded = {
        posted: [],
        calls: [],
        problems: [],
        ledgerChanges: 0,
        shaderRequested: 0,
        sourceFound: 0,
        sourcesForgotten: 0,
        coverage: [],
        heat: [],
        uploads: 0,
        outstanding: [],
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
        releaseTexturesExcept: () => seen.calls.push("release"),
        forgetTranslatedEffects: () => seen.calls.push("forget effects"),
        setHeatStrength: (strength) => seen.heat.push(strength),
        uploadTexture: () => seen.uploads++,
    };

    const events: AssetLoaderEvents = {
        post: (message) => seen.posted.push(message),
        ledgersChanged: () => {
            seen.ledgerChanges++;
            seen.outstanding.push(loader.requested - loader.settled);
        },
        shaderRequested: () => seen.shaderRequested++,
        shaderSourceFound: () => seen.sourceFound++,
        shaderSourcesForgotten: () => seen.sourcesForgotten++,
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
                { type: "requestTexture", name: "hull.dds", request: 1 },
            ]);
            assert.equal(loader.requested, 1);
            assert.equal(loader.settled, 0);
        });

        // Two refreshes close together ask for the same texture twice, and the replies can land
        // in either order. Only the reply to the latest request is the texture: an older one is
        // older bytes, and it must not settle the wait of the scene that asked again.
        it("takes only the reply to the latest request for a name", () => {
            const { loader, seen } = harness();
            loader.requestTexture("hull.dds");
            loader.clearLedgers();
            loader.requestTexture("hull.dds");
            const [first, second] = seen.posted.map((p) => p.request);

            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([1]),
                version: "v1",
                request: first,
            });

            assert.notEqual(first, second);
            assert.equal(loader.settled, 0);
            assert.deepEqual(seen.calls, []);

            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([2]),
                version: "v2",
                request: second,
            });
            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([1]),
                version: "v1",
                request: first,
            });

            assert.equal(loader.settled, 1);
            assert.deepEqual(seen.calls, [
                "texture hull.dds",
                "particle texture hull.dds",
            ]);

            loader.clearLedgers();
            loader.requestTexture("hull.dds");
            assert.equal(seen.posted[2].have, "v2");
        });

        it("settles on the reply and hands the texture to meshes and particles alike", () => {
            const { loader, seen } = harness();
            loader.requestTexture("hull.dds");

            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([1, 2, 3]),
            });

            assert.equal(loader.settled, 1);
            assert.deepEqual(seen.calls, [
                "texture hull.dds",
                "particle texture hull.dds",
            ]);
            assert.deepEqual(seen.problems, []);
        });

        // The file's bytes reach the decoder as they came, with the format the server found:
        // nothing in between turns them into text and back.
        it("hands the decoder the bytes as they arrived and the format found", () => {
            const decoded: { bytes: Uint8Array; format: string }[] = [];
            const { loader } = harness({
                decode: (bytes, format) => {
                    decoded.push({ bytes, format });
                    return {} as THREE.Texture;
                },
            });
            const data = new Uint8Array([68, 68, 83, 32]);

            loader.receiveTexture("hull.tga", { format: "dds", data });

            assert.equal(decoded.length, 1);
            assert.equal(decoded[0].bytes, data);
            assert.equal(decoded[0].format, "dds");
        });

        // #420: a PNG is drawn, and the notes recommend DDS - information, never a warning: the game loads it.
        it("notes a PNG texture as information recommending DDS", () => {
            const { loader, seen } = harness({ decode: decodeTexture });

            loader.receiveTexture("Hull.png", {
                format: "png",
                data: TWO_BY_TWO_PNG,
            });

            assert.deepEqual(seen.calls, [
                "texture Hull.png",
                "particle texture Hull.png",
            ]);
            assert.deepEqual(seen.problems, [
                {
                    severity: "info",
                    message:
                        "Texture 'Hull.png' is a PNG file. The game loads it; DDS is recommended.",
                },
            ]);
        });

        // #420: decoded by the bytes, as the game does - and the mismatch said, since nobody means it.
        it("notes a texture whose content is not what its extension says", () => {
            const { loader, seen } = harness({ decode: decodeTexture });

            loader.receiveTexture("Hull.tga", {
                format: "tga",
                data: TWO_BY_TWO_PNG,
            });

            assert.deepEqual(seen.calls, [
                "texture Hull.tga",
                "particle texture Hull.tga",
            ]);
            assert.deepEqual(
                seen.problems.map((p) => p.severity),
                ["info", "info"],
            );
            assert.ok(
                seen.problems.some(
                    (p) =>
                        p.message ===
                        "Texture 'Hull.tga' is a PNG file under a .tga name. The game decodes it by its content.",
                ),
            );
        });

        it("says nothing of a texture whose content matches its extension", () => {
            const { loader, seen } = harness({ decode: decodeTexture });

            loader.receiveTexture("Hull.tga", {
                format: "tga",
                data: ONE_PIXEL_TGA,
            });

            assert.deepEqual(seen.problems, []);
        });

        // A texture that did not arrive carries the host's reason, and that is what is said.
        // A refresh asks with the version held, and "still yours" binds the texture kept - no
        // decode, no new texture object, nothing missing.
        it("asks again with the version it holds and rebinds the texture kept", () => {
            let decodes = 0;
            const { loader, seen } = harness({
                decode: () => {
                    decodes++;
                    return {} as THREE.Texture;
                },
            });
            loader.requestTexture("hull.dds");
            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([1]),
                version: "v1",
            });

            loader.clearLedgers();
            seen.calls.length = 0;
            loader.requestTexture("HULL.dds");
            loader.receiveTexture("HULL.dds", {
                version: "v1",
                unchanged: true,
            });

            assert.deepEqual(seen.posted, [
                { type: "requestTexture", name: "hull.dds", request: 1 },
                {
                    type: "requestTexture",
                    name: "HULL.dds",
                    have: "v1",
                    request: 2,
                },
            ]);
            assert.equal(decodes, 1);
            assert.deepEqual(seen.calls, [
                "texture HULL.dds",
                "particle texture HULL.dds",
            ]);
            assert.deepEqual(seen.problems, []);
            assert.equal(loader.settled, 1);
        });

        it("decodes a changed texture again and asks with its new version afterwards", () => {
            let decodes = 0;
            const { loader, seen } = harness({
                decode: () => {
                    decodes++;
                    return {} as THREE.Texture;
                },
            });
            loader.requestTexture("hull.dds");
            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([1]),
                version: "v1",
            });
            loader.clearLedgers();
            loader.requestTexture("hull.dds");
            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([2]),
                version: "v2",
            });
            loader.clearLedgers();
            loader.requestTexture("hull.dds");

            assert.equal(decodes, 2);
            assert.equal(seen.posted[2].have, "v2");
        });

        // A texture that was there and is gone is no longer held: the next scene asks for it fresh.
        it("forgets a texture that stopped resolving", () => {
            const { loader, seen } = harness();
            loader.requestTexture("hull.dds");
            loader.receiveTexture("hull.dds", {
                format: "dds",
                data: new Uint8Array([1]),
                version: "v1",
            });
            loader.clearLedgers();
            loader.requestTexture("hull.dds");
            loader.receiveTexture("hull.dds", { data: null });
            loader.clearLedgers();
            loader.requestTexture("hull.dds");

            assert.equal(seen.posted[2].have, undefined);
        });

        it("says the host's reason for a texture that did not arrive", () => {
            const { loader, seen } = harness();

            loader.receiveTexture("hull.tga", {
                data: null,
                error: "The data channel closed.",
            });

            assert.deepEqual(seen.problems, [
                { severity: "warning", message: "The data channel closed." },
            ]);
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

    // #418: the game sets no heat strength of its own, so the composite effect's declared default is
    // what bends the frame. The source here is a dummy, not the shipped effect.
    describe("the heat composite's effect", () => {
        const effect = {} as TranslatedEffect;
        const translating = () =>
            harness({
                translate: () => ({
                    effect,
                    refusal: null,
                    missingIncludes: [],
                }),
            });

        it("hands the viewport the strength the effect declares, and translates nothing", () => {
            const { loader, seen } = translating();
            loader.requestShader("SceneHeat.fx");

            loader.receiveShader(
                "SceneHeat.fx",
                "float Other = 2.0f;\nfloat DistortionAmount = 0.7f;\n",
            );

            assert.deepEqual(seen.heat, [0.7]);
            assert.deepEqual(seen.calls, []);
            assert.equal(loader.settled, 1);
        });

        it("hands the viewport no strength when the effect is not there or declares none", () => {
            const { loader, seen } = translating();

            loader.receiveShader("SceneHeat.fx", null);
            loader.receiveShader("sceneheat.fx", "float Other = 2.0f;");

            assert.deepEqual(seen.heat, [null, null]);
        });

        it("forgets the strength when the effects are fetched again", () => {
            const { loader, seen } = translating();
            loader.receiveShader(
                "SceneHeat.fx",
                "float DistortionAmount = 0.7;",
            );

            loader.refetchShaders(["SceneHeat.fx"]);

            assert.deepEqual(seen.heat, [0.7, null]);
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

    // An effect is started by name from many places - every hardpoint of a capital ship names the
    // one explosion - and the host answers every request it is sent. Asked for through here, a
    // name goes out once, and what the system needs is fetched before any emitter of it exists.
    describe("a particle system request", () => {
        it("posts a name once and counts it once, however it is cased", () => {
            const { loader, seen } = harness();

            loader.requestSystem("Large_Explosion_Space");
            loader.requestSystem("large_explosion_space");

            assert.deepEqual(seen.posted, [
                {
                    type: "requestParticleSystem",
                    name: "Large_Explosion_Space",
                },
            ]);
            assert.equal(loader.requested, 1);
            assert.equal(loader.settled, 0);
        });

        it("asks for the textures and effects a system names when it arrives", () => {
            const { loader, seen } = harness();
            loader.requestSystem("Large_Explosion_Space");

            loader.receiveSystem(
                "Large_Explosion_Space",
                testSystem([
                    emitter({ colorTexture: "P_Fire.tga" }),
                    emitter({
                        colorTexture: "p_smoke.tga",
                        properties: properties({ blendMode: "Transparent" }),
                    }),
                ]),
            );

            assert.deepEqual(seen.posted.slice(1), [
                { type: "requestTexture", name: "p_fire.tga", request: 1 },
                { type: "requestTexture", name: "p_smoke.tga", request: 2 },
                { type: "requestShader", name: "PrimAdditive.fx" },
                { type: "requestShader", name: "PrimAlpha.fx" },
            ]);
            // Nothing was put in the scene for it.
            assert.deepEqual(seen.calls, []);
            assert.equal(loader.requested, 5);
            assert.equal(loader.settled, 1);
        });

        // The cover lifts the moment nothing is outstanding. A system counted as answered before
        // its textures were asked for would lift it for one render, over an effect not yet dressed.
        it("never reads as finished between a system arriving and its textures being asked for", () => {
            const { loader, seen } = harness();
            loader.requestSystem("Large_Explosion_Space");
            seen.outstanding.length = 0;

            loader.receiveSystem(
                "Large_Explosion_Space",
                testSystem([emitter()]),
            );

            assert.ok(seen.outstanding.length > 0);
            assert.ok(
                seen.outstanding.every((count) => count > 0),
                `outstanding ${seen.outstanding.join(", ")}`,
            );
        });

        it("counts a system that could not be read as answered", () => {
            const { loader, seen } = harness();
            loader.requestSystem("Missing_Effect");

            loader.receiveSystem("Missing_Effect", null);

            assert.equal(seen.posted.length, 1);
            assert.equal(loader.requested, 1);
            assert.equal(loader.settled, 1);
        });

        it("asks again for a new subject", () => {
            const { loader, seen } = harness();
            loader.requestSystem("Large_Explosion_Space");

            loader.clearLedgers();
            loader.requestSystem("Large_Explosion_Space");

            assert.equal(seen.posted.length, 2);
            assert.equal(loader.requested, 1);
        });
    });

    // A texture nothing draws yet - an explosion's, fetched up front - was copied to the graphics
    // card on the frame that first drew with it, which is the frame the explosion starts on.
    describe("a texture that arrives", () => {
        it("is copied to the graphics card then, not on the first frame that draws with it", () => {
            const { loader, seen } = harness();
            loader.requestTexture("p_fire.tga");

            loader.receiveTexture("p_fire.tga", {
                format: "tga",
                data: new Uint8Array([1]),
                version: "v1",
            });

            assert.equal(seen.uploads, 1);
        });

        it("is not copied when it did not resolve", () => {
            const { loader, seen } = harness({ decode: () => null });
            loader.requestTexture("p_fire.tga");

            loader.receiveTexture("p_fire.tga", { format: "tga" });

            assert.equal(seen.uploads, 0);
        });
    });

    // #395: the shader folder changed and the server restarted on it. What came back from the old
    // folder - an effect, a header, or the word that neither was there - no longer stands.
    describe("a refetch of the effects", () => {
        const effect = {} as TranslatedEffect;

        // An effect fetched up front has no emitter in the scene to name its shaders again.
        it("asks again for the effects of every system that arrived, built or not", () => {
            const { loader, seen } = harness();
            loader.requestSystem("Large_Explosion_Space");
            loader.receiveSystem(
                "Large_Explosion_Space",
                testSystem([emitter()]),
            );
            seen.posted.length = 0;

            loader.refetchShaders([]);

            assert.deepEqual(seen.posted, [
                { type: "requestShader", name: "PrimAdditive.fx" },
            ]);
        });

        it("asks again for an effect that was not there and translates it when it is", () => {
            const { loader, seen } = harness({
                translate: () => ({
                    effect,
                    refusal: null,
                    missingIncludes: [],
                }),
            });
            loader.requestShader("PrimAlpha.fx");
            loader.receiveShader("PrimAlpha.fx", null);

            loader.refetchShaders(["PrimAlpha.fx"]);

            assert.deepEqual(seen.posted, [
                { type: "requestShader", name: "PrimAlpha.fx" },
                { type: "requestShader", name: "PrimAlpha.fx" },
            ]);
            assert.equal(loader.requested, 1);
            assert.equal(loader.settled, 0);

            loader.receiveShader("PrimAlpha.fx", "technique t {}");

            assert.deepEqual(seen.calls, [
                "forget effects",
                "translated primalpha.fx",
            ]);
            assert.equal(seen.sourceFound, 1);
            // The panel's "no sources are reachable" reading starts over, not from the old folder.
            assert.equal(seen.sourcesForgotten, 1);
        });

        it("drops the translations and the headers from the old folder", () => {
            let read: string | null = "unset";
            const { loader, seen } = harness({
                translate: (_source, readHeader) => {
                    read = readHeader("common.fxh");
                    return { effect, refusal: null, missingIncludes: [] };
                },
            });
            loader.receiveShader("common.fxh", "float4 old;");
            loader.receiveShader("MeshGloss.fx", "technique t {}");
            assert.equal(read, "float4 old;");

            loader.refetchShaders(["MeshGloss.fx"]);
            loader.receiveShader("MeshGloss.fx", "technique t {}");

            // Translated again, and against nothing from before: the header is asked for anew.
            assert.equal(read, null);
            assert.deepEqual(seen.calls, [
                "translated meshgloss.fx",
                "forget effects",
                "translated meshgloss.fx",
            ]);
        });
    });
});
