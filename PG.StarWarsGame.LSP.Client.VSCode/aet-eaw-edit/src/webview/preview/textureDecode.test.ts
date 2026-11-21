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

/**
 * A 2x2 8-bit RGBA PNG, top row first: (10, 20, 30, 255) (40, 50, 60, 255), then (70, 80, 90, 255)
 * (100, 110, 120, 128). Written by Python's zlib, so it is no output of the decoder under test.
 */
const TWO_BY_TWO_PNG = new Uint8Array([
    137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 2, 0,
    0, 0, 2, 8, 6, 0, 0, 0, 114, 182, 13, 36, 0, 0, 0, 26, 73, 68, 65, 84, 120,
    218, 99, 224, 18, 145, 251, 175, 97, 100, 243, 159, 193, 45, 32, 234, 127,
    74, 94, 69, 3, 0, 50, 91, 6, 138, 121, 232, 132, 18, 0, 0, 0, 0, 73, 69, 78,
    68, 174, 66, 96, 130,
]);

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

    it("has no texture for bytes it cannot decode, rather than throwing", () => {
        assert.equal(decodeTexture(new Uint8Array([1, 2, 3]), "png"), null);
        assert.equal(decodeTexture(new Uint8Array([1, 2, 3]), ""), null);
        assert.equal(
            decodeTexture(new Uint8Array([0x44, 0x44, 0x53, 0x20, 1]), "dds"),
            null,
        );
    });

    // #420: the game decodes a PNG wherever it loads a texture, as the TGA it is handled like.
    it("decodes a PNG as it unpacks a TGA: top row first, flipped, linear through a generated mip chain", () => {
        const texture = decodeTexture(TWO_BY_TWO_PNG, "png");

        assert.ok(texture instanceof THREE.DataTexture);
        const image = texture.image as {
            width: number;
            height: number;
            data: Uint8Array;
        };
        assert.equal(image.width, 2);
        assert.equal(image.height, 2);
        assert.deepEqual(
            [...image.data],
            [
                10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110,
                120, 128,
            ],
        );
        assert.equal(texture.flipY, true);
        assert.equal(texture.minFilter, THREE.LinearMipmapLinearFilter);
        assert.equal(texture.magFilter, THREE.LinearFilter);
        assert.equal(texture.generateMipmaps, true);
        assert.equal(
            texture.colorSpace,
            decodeTexture(onePixelTga(1, 2, 3), "tga")?.colorSpace,
        );
    });

    // #420: the game hands the file's bytes to its loader with no format stated, so the CONTENT decides.
    it("decodes by the bytes, whatever extension the file was found under", () => {
        const pngAsTga = decodeTexture(TWO_BY_TWO_PNG, "tga");
        const tgaAsDds = decodeTexture(onePixelTga(10, 20, 30), "dds");
        const ddsAsPng = decodeTexture(uncompressedDds(4, 1), "png");

        assert.equal((pngAsTga?.image as { width: number }).width, 2);
        assert.deepEqual(
            [...(tgaAsDds?.image as { data: Uint8Array }).data],
            [10, 20, 30, 255],
        );
        assert.equal((ddsAsPng?.image as { width: number }).width, 4);
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
