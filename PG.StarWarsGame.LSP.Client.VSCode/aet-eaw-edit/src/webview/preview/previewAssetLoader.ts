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

import type { AlamoParticleContent } from "../../protocol/modelPreview";
import type { ModelPreviewTexture } from "../../protocol/panels/modelPreview";
import {
    type TranslatedEffect,
    translateEffect,
    type TranslationResult,
} from "./fx/effect";
import { systemEffectNames, systemTextureNames } from "./effectNeeds";
import { parseFxManifest, selectTechnique } from "./fx/fxParser";
import { type FxMaterialState, materialStateFrom } from "./fx/renderState";
import { collectUniformDefaults } from "./fx/semantics";
import { HEAT_COMPOSITE_EFFECT, HEAT_STRENGTH_PARAMETER } from "./heatPass";
import { AssetLedger } from "./loadProgress";
import { bufferOf } from "./payloadBytes";
import { decodePng, type TextureContent, textureContentOf } from "./pngDecode";
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
    /** Copies a texture to the graphics card now rather than on the first frame that draws with it. */
    uploadTexture(texture: THREE.Texture): void;
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
        type: "requestTexture" | "requestShader" | "requestParticleSystem";
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
     * The particle systems asked for by name, counted with the rest: a system is what names its
     * textures and effects, so the cover has to wait for it before it can know what else it waits on.
     */
    private readonly systems = new AssetLedger();

    /**
     * The effects of every system that arrived, whether or not an emitter of it was ever built.
     *
     * A refetch asks again for what the scene graph names, and a system fetched up front - an
     * explosion nothing has set off yet - is not in the scene graph.
     */
    private readonly systemEffects = new Set<string>();

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

    /** Assets asked for: textures, effects and particle systems together. */
    get requested(): number {
        return (
            this.textures.requested +
            this.shaders.requested +
            this.systems.requested
        );
    }

    /** Assets answered, found or not. */
    get settled(): number {
        return (
            this.textures.settled + this.shaders.settled + this.systems.settled
        );
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
     * Asks for one particle system by name, once.
     *
     * Every hardpoint of a capital ship names the one explosion, and the host answers every request
     * it is sent: MEASURED, 145 requests for one system took ten seconds to come back.
     */
    requestSystem(name: string): void {
        if (!this.systems.request(name)) {
            return;
        }

        this.events.post({ type: "requestParticleSystem", name });
        this.events.ledgersChanged();
    }

    /**
     * A particle system has come back, or come back empty.
     *
     * What it names is asked for here, off the system's own data: its textures and the effects it
     * is drawn with are on their way before any emitter of it is built, which for an explosion is
     * not until something blows up. Asked for BEFORE the system counts as answered, so the wait
     * never reads as over in between.
     */
    receiveSystem(name: string, system: AlamoParticleContent | null): void {
        if (system !== null) {
            for (const texture of systemTextureNames(system)) {
                this.requestTexture(texture);
            }

            for (const effect of systemEffectNames(system)) {
                this.systemEffects.add(effect);
                this.requestShader(effect);
            }
        }

        this.systems.settle(name);
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
        this.systems.clear();
        this.systemEffects.clear();
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

        for (const name of this.systemEffects) {
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
            viewport.uploadTexture(kept.texture);
            this.noteContent(name, kept.texture, result.format ?? null);
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

            // After the two above, which decide how it is sampled: copied to the graphics card
            // while the cover is still down, not on the frame that first draws with it. For an
            // effect fetched up front that frame is the one the explosion starts on.
            viewport.uploadTexture(texture);
            this.noteContent(name, texture, result.format ?? null);
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
     * What the decoded file was, said when it is worth saying (#420): a PNG is information recommending
     * DDS - the game loads it, so never a warning - and a file whose content is not what its extension
     * says is information too, since the game decodes it by the content and nobody means the mismatch.
     */
    private noteContent(
        name: string,
        texture: THREE.Texture,
        format: string | null,
    ): void {
        const content = contentOfTexture(texture);
        if (content === null) {
            return;
        }

        if (content === "png") {
            this.events.problem(
                "info",
                l10n.t(
                    "Texture '{0}' is a PNG file. The game loads it; DDS is recommended.",
                    name,
                ),
            );
        }

        if (
            format !== null &&
            format !== "" &&
            format.toLowerCase() !== content
        ) {
            this.events.problem(
                "info",
                l10n.t(
                    "Texture '{0}' is a {1} file under a .{2} name. The game decodes it by its content.",
                    name,
                    content.toUpperCase(),
                    format.toLowerCase(),
                ),
            );
        }
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
 * the host - moves bytes and decides nothing about them. What the bytes ARE picks the decoder, as in
 * the game (#420): it hands the file to its loader with no format stated, so a PNG under a `.tga` name
 * loads as a PNG. `format`, the extension the file was found under, decides nothing here; the loader
 * compares it with the content the texture records, to say when the two disagree.
 *
 * DDS keeps its compressed blocks all the way to the GPU - decoding it would inflate a 2048x2048
 * texture several times over for no gain - while TGA and PNG have to be unpacked, because no GPU
 * samples them directly. A PNG is unpacked to the same RGBA a TGA is, top row first and flipped the
 * same way, so the two formats of one picture draw alike. Every format is sampled LINEAR through a mip
 * chain, which is what the game's samplers ask for. Bytes no decoder can read are no texture, never a
 * throw: the caller says the texture is missing.
 */
export function decodeTexture(
    bytes: Uint8Array,
    _format: string,
): THREE.Texture | null {
    const content = textureContentOf(bytes);
    try {
        const texture = decodeContent(bytes, content);
        if (texture !== null) {
            texture.userData.aetTextureContent = content;
        }
        return texture;
    } catch {
        return null;
    }
}

/** The content `decodeTexture` found in the bytes it decoded; null for a texture it did not make. */
export function contentOfTexture(
    texture: THREE.Texture,
): TextureContent | null {
    const content = (
        texture.userData as { aetTextureContent?: unknown } | undefined
    )?.aetTextureContent;
    return content === "dds" || content === "png" || content === "tga"
        ? content
        : null;
}

/** An unpacked RGBA image as a texture: top row first, flipped as the game's TGAs are, sampled linear. */
function unpacked(
    data: Uint8Array,
    width: number,
    height: number,
): THREE.Texture {
    const texture = new THREE.DataTexture(data, width, height);
    applyFiltering(texture);
    texture.needsUpdate = true;
    // TGA rows run bottom-up, which is also how the game stores them. A PNG is flipped the same, so one
    // picture draws alike in either format.
    texture.flipY = true;
    return applyAddressing(texture, width, height);
}

function decodeContent(
    bytes: Uint8Array,
    content: TextureContent,
): THREE.Texture | null {
    const buffer = bufferOf(bytes);

    if (content === "dds") {
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

    if (content === "png") {
        const image = decodePng(bytes);
        return image === null
            ? null
            : unpacked(image.data, image.width, image.height);
    }

    const parsed = new TGALoader().parse(buffer);
    return parsed.data instanceof Uint8Array &&
        parsed.width !== undefined &&
        parsed.height !== undefined
        ? unpacked(parsed.data, parsed.width, parsed.height)
        : null;
}
