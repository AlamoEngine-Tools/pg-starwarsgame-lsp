// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { deflateSync } from "node:zlib";

import { decodePng, textureContentOf } from "./pngDecode";

const SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

function chunk(type: string, body: Uint8Array): number[] {
    const length = [
        (body.length >>> 24) & 255,
        (body.length >>> 16) & 255,
        (body.length >>> 8) & 255,
        body.length & 255,
    ];
    // The decoder does not check CRCs, as no reader the game uses needs to; zero stands in.
    return [
        ...length,
        ...[...type].map((c) => c.charCodeAt(0)),
        ...body,
        0,
        0,
        0,
        0,
    ];
}

function header(
    width: number,
    height: number,
    depth: number,
    colour: number,
    interlace = 0,
): Uint8Array {
    return new Uint8Array([
        (width >>> 24) & 255,
        (width >>> 16) & 255,
        (width >>> 8) & 255,
        width & 255,
        (height >>> 24) & 255,
        (height >>> 16) & 255,
        (height >>> 8) & 255,
        height & 255,
        depth,
        colour,
        0,
        0,
        interlace,
    ]);
}

/** A PNG of the given scanlines, each already prefixed with its filter byte. */
function png(
    width: number,
    height: number,
    depth: number,
    colour: number,
    scanlines: number[],
    extra: [string, number[]][] = [],
    interlace = 0,
): Uint8Array {
    return new Uint8Array([
        ...SIGNATURE,
        ...chunk("IHDR", header(width, height, depth, colour, interlace)),
        ...extra.flatMap(([type, body]) => chunk(type, new Uint8Array(body))),
        ...chunk(
            "IDAT",
            new Uint8Array(deflateSync(new Uint8Array(scanlines))),
        ),
        ...chunk("IEND", new Uint8Array()),
    ]);
}

describe("textureContentOf", () => {
    it("knows a DDS by its magic, a PNG by its signature, and anything else as a TGA", () => {
        assert.equal(
            textureContentOf(new Uint8Array([0x44, 0x44, 0x53, 0x20, 124])),
            "dds",
        );
        assert.equal(
            textureContentOf(new Uint8Array([...SIGNATURE, 0])),
            "png",
        );
        assert.equal(textureContentOf(new Uint8Array([0, 0, 2, 0])), "tga");
        // Too short to carry a signature: whatever it is, it is no DDS and no PNG.
        assert.equal(textureContentOf(new Uint8Array([0x89, 0x50])), "tga");
        assert.equal(textureContentOf(new Uint8Array()), "tga");
    });
});

describe("decodePng", () => {
    it("decodes 8-bit RGBA, top row first", () => {
        const image = decodePng(
            png(1, 2, 8, 6, [0, 10, 20, 30, 40, 0, 50, 60, 70, 80]),
        );

        assert.deepEqual(
            image && { w: image.width, h: image.height, data: [...image.data] },
            {
                w: 1,
                h: 2,
                data: [10, 20, 30, 40, 50, 60, 70, 80],
            },
        );
    });

    // Each scanline names its own filter; all five appear in real files.
    it("reverses every scanline filter", () => {
        // Two RGB pixels a row, three rows: Sub, Up, Average, then Paeth on a fourth.
        const raw = [
            [1, 2, 3, 5, 7, 9],
            [11, 12, 13, 15, 17, 19],
            [21, 22, 23, 25, 27, 29],
            [31, 32, 33, 35, 37, 39],
        ];
        const paeth = (a: number, b: number, c: number): number => {
            const p = a + b - c;
            const [pa, pb, pc] = [
                Math.abs(p - a),
                Math.abs(p - b),
                Math.abs(p - c),
            ];
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        };
        const filtered: number[] = [];
        raw.forEach((row, y) => {
            const prior = y === 0 ? new Array<number>(6).fill(0) : raw[y - 1];
            const type = [1, 2, 3, 4][y];
            filtered.push(type);
            row.forEach((value, i) => {
                const left = i >= 3 ? row[i - 3] : 0;
                const up = prior[i];
                const upLeft = i >= 3 ? prior[i - 3] : 0;
                const predicted =
                    type === 1
                        ? left
                        : type === 2
                          ? up
                          : type === 3
                            ? (left + up) >> 1
                            : paeth(left, up, upLeft);
                filtered.push((value - predicted) & 255);
            });
        });

        const image = decodePng(png(2, 4, 8, 2, filtered));

        assert.deepEqual(
            [...(image?.data ?? [])],
            raw.flatMap((row) => [
                row[0],
                row[1],
                row[2],
                255,
                row[3],
                row[4],
                row[5],
                255,
            ]),
        );
    });

    it("decodes a palette with its transparency", () => {
        const image = decodePng(
            png(
                3,
                1,
                8,
                3,
                [0, 0, 1, 2],
                [
                    ["PLTE", [255, 0, 0, 0, 255, 0, 0, 0, 255]],
                    ["tRNS", [128]],
                ],
            ),
        );

        assert.deepEqual(
            [...(image?.data ?? [])],
            [255, 0, 0, 128, 0, 255, 0, 255, 0, 0, 255, 255],
        );
    });

    it("scales a 1-bit grey image to full range", () => {
        // 0b10100000: four pixels, white, black, white, black.
        const image = decodePng(png(4, 1, 1, 0, [0, 0b10100000]));

        assert.deepEqual(
            [...(image?.data ?? [])],
            [
                255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0,
                255,
            ],
        );
    });

    it("keeps the high byte of a 16-bit sample, and a grey-alpha pixel's alpha", () => {
        const rgb = decodePng(
            png(1, 1, 16, 2, [0, 0x12, 0x34, 0x56, 0x78, 0x9a, 0xbc]),
        );
        const greyAlpha = decodePng(png(1, 1, 8, 4, [0, 77, 99]));

        assert.deepEqual([...(rgb?.data ?? [])], [0x12, 0x56, 0x9a, 255]);
        assert.deepEqual([...(greyAlpha?.data ?? [])], [77, 77, 77, 99]);
    });

    it("makes the grey or colour tRNS names transparent", () => {
        const grey = decodePng(png(2, 1, 8, 0, [0, 7, 8], [["tRNS", [0, 7]]]));

        assert.deepEqual([...(grey?.data ?? [])], [7, 7, 7, 0, 8, 8, 8, 255]);
    });

    // Adam7 stores the image in seven passes; a 3x3 image has pixels in passes 1, 4, 5, 6 and 7 only.
    it("reassembles an interlaced image", () => {
        const at = (x: number, y: number): number => 10 * y + x;
        const passes = [
            [0, 0, 8, 8],
            [4, 0, 8, 8],
            [0, 4, 4, 8],
            [2, 0, 4, 4],
            [0, 2, 2, 4],
            [1, 0, 2, 2],
            [0, 1, 1, 2],
        ];
        const scanlines: number[] = [];
        for (const [x0, y0, dx, dy] of passes) {
            for (let y = y0; y < 3; y += dy) {
                const row: number[] = [];
                for (let x = x0; x < 3; x += dx) {
                    row.push(at(x, y));
                }
                if (row.length > 0) {
                    scanlines.push(0, ...row);
                }
            }
        }

        const image = decodePng(png(3, 3, 8, 0, scanlines, [], 1));

        const expected: number[] = [];
        for (let y = 0; y < 3; y++) {
            for (let x = 0; x < 3; x++) {
                expected.push(at(x, y), at(x, y), at(x, y), 255);
            }
        }
        assert.deepEqual([...(image?.data ?? [])], expected);
    });

    it("has no image for a file that is not a valid PNG", () => {
        assert.equal(decodePng(new Uint8Array([1, 2, 3])), null);
        const valid = png(1, 1, 8, 6, [0, 1, 2, 3, 4]);
        assert.equal(decodePng(valid.subarray(0, valid.length - 20)), null);
        // A filter type past Paeth.
        assert.equal(decodePng(png(1, 1, 8, 6, [5, 1, 2, 3, 4])), null);
        // A colour type the format does not define.
        assert.equal(decodePng(png(1, 1, 8, 5, [0, 1, 2, 3, 4])), null);
        // A palette image without its palette.
        assert.equal(decodePng(png(1, 1, 8, 3, [0, 0])), null);
    });
});
