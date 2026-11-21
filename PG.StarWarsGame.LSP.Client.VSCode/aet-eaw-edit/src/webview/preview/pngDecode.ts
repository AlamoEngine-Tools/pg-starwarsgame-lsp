// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a texture file IS, by its bytes, and PNG decoding.
//
// Measured in the game (FoC x64, 2024 release, #420): the file a texture name resolves to goes to the
// Direct3D texture loader as raw bytes with no format stated, so its content decides how it is decoded -
// a PNG loads like a TGA or a DDS, under any extension. The preview sniffs the same way.
//
// The PNG decode is synchronous because the texture pipeline is: the bytes become a texture in the one
// call that receives them, unpacked to RGBA like a TGA so a PNG and a TGA of one picture are drawn the
// same. The browser's own decoders are asynchronous and premultiply alpha on the way through a canvas,
// which a particle sheet drawn additively cannot afford. `fast-png` does the file format; this only turns
// what it returns into 8-bit RGBA.

import { convertIndexedToRgb, decode, type DecodedPng } from "fast-png";

/** The formats the preview decodes, as the bytes say. */
export type TextureContent = "dds" | "png" | "tga";

const PNG_SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

/** "DDS " - the four bytes every DDS file starts with. */
const DDS_MAGIC = [0x44, 0x44, 0x53, 0x20];

/** Where IHDR's colour type sits: the signature, the chunk's length and type, then width, height, depth. */
const COLOUR_TYPE_OFFSET = 25;

/** The colour type of a palette image. */
const INDEXED = 3;

function startsWith(bytes: Uint8Array, prefix: readonly number[]): boolean {
    return (
        bytes.length >= prefix.length && prefix.every((b, i) => bytes[i] === b)
    );
}

/**
 * What a texture file is by its content: DDS by its magic, PNG by its signature, and TGA otherwise - a TGA
 * has no signature of its own to test for.
 */
export function textureContentOf(bytes: Uint8Array): TextureContent {
    if (startsWith(bytes, DDS_MAGIC)) {
        return "dds";
    }
    return startsWith(bytes, PNG_SIGNATURE) ? "png" : "tga";
}

/** A decoded image: 8-bit RGBA, top row first. */
export interface DecodedImage {
    data: Uint8Array;
    width: number;
    height: number;
}

/**
 * Decodes a PNG to 8-bit RGBA, top row first, or null when the file is not one that can be read: every
 * colour type and bit depth the format defines, palettes and their transparency, and interlacing. A
 * 16-bit sample keeps its high byte; gamma and colour-profile chunks are not applied, so the values are
 * the file's own, as a TGA's are.
 */
export function decodePng(bytes: Uint8Array): DecodedImage | null {
    if (!startsWith(bytes, PNG_SIGNATURE)) {
        return null;
    }

    try {
        const png = decode(bytes);
        // The library reads a palette image that carries no palette as grey; the format has no image there.
        if (
            bytes[COLOUR_TYPE_OFFSET] === INDEXED &&
            png.palette === undefined
        ) {
            return null;
        }
        return toRgba(png);
    } catch {
        // A malformed file, a bad deflate stream or a read past the end: no image either way.
        return null;
    }
}

function toRgba(png: DecodedPng): DecodedImage {
    const { width, height } = png;
    const pixels = width * height;
    const out = new Uint8Array(pixels * 4);

    if (png.palette !== undefined) {
        // Expanded by the library; an entry carries a fourth value once the file has a tRNS chunk.
        const colours = convertIndexedToRgb(png);
        const stride = colours.length / Math.max(1, pixels);
        for (let i = 0; i < pixels; i++) {
            out[i * 4] = colours[i * stride];
            out[i * 4 + 1] = colours[i * stride + 1];
            out[i * 4 + 2] = colours[i * stride + 2];
            out[i * 4 + 3] = stride === 4 ? colours[i * stride + 3] : 255;
        }
        return { data: out, width, height };
    }

    const { channels, depth, data } = png;
    const max = (1 << depth) - 1;
    // Below 8 bits (grey only) the library leaves each row packed, padded to a whole byte.
    const rowBytes = Math.ceil((width * depth) / 8);
    const sample = (pixel: number, channel: number): number => {
        if (depth >= 8) {
            return data[pixel * channels + channel];
        }
        const x = pixel % width;
        const y = (pixel - x) / width;
        const bit = x * depth;
        const byte = data[y * rowBytes + (bit >> 3)];
        return (byte >> (8 - depth - (bit & 7))) & max;
    };
    const to8 = (value: number): number =>
        depth === 16
            ? value >> 8
            : depth < 8
              ? Math.round((value * 255) / max)
              : value;

    // A tRNS chunk on grey or RGB names one colour, in raw samples, that is fully transparent.
    const key =
        png.transparency !== undefined && png.transparency.length > 0
            ? png.transparency
            : null;
    const colourChannels = channels === 1 || channels === 2 ? 1 : 3;
    const hasAlpha = channels === 2 || channels === 4;

    for (let i = 0; i < pixels; i++) {
        const r = sample(i, 0);
        const g = colourChannels === 3 ? sample(i, 1) : r;
        const b = colourChannels === 3 ? sample(i, 2) : r;
        out[i * 4] = to8(r);
        out[i * 4 + 1] = to8(g);
        out[i * 4 + 2] = to8(b);

        if (hasAlpha) {
            out[i * 4 + 3] = to8(sample(i, colourChannels));
        } else if (
            key !== null &&
            key[0] === r &&
            (colourChannels === 1 || (key[1] === g && key[2] === b))
        ) {
            out[i * 4 + 3] = 0;
        } else {
            out[i * 4 + 3] = 255;
        }
    }

    return { data: out, width, height };
}
