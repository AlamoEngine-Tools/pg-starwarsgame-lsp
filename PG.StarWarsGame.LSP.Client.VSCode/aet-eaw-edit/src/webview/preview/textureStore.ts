// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every decoded texture the viewport holds, by name, and the one place a texture is let go (#436).
//
// The materials and the particle systems both drew from maps of their own, of the same objects, and
// neither ever removed one: a texture stayed on the GPU for as long as the panel was open, and one
// sent again took its predecessor's place in the map without the predecessor being disposed. Both
// layers read this store now. A texture that is replaced is disposed, and when a scene has finished
// loading the textures it did not ask for are disposed too - what is kept is what is on screen.

import type * as THREE from "three";

export class TextureStore {
    /**
     * Texture by lower-cased name. Handed to a particle system as the map it looks its sprites up
     * in, so it is the same map, not a copy: a system built later sees what arrived before it.
     */
    readonly byName = new Map<string, THREE.Texture>();

    /** The spelling each texture was first named by, which is what a material is bound by. */
    private readonly spelling = new Map<string, string>();

    /** How many textures are held. */
    get size(): number {
        return this.byName.size;
    }

    /**
     * Takes a texture under a name. One already held under that name is disposed, unless it is the
     * same object handed over again - which is what a refresh does with a texture that did not
     * change.
     */
    set(name: string, texture: THREE.Texture): void {
        const key = name.toLowerCase();
        const replaced = this.byName.get(key);

        this.byName.set(key, texture);
        if (!this.spelling.has(key)) {
            this.spelling.set(key, name);
        }

        if (replaced !== undefined && replaced !== texture) {
            replaced.dispose();
        }
    }

    /** The texture held under a name, whatever its case. */
    get(name: string): THREE.Texture | undefined {
        return this.byName.get(name.toLowerCase());
    }

    /** Every texture with the name it was first given. */
    entries(): [string, THREE.Texture][] {
        return [...this.byName].map(([key, texture]) => [
            this.spelling.get(key) ?? key,
            texture,
        ]);
    }

    /**
     * Disposes every texture whose name is not among `keep`, and forgets it.
     *
     * @param keep Lower-cased names of the textures the scene on screen asked for.
     * @returns How many were let go.
     */
    retain(keep: ReadonlySet<string>): number {
        let released = 0;

        for (const [key, texture] of [...this.byName]) {
            if (keep.has(key)) {
                continue;
            }

            texture.dispose();
            this.byName.delete(key);
            this.spelling.delete(key);
            released++;
        }

        return released;
    }

    /** Disposes everything: the viewport is going away. */
    clear(): void {
        this.retain(new Set());
    }
}
