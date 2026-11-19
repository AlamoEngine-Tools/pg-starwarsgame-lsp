// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

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
});
