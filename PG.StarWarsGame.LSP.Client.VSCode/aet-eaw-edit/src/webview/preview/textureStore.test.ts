// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import type { TranslatedEffect } from "./fx/effect";
import {
    type AssetLoaderEvents,
    type AssetViewport,
    PreviewAssetLoader,
} from "./previewAssetLoader";
import { TextureStore } from "./textureStore";

/** A texture that counts how often it was disposed. */
function tracked(): { texture: THREE.Texture; disposed: () => number } {
    const texture = new THREE.Texture();
    let count = 0;
    texture.addEventListener("dispose", () => count++);
    return { texture, disposed: () => count };
}

describe("TextureStore", () => {
    it("holds a texture by name, whatever the case", () => {
        const store = new TextureStore();
        const { texture } = tracked();

        store.set("Hull.DDS", texture);

        assert.equal(store.get("hull.dds"), texture);
        assert.equal(store.byName.get("hull.dds"), texture);
        assert.deepEqual(store.entries(), [["Hull.DDS", texture]]);
        assert.equal(store.size, 1);
    });

    // A texture sent again used to take the old one's place in the map and leave it on the GPU.
    it("disposes a texture it replaces, once", () => {
        const store = new TextureStore();
        const old = tracked();
        const next = tracked();
        store.set("hull.dds", old.texture);

        store.set("HULL.dds", next.texture);

        assert.equal(old.disposed(), 1);
        assert.equal(next.disposed(), 0);
        assert.equal(store.get("hull.dds"), next.texture);
        assert.equal(store.size, 1);
    });

    // A refresh hands an unchanged texture over again to rebind it; that is not a replacement.
    it("keeps a texture handed over again", () => {
        const store = new TextureStore();
        const hull = tracked();
        store.set("hull.dds", hull.texture);

        store.set("hull.dds", hull.texture);

        assert.equal(hull.disposed(), 0);
    });

    it("disposes and forgets what the scene did not ask for", () => {
        const store = new TextureStore();
        const hull = tracked();
        const turret = tracked();
        store.set("Hull.dds", hull.texture);
        store.set("Turret.dds", turret.texture);

        const released = store.retain(new Set(["hull.dds"]));

        assert.equal(released, 1);
        assert.equal(turret.disposed(), 1);
        assert.equal(hull.disposed(), 0);
        assert.equal(store.get("turret.dds"), undefined);
        assert.equal(store.size, 1);
    });

    it("disposes everything when the viewport goes away", () => {
        const store = new TextureStore();
        const hull = tracked();
        const turret = tracked();
        store.set("hull.dds", hull.texture);
        store.set("turret.dds", turret.texture);

        store.clear();

        assert.equal(hull.disposed(), 1);
        assert.equal(turret.disposed(), 1);
        assert.equal(store.size, 0);
    });
});

/**
 * The loader over a viewport whose textures live in a real store, as in the panel: every texture it
 * decodes is tracked, so a test can count the ones still alive.
 */
function loaderOverAStore(): {
    loader: PreviewAssetLoader;
    store: TextureStore;
    alive: () => number;
    decoded: () => number;
} {
    const store = new TextureStore();
    const made: { disposed: () => number }[] = [];

    const viewport: AssetViewport = {
        applyTranslatedEffect: () => undefined,
        translatedCoverage: () => ({ translated: 0, total: 0 }),
        shaderTextureProblems: () => [],
        applyShaderState: () => undefined,
        setTexture: (name, texture) => store.set(name, texture),
        setParticleTexture: () => undefined,
        setParticleTextureMissing: () => undefined,
        releaseTexturesExcept: (keep) => {
            store.retain(keep);
        },
    };
    const events: AssetLoaderEvents = {
        post: () => undefined,
        ledgersChanged: () => undefined,
        shaderRequested: () => undefined,
        shaderSourceFound: () => undefined,
        translated: () => undefined,
        problem: () => undefined,
    };
    const loader = new PreviewAssetLoader(() => viewport, events, {
        translate: () => ({
            effect: null as TranslatedEffect | null,
            refusal: null,
            missingIncludes: [],
        }),
        decode: () => {
            const one = tracked();
            made.push(one);
            return one.texture;
        },
    });

    return {
        loader,
        store,
        alive: () => made.filter((one) => one.disposed() === 0).length,
        decoded: () => made.length,
    };
}

/** One scene: its textures asked for, answered, and the load finished. */
function showScene(
    loader: PreviewAssetLoader,
    textures: { name: string; version: string }[],
    held: Map<string, string>,
): void {
    loader.clearLedgers();
    for (const { name, version } of textures) {
        loader.requestTexture(name);
        if (held.get(name) === version) {
            loader.receiveTexture(name, { version, unchanged: true });
        } else {
            loader.receiveTexture(name, {
                format: "dds",
                data: new Uint8Array([1]),
                version,
            });
            held.set(name, version);
        }
    }
    loader.releaseUnused();
    for (const name of [...held.keys()]) {
        if (!textures.some((t) => t.name === name)) {
            held.delete(name);
        }
    }
}

describe("the textures a preview keeps", () => {
    // The acceptance test of #436: the viewport's textures used to pile up for the panel's life.
    it("are the current scene's alone after ten scene changes", () => {
        const { loader, store, alive, decoded } = loaderOverAStore();
        const held = new Map<string, string>();

        for (let scene = 1; scene <= 10; scene++) {
            showScene(
                loader,
                [
                    // Shared by every scene and never changed: decoded once, kept throughout.
                    { name: "shared.dds", version: "v1" },
                    // Each scene's own two.
                    { name: `hull_${scene}.dds`, version: "v1" },
                    { name: `turret_${scene}.dds`, version: "v1" },
                ],
                held,
            );
        }

        assert.equal(decoded(), 21);
        assert.equal(alive(), 3);
        assert.equal(store.size, 3);
        assert.deepEqual([...store.byName.keys()].sort(), [
            "hull_10.dds",
            "shared.dds",
            "turret_10.dds",
        ]);
    });

    it("lose the old copy when a texture's file changed", () => {
        const { loader, alive, decoded } = loaderOverAStore();
        const held = new Map<string, string>();

        showScene(loader, [{ name: "hull.dds", version: "v1" }], held);
        showScene(loader, [{ name: "hull.dds", version: "v2" }], held);

        assert.equal(decoded(), 2);
        assert.equal(alive(), 1);
    });

    // A texture let go is asked for without a version the next time a scene wants it.
    it("are asked for afresh once they were let go", () => {
        const posted: { name: string; have?: string }[] = [];
        const store = new TextureStore();
        const viewport: AssetViewport = {
            applyTranslatedEffect: () => undefined,
            translatedCoverage: () => ({ translated: 0, total: 0 }),
            shaderTextureProblems: () => [],
            applyShaderState: () => undefined,
            setTexture: (name, texture) => store.set(name, texture),
            setParticleTexture: () => undefined,
            setParticleTextureMissing: () => undefined,
            releaseTexturesExcept: (keep) => {
                store.retain(keep);
            },
        };
        const loader = new PreviewAssetLoader(
            () => viewport,
            {
                post: (message) => posted.push(message),
                ledgersChanged: () => undefined,
                shaderRequested: () => undefined,
                shaderSourceFound: () => undefined,
                translated: () => undefined,
                problem: () => undefined,
            },
            { decode: () => new THREE.Texture() },
        );
        const held = new Map<string, string>();

        showScene(loader, [{ name: "hull.dds", version: "v1" }], held);
        showScene(loader, [{ name: "other.dds", version: "v1" }], held);
        loader.clearLedgers();
        loader.requestTexture("hull.dds");

        assert.equal(posted.at(-1)?.name, "hull.dds");
        assert.equal(posted.at(-1)?.have, undefined);
    });
});
