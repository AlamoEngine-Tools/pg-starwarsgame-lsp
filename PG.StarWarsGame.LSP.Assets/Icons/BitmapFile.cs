// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Reads an uncompressed Windows bitmap into top-down RGBA.
/// </summary>
/// <remarks>
///     <para>
///         Here because the game goes by content, not by name: two shipped mods keep their mega
///         texture as a bitmap under the <c>.tga</c> name the engine looks for, and the game shows
///         their icons. MEASURED 2026-11-09 on both files: <c>BM</c>, a 40-byte info header, 2048x2048,
///         32 bits per pixel, no compression, and alpha values from 0 to 254 in the fourth byte. Read
///         as a TGA they fail with "Detected invalid targa image" - the report's exact error - and
///         every icon of the mod then counts as missing (#166).
///     </para>
///     <para>
///         Covers what such files are: BITMAPINFOHEADER or a later header, 24 or 32 bits per pixel,
///         uncompressed or the bit-field form with the usual BGR(A) masks, bottom-up or top-down. The
///         alpha is the fourth byte of a 32-bit pixel whatever the header says about it - the decision
///         the TGA path takes, for the same reason. Anything else is refused with a message naming
///         what it was, so the log says why instead of "invalid targa".
///     </para>
/// </remarks>
internal static class BitmapFile
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const int Uncompressed = 0; // BI_RGB
    private const int BitFields = 3; // BI_BITFIELDS

    /// <summary>Whether <paramref name="bytes" /> start like a Windows bitmap.</summary>
    public static bool IsBitmap(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= FileHeaderSize + InfoHeaderSize && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
    }

    /// <summary>The bitmap's size and its pixels as top-down RGBA.</summary>
    /// <exception cref="NotSupportedException">The bitmap is of a shape this reader does not cover.</exception>
    public static (int Width, int Height, byte[] Rgba) Decode(ReadOnlySpan<byte> bytes)
    {
        if (!IsBitmap(bytes))
            throw new NotSupportedException("Not a Windows bitmap.");

        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes[10..]);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[14..]);
        if (headerSize < InfoHeaderSize)
            throw new NotSupportedException(
                $"Unsupported bitmap: a {headerSize}-byte info header (BITMAPINFOHEADER or later is read).");

        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]);
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(bytes[30..]);

        if (width <= 0 || rawHeight == 0)
            throw new NotSupportedException($"Unsupported bitmap: {width}x{rawHeight} pixels.");
        if (bitsPerPixel is not (24 or 32))
            throw new NotSupportedException(
                $"Unsupported bitmap: {bitsPerPixel} bits per pixel (24 or 32 are read).");

        switch (compression)
        {
            case Uncompressed:
                break;
            case BitFields when HasPlainMasks(bytes):
                break;
            case BitFields:
                throw new NotSupportedException("Unsupported bitmap: bit fields other than plain BGR(A).");
            default:
                throw new NotSupportedException(
                    $"Unsupported bitmap: compression {compression} (only uncompressed pixels are read).");
        }

        // A negative height is the top-down form; the usual positive one stores the bottom row first.
        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);
        var bytesPerPixel = bitsPerPixel / 8;
        var stride = (width * bytesPerPixel + 3) & ~3; // rows are padded to four bytes

        if (dataOffset < FileHeaderSize + headerSize || dataOffset + (long)stride * height > bytes.Length)
            throw new NotSupportedException("Unsupported bitmap: the pixel data runs past the end of the file.");

        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var fileRow = topDown ? y : height - 1 - y;
            var s = dataOffset + fileRow * stride;
            var t = y * width * 4;

            for (var x = 0; x < width; x++, s += bytesPerPixel, t += 4)
            {
                // Source channels are BGR(A).
                rgba[t] = bytes[s + 2];
                rgba[t + 1] = bytes[s + 1];
                rgba[t + 2] = bytes[s];
                rgba[t + 3] = bytesPerPixel == 4 ? bytes[s + 3] : (byte)255;
            }
        }

        return (width, height, rgba);
    }

    /// <summary>
    ///     The bit-field masks right after the info header (or inside a V4/V5 header, at the same
    ///     offset) are the plain BGR(A) layout: red in the third byte, green in the second, blue in
    ///     the first. Only then do the pixels read like the uncompressed form.
    /// </summary>
    private static bool HasPlainMasks(ReadOnlySpan<byte> bytes)
    {
        const int masks = FileHeaderSize + InfoHeaderSize;
        if (bytes.Length < masks + 12) return false;

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[masks..]) == 0x00FF0000
               && BinaryPrimitives.ReadUInt32LittleEndian(bytes[(masks + 4)..]) == 0x0000FF00
               && BinaryPrimitives.ReadUInt32LittleEndian(bytes[(masks + 8)..]) == 0x000000FF;
    }
}