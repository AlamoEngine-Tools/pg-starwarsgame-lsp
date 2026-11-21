// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { decodeTexture } from "./previewAssetLoader";

/** A 1x1 uncompressed 24-bit TGA: an 18-byte header and one pixel, stored blue-green-red. */
function onePixelTga(red: number, green: number, blue: number): Uint8Array {
    return new Uint8Array([
        0,
        0,
        2,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        1,
        0,
        1,
        0,
        24,
        0,
        blue,
        green,
        red,
    ]);
}

/**
 * An uncompressed 32-bit DDS of `size` x `size` carrying `levels` mip levels, every texel of level `n`
 * the value `n + 1` in each channel. One level is written without the mip count flag, as a file that
 * ships no chain is.
 */
function uncompressedDds(size: number, levels: number): Uint8Array {
    const header = new Int32Array(32);
    header[0] = 0x20534444; // "DDS "
    header[1] = 124;
    header[2] = 0x1 | 0x2 | 0x4 | 0x1000 | (levels > 1 ? 0x20000 : 0);
    header[3] = size;
    header[4] = size;
    header[7] = levels;
    header[19] = 32;
    header[20] = 0x41; // RGB with alpha
    header[22] = 32;
    header[23] = 0x00ff0000;
    header[24] = 0x0000ff00;
    header[25] = 0x000000ff;
    header[26] = 0xff000000 | 0;
    header[27] = 0x1000;

    const texels: number[] = [];
    for (let level = 0, edge = size; level < levels; level++) {
        texels.push(...new Array<number>(edge * edge * 4).fill(level + 1));
        edge = Math.max(1, edge >> 1);
    }

    const file = new Uint8Array(128 + texels.length);
    file.set(new Uint8Array(header.buffer), 0);
    file.set(texels, 128);
    return file;
}

describe("decodeTexture", () => {
    it("decodes a texture from its file's bytes", () => {
        const texture = decodeTexture(onePixelTga(10, 20, 30), "tga");

        assert.notEqual(texture, null);
        const image = texture?.image as {
            width: number;
            height: number;
            data: Uint8Array;
        };
        assert.equal(image.width, 1);
        assert.equal(image.height, 1);
        assert.deepEqual([...image.data], [10, 20, 30, 255]);
    });

    // A message's typed array can be a view into a larger buffer; only its own bytes are the file.
    it("reads only the view's bytes when they sit inside a larger buffer", () => {
        const file = onePixelTga(1, 2, 3);
        const padded = new Uint8Array(file.byteLength + 8);
        padded.set(file, 4);

        const texture = decodeTexture(
            padded.subarray(4, 4 + file.byteLength),
            "tga",
        );

        const image = texture?.image as { data: Uint8Array };
        assert.deepEqual([...image.data], [1, 2, 3, 255]);
    });

    it("has no texture for a format it does not decode", () => {
        assert.equal(decodeTexture(new Uint8Array([1, 2, 3]), "png"), null);
        assert.equal(decodeTexture(new Uint8Array([1, 2, 3]), ""), null);
    });

    // #418: the game samples through LINEAR samplers with a mip chain. A TGA arrived NEAREST with its top
    // level alone, blocky up close and shimmering at a distance.
    it("samples a TGA linearly, with a full mip chain generated for it", () => {
        const texture = decodeTexture(onePixelTga(1, 2, 3), "tga");

        assert.ok(texture !== null);
        assert.equal(texture.minFilter, THREE.LinearMipmapLinearFilter);
        assert.equal(texture.magFilter, THREE.LinearFilter);
        assert.equal(texture.generateMipmaps, true);
    });

    it("keeps the mip levels an uncompressed DDS ships", () => {
        const texture = decodeTexture(uncompressedDds(4, 3), "dds");

        assert.ok(texture !== null);
        assert.equal(texture.minFilter, THREE.LinearMipmapLinearFilter);
        assert.equal(texture.magFilter, THREE.LinearFilter);
        assert.equal(texture.generateMipmaps, false);
        assert.deepEqual(
            texture.mipmaps.map((level: { width: number }) => level.width),
            [4, 2, 1],
        );
        assert.equal((texture.mipmaps[1] as { data: Uint8Array }).data[0], 2);
    });

    it("generates the mip chain of an uncompressed DDS that ships none", () => {
        const texture = decodeTexture(uncompressedDds(4, 1), "dds");

        assert.ok(texture !== null);
        assert.equal(texture.minFilter, THREE.LinearMipmapLinearFilter);
        assert.equal(texture.magFilter, THREE.LinearFilter);
        assert.equal(texture.generateMipmaps, true);
        assert.equal(texture.mipmaps.length, 0);
    });
});
