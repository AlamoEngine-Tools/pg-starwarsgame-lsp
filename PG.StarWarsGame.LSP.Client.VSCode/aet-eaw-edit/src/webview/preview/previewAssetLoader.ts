// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the preview asks the host for so the geometry can be DRAWN - textures and effects - and what
// it does with each answer.
//
// Not React. The pipeline is refs and ledgers mutated from the message handler as replies land in
// whatever order the server answers, and re-rendering on each arrival buys nothing; the panel hears
// about the few things it shows (the loading tally, the translated count, the problems) through
// `AssetLoaderEvents`. Out of the component so it can be tested on its own, against a viewport that
// only has to answer the handful of calls below.

import * as l10n from "@vscode/l10n";
import * as THREE from "three";
import { DDSLoader } from "three/examples/jsm/loaders/DDSLoader.js";
import { TGALoader } from "three/examples/jsm/loaders/TGALoader.js";

import type { GetModelTextureResult } from "../../protocol/modelPreview";
import {
    type TranslatedEffect,
    translateEffect,
    type TranslationResult,
} from "./fx/effect";
import { parseFxManifest, selectTechnique } from "./fx/fxParser";
import { type FxMaterialState, materialStateFrom } from "./fx/renderState";
import { AssetLedger } from "./loadProgress";
import { addressingFor } from "./textures";

/** The part of the viewport the loader drives. */
export interface AssetViewport {
    applyTranslatedEffect(shaderName: string, effect: TranslatedEffect): void;
    translatedCoverage(): { translated: number; total: number };
    shaderTextureProblems(): string[];
    applyShaderState(shaderName: string, state: FxMaterialState): void;
    setTexture(name: string, texture: THREE.Texture): void;
    setParticleTexture(name: string, texture: THREE.Texture): void;
    setParticleTextureMissing(name: string): void;
}

/** What the loader tells the panel. */
export interface AssetLoaderEvents {
    post(message: {
        type: "requestTexture" | "requestShader";
        name: string;
    }): void;
    /** A request was made or answered: the loading tally reads `requested` and `settled`. */
    ledgersChanged(): void;
    /** An effect was asked for, which a particle system on its own never does. */
    shaderRequested(): void;
    /** An `.fx` source was reachable at all. */
    shaderSourceFound(): void;
    /** An effect was translated; how many sub-meshes now draw with their own shaders. */
    translated(coverage: { translated: number; total: number }): void;
    problem(severity: "warning" | "info", message: string): void;
}

export interface AssetLoaderOptions {
    translate?: (
        source: string,
        read: (name: string) => string | null,
    ) => TranslationResult;
    decode?: (result: GetModelTextureResult) => THREE.Texture | null;
}

export class PreviewAssetLoader {
    /**
     * What has been asked for and what has come back, per kind.
     *
     * They dedupe - a ten-part unit must not request its shared hull texture ten times - and they
     * are also what the loading cover counts. Two ledgers rather than one so a texture and an
     * effect that happen to share a name cannot settle each other.
     */
    private readonly textures = new AssetLedger();
    private readonly shaders = new AssetLedger();

    /**
     * Every `.fx` and `.fxh` fetched, by lower-cased file name. Null means "asked for, not there".
     *
     * Headers arrive over the same channel as the effects that include them and in no fixed order,
     * so translation is retried against this whole map each time anything new lands.
     */
    private readonly sources = new Map<string, string | null>();

    /** Effects already handed to the viewport, so a retry does not rebuild materials for nothing. */
    private readonly translatedNames = new Set<string>();

    private readonly translate: NonNullable<AssetLoaderOptions["translate"]>;
    private readonly decode: NonNullable<AssetLoaderOptions["decode"]>;

    constructor(
        private readonly viewport: () => AssetViewport | null,
        private readonly events: AssetLoaderEvents,
        options: AssetLoaderOptions = {},
    ) {
        this.translate = options.translate ?? translateEffect;
        this.decode = options.decode ?? decodeTexture;
    }

    /** Assets asked for, textures and effects together. */
    get requested(): number {
        return this.textures.requested + this.shaders.requested;
    }

    /** Assets answered, found or not. */
    get settled(): number {
        return this.textures.settled + this.shaders.settled;
    }

    /**
     * Asks for one texture, once.
     *
     * The gate is the ledger's rather than a bare Set because the count and the deduplication have
     * to agree: the host answers a repeat with nothing, so a name posted twice and counted twice
     * would leave the cover waiting on a reply that is never coming.
     */
    requestTexture(name: string): void {
        if (!this.textures.request(name)) {
            return;
        }

        this.events.post({ type: "requestTexture", name });
        this.events.ledgersChanged();
    }

    /** The same, for an effect or one of its headers. */
    requestShader(name: string): void {
        if (!this.shaders.request(name)) {
            return;
        }

        this.events.post({ type: "requestShader", name });
        this.events.shaderRequested();
        this.events.ledgersChanged();
    }

    /**
     * Forgets what was asked for, for a new subject. The fetched sources and the translations stay:
     * an effect is the same file whichever model asked for it.
     */
    clearLedgers(): void {
        this.textures.clear();
        this.shaders.clear();
    }

    /** An effect or header has come back, or come back empty. */
    receiveShader(name: string, source: string | null): void {
        this.sources.set(name.toLowerCase(), source);

        // Settled before anything else: an effect that is simply not there is still an answer,
        // and the wait is over.
        this.shaders.settle(name);
        this.events.ledgersChanged();

        if (source === null) {
            // No copy of this effect is reachable. The archetype material stands, which is the same
            // thing that happens for any mod-authored shader.
            return;
        }

        this.events.shaderSourceFound();

        const pass = selectTechnique(parseFxManifest(source))?.passes[0];

        if (pass !== undefined) {
            this.viewport()?.applyShaderState(name, materialStateFrom(pass));
        }

        // Eleven of the shipped effects are render state and nothing else - `MeshAlpha.fx` among
        // them, whose two programmable techniques are commented out in the file. That is a true
        // statement about the DATA that the reader can do nothing with, so it is not reported; the
        // translated count in the dock already says how much of the model draws with its own
        // shaders.

        // A header may have arrived after the effect that wanted it, so every effect still waiting
        // is retried whenever anything new lands.
        this.translatePending();
    }

    /** A texture has come back, or come back empty. */
    receiveTexture(name: string, result: GetModelTextureResult): void {
        // Before the decode, and regardless of how it goes: a texture that did not resolve is an
        // ANSWER. The problems bar is what tells the reader it is missing; the cover only needs to
        // know it is no longer waiting.
        this.textures.settle(name);
        this.events.ledgersChanged();

        const viewport = this.viewport();
        if (viewport === null) {
            return;
        }

        const texture =
            (result.data ?? null) === null ? null : this.decode(result);

        if (texture !== null) {
            // Offered to both: a name resolves to one texture whichever kind of thing samples it,
            // and the loader does not track which asked.
            viewport.setTexture(name, texture);
            viewport.setParticleTexture(name, texture);
            return;
        }

        // The reply IS the answer - the server sends no data when the name did not resolve.
        // Dropping it silently left every emitter naming that texture drawing a plain tinted quad,
        // which is exactly what a soft coloured puff is supposed to look like. A decode that fails
        // counts the same: either way the reader cannot see the texture, and needs to know that
        // rather than guess.
        viewport.setParticleTextureMissing(name);

        // And said in words, not only drawn. The marker tells you SOMETHING is missing at the place
        // it is missing from; the problems bar is what names the file. The server-side
        // ModelTextureExistence diagnostic covers the same ground without opening the preview, but
        // only for a texture a model names statically - this also catches one the server resolved
        // and the client could not decode.
        this.events.problem(
            "warning",
            result.error ??
                l10n.t(
                    "Texture '{0}' was not found. Anything that draws with it shows the AET logo instead.",
                    name,
                ),
        );
    }

    /**
     * Translates every effect whose headers have all arrived, and hands the result to the viewport.
     *
     * Retried on each arrival rather than tracked as a dependency graph: there are a handful of
     * headers, they are shared, and re-running a pure translation is cheaper than the bookkeeping.
     */
    translatePending(): void {
        const viewport = this.viewport();
        if (viewport === null) {
            return;
        }

        const read = (name: string): string | null =>
            this.sources.get(name.toLowerCase()) ?? null;

        for (const [name, source] of this.sources) {
            if (
                source === null ||
                !name.endsWith(".fx") ||
                this.translatedNames.has(name)
            ) {
                continue;
            }

            const result = this.translate(source, read);

            // Ask for the headers this one needs. Each is requested once; an absent one comes back
            // as null and stops the retries rather than looping.
            for (const missing of result.missingIncludes) {
                this.requestShader(missing);
            }

            if (result.effect !== null) {
                this.translatedNames.add(name);
                viewport.applyTranslatedEffect(name, result.effect);
                this.events.translated(viewport.translatedCoverage());

                // A sampler reading nothing draws black, which looks like a translation bug and is
                // usually a texture that did not resolve. Say so rather than leave it on screen.
                for (const problem of viewport.shaderTextureProblems()) {
                    this.events.problem("info", problem);
                }
            } else if (result.missingIncludes.length === 0) {
                // Nothing more is coming for this one; the archetype is what it draws with.
                this.translatedNames.add(name);
            }
        }
    }
}

const S3TC_FORMATS = new Set<number>([
    THREE.RGB_S3TC_DXT1_Format,
    THREE.RGBA_S3TC_DXT1_Format,
    THREE.RGBA_S3TC_DXT3_Format,
    THREE.RGBA_S3TC_DXT5_Format,
]);

/**
 * Applies the addressing mode the game's samplers ask for.
 *
 * The rule itself lives in `textures.ts` so it can be unit tested; this only carries the answer
 * onto the texture.
 */
function applyAddressing(
    texture: THREE.Texture,
    width: number | undefined,
    height: number | undefined,
): THREE.Texture {
    if (addressingFor(width, height) === "repeat") {
        texture.wrapS = THREE.RepeatWrapping;
        texture.wrapT = THREE.RepeatWrapping;
    }

    return texture;
}

/**
 * Turns raw texture bytes into a three.js texture.
 *
 * The server sends the file undecoded, so the format picks the decoder. DDS keeps its compressed
 * blocks all the way to the GPU - decoding it would inflate a 2048x2048 texture several times over
 * for no gain - while TGA has to be unpacked, because no GPU samples it directly.
 */
export function decodeTexture(
    result: GetModelTextureResult,
): THREE.Texture | null {
    const bytes = Uint8Array.from(atob(result.data ?? ""), (c) =>
        c.charCodeAt(0),
    );

    if (result.format === "dds") {
        const parsed = new DDSLoader().parse(bytes.buffer as ArrayBuffer, true);

        // Not every .dds is compressed - the loader hands back a plain pixel format for the
        // uncompressed ones, and a CompressedTexture cannot carry those.
        if (S3TC_FORMATS.has(parsed.format as number)) {
            const texture = new THREE.CompressedTexture(
                parsed.mipmaps as THREE.CompressedTextureMipmap[],
                parsed.width,
                parsed.height,
                parsed.format as THREE.CompressedPixelFormat,
            );
            texture.needsUpdate = true;
            return applyAddressing(texture, parsed.width, parsed.height);
        }

        const level = parsed.mipmaps[0] as { data: Uint8Array } | undefined;
        if (level === undefined) {
            return null;
        }

        const texture = new THREE.DataTexture(
            level.data,
            parsed.width,
            parsed.height,
        );
        texture.needsUpdate = true;
        return applyAddressing(texture, parsed.width, parsed.height);
    }

    if (result.format === "tga") {
        const parsed = new TGALoader().parse(bytes.buffer as ArrayBuffer);

        const texture = new THREE.DataTexture(
            parsed.data,
            parsed.width,
            parsed.height,
        );
        texture.needsUpdate = true;
        // TGA rows run bottom-up, which is also how the game stores them.
        texture.flipY = true;
        return applyAddressing(texture, parsed.width, parsed.height);
    }

    return null;
}
