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

import type { ModelPreviewTexture } from "../../protocol/panels/modelPreview";
import {
    type TranslatedEffect,
    translateEffect,
    type TranslationResult,
} from "./fx/effect";
import { parseFxManifest, selectTechnique } from "./fx/fxParser";
import { type FxMaterialState, materialStateFrom } from "./fx/renderState";
import { collectUniformDefaults } from "./fx/semantics";
import { HEAT_COMPOSITE_EFFECT, HEAT_STRENGTH_PARAMETER } from "./heatPass";
import { AssetLedger } from "./loadProgress";
import { bufferOf } from "./payloadBytes";
import { addressingFor } from "./textures";

/**
 * A texture as the host delivers it: the file's bytes and the format found, the word that the copy
 * already held still stands, or why there is neither.
 */
export type TexturePayload = Pick<
    ModelPreviewTexture,
    "format" | "data" | "error" | "version" | "unchanged" | "request"
>;

/** The part of the viewport the loader drives. */
export interface AssetViewport {
    applyTranslatedEffect(shaderName: string, effect: TranslatedEffect): void;
    translatedCoverage(): { translated: number; total: number };
    shaderTextureProblems(): string[];
    applyShaderState(shaderName: string, state: FxMaterialState): void;
    setTexture(name: string, texture: THREE.Texture): void;
    setParticleTexture(name: string, texture: THREE.Texture): void;
    setParticleTextureMissing(name: string): void;
    /** Disposes every texture whose lower-cased name is not among `keep`. */
    releaseTexturesExcept(keep: ReadonlySet<string>): void;
    /** Drops every translated effect and declared render state; the meshes go back to their archetypes. */
    forgetTranslatedEffects(): void;
    /** How far heat bends the frame, as the heat effect declares it; null while none is in hand. */
    setHeatStrength(strength: number | null): void;
}

/** What the loader tells the panel. */
export interface AssetLoaderEvents {
    post(message: {
        type: "requestTexture" | "requestShader";
        name: string;
        /** For a texture: the version already held, when one is. */
        have?: string;
        /** For a texture: the number of this request, which the host echoes on its reply. */
        request?: number;
    }): void;
    /** A request was made or answered: the loading tally reads `requested` and `settled`. */
    ledgersChanged(): void;
    /** An effect was asked for, which a particle system on its own never does. */
    shaderRequested(): void;
    /** An `.fx` source was reachable at all. */
    shaderSourceFound(): void;
    /** Every source fetched was forgotten, for a refetch: none is reachable until one comes back. */
    shaderSourcesForgotten(): void;
    /** An effect was translated; how many sub-meshes now draw with their own shaders. */
    translated(coverage: { translated: number; total: number }): void;
    problem(severity: "warning" | "info", message: string): void;
}

export interface AssetLoaderOptions {
    translate?: (
        source: string,
        read: (name: string) => string | null,
    ) => TranslationResult;
    decode?: (bytes: Uint8Array, format: string) => THREE.Texture | null;
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

    /**
     * Every texture decoded and the version it came with, by lower-cased name.
     *
     * A scene refresh asks for each texture again with the version held here. For one whose file
     * did not change the host answers "still yours" and sends no bytes; the texture kept here is
     * bound to the scene's new materials, with no decode and no upload.
     */
    private readonly held = new Map<
        string,
        { version: string; texture: THREE.Texture }
    >();

    /**
     * The number of the latest request for each texture this scene asked for, by lower-cased name.
     *
     * Two refreshes close together ask for a texture twice and the replies land in either order.
     * Only the reply to the latest request is that texture; one to an earlier request is older
     * bytes, and belongs to a scene that is no longer on screen.
     */
    private readonly asked = new Map<string, number>();
    private sequence = 0;

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

        const have = this.held.get(name.toLowerCase())?.version;
        const request = ++this.sequence;
        this.asked.set(name.toLowerCase(), request);
        this.events.post(
            have === undefined
                ? { type: "requestTexture", name, request }
                : { type: "requestTexture", name, have, request },
        );
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
     * Lets go of every texture the scene on screen did not ask for. Called when the scene has
     * finished loading: what stays on the GPU is what is drawn, not everything this panel ever
     * showed. A texture asked for and still on its way counts as asked for.
     */
    releaseUnused(): void {
        const keep = this.textures.requestedNames;

        for (const name of [...this.held.keys()]) {
            if (!keep.has(name)) {
                this.held.delete(name);
            }
        }

        this.viewport()?.releaseTexturesExcept(keep);
    }

    /**
     * Forgets what was asked for, for a new subject. The fetched sources and the translations stay:
     * an effect is the same file whichever model asked for it.
     */
    clearLedgers(): void {
        this.textures.clear();
        this.shaders.clear();
        this.asked.clear();
    }

    /**
     * Forgets every effect and header fetched, and every answer that one was not there, then asks
     * for these effects again.
     *
     * For a changed shader or game folder (#395): what the old folder answered no longer stands,
     * and a translation made from it would go on drawing. Headers are asked for again as the
     * effects that include them come back.
     */
    refetchShaders(names: readonly string[]): void {
        this.sources.clear();
        this.translatedNames.clear();
        this.shaders.clear();
        this.events.shaderSourcesForgotten();
        this.events.ledgersChanged();

        const viewport = this.viewport();
        if (viewport !== null) {
            viewport.setHeatStrength(null);
            viewport.forgetTranslatedEffects();
            this.events.translated(viewport.translatedCoverage());
        }

        for (const name of names) {
            this.requestShader(name);
        }
    }

    /** An effect or header has come back, or come back empty. */
    receiveShader(name: string, source: string | null): void {
        this.sources.set(name.toLowerCase(), source);

        // Settled before anything else: an effect that is simply not there is still an answer,
        // and the wait is over.
        this.shaders.settle(name);
        this.events.ledgersChanged();

        // The heat composite is drawn by the preview's own pass, never translated onto a mesh: all
        // it gives is the strength it declares, which the game draws with.
        if (name.toLowerCase() === HEAT_COMPOSITE_EFFECT.toLowerCase()) {
            this.translatedNames.add(name.toLowerCase());
            this.viewport()?.setHeatStrength(
                source === null
                    ? null
                    : (collectUniformDefaults(source).get(
                          HEAT_STRENGTH_PARAMETER,
                      )?.[0] ?? null),
            );
            if (source !== null) {
                this.events.shaderSourceFound();
            }
            return;
        }

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
    receiveTexture(name: string, result: TexturePayload): void {
        // A reply to a request that a later one overtook, or that a new scene no longer waits on,
        // is nothing to show and nothing to count. One that names no request is taken as it comes.
        if (
            result.request !== undefined &&
            this.asked.get(name.toLowerCase()) !== result.request
        ) {
            return;
        }

        // Before the decode, and regardless of how it goes: a texture that did not resolve is an
        // ANSWER. The problems bar is what tells the reader it is missing; the cover only needs to
        // know it is no longer waiting.
        this.textures.settle(name);
        this.events.ledgersChanged();

        const viewport = this.viewport();
        if (viewport === null) {
            return;
        }

        // The copy already held still stands: it is handed to the viewport again, because a refresh
        // rebuilt the materials it was bound to.
        const kept =
            result.unchanged === true
                ? this.held.get(name.toLowerCase())
                : undefined;
        if (kept !== undefined) {
            viewport.setTexture(name, kept.texture);
            viewport.setParticleTexture(name, kept.texture);
            return;
        }

        const bytes = result.data ?? null;
        const texture =
            bytes === null ? null : this.decode(bytes, result.format ?? "");

        if (texture !== null) {
            const version = result.version ?? null;
            if (version === null) {
                this.held.delete(name.toLowerCase());
            } else {
                this.held.set(name.toLowerCase(), { version, texture });
            }

            // Offered to both: a name resolves to one texture whichever kind of thing samples it,
            // and the loader does not track which asked.
            viewport.setTexture(name, texture);
            viewport.setParticleTexture(name, texture);
            return;
        }

        this.held.delete(name.toLowerCase());

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
 * Samples an unpacked texture as the game's samplers do: LINEAR, through a mip chain.
 *
 * A `DataTexture` arrives NEAREST with its top level alone, which drew a TGA blocky up close and
 * shimmering at a distance. The chain is the file's own where it ships one - `mipmaps` holds every
 * level - and generated from the top level where it ships none.
 */
function applyFiltering(texture: THREE.DataTexture): THREE.DataTexture {
    texture.minFilter = THREE.LinearMipmapLinearFilter;
    texture.magFilter = THREE.LinearFilter;
    texture.generateMipmaps = texture.mipmaps.length === 0;

    return texture;
}

/**
 * Turns a texture file's bytes into a three.js texture.
 *
 * The ONE place a texture's bytes become a texture: everything before it - the server, the pipe,
 * the host - moves bytes and decides nothing about them. Today `format`, the extension the server
 * found, picks the decoder; choosing by what the bytes ARE instead changes this function and
 * nothing around it.
 *
 * DDS keeps its compressed blocks all the way to the GPU - decoding it would inflate a 2048x2048
 * texture several times over for no gain - while TGA has to be unpacked, because no GPU samples it
 * directly. Either way it is sampled LINEAR through a mip chain, which is what the game's samplers
 * ask for.
 */
export function decodeTexture(
    bytes: Uint8Array,
    format: string,
): THREE.Texture | null {
    const buffer = bufferOf(bytes);

    if (format === "dds") {
        const parsed = new DDSLoader().parse(buffer, true);

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

        const levels = parsed.mipmaps as {
            data: Uint8Array;
            width: number;
            height: number;
        }[];
        const level = levels[0];
        if (level === undefined) {
            return null;
        }

        const texture = new THREE.DataTexture(
            level.data,
            parsed.width,
            parsed.height,
        );

        // The levels the file ships, kept; a file that ships one has its chain generated.
        if (levels.length > 1) {
            texture.mipmaps = levels;
        }

        applyFiltering(texture);
        texture.needsUpdate = true;
        return applyAddressing(texture, parsed.width, parsed.height);
    }

    if (format === "tga") {
        const parsed = new TGALoader().parse(buffer);

        const texture = new THREE.DataTexture(
            parsed.data,
            parsed.width,
            parsed.height,
        );
        applyFiltering(texture);
        texture.needsUpdate = true;
        // TGA rows run bottom-up, which is also how the game stores them.
        texture.flipY = true;
        return applyAddressing(texture, parsed.width, parsed.height);
    }

    return null;
}
