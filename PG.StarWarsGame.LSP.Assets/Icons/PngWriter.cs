// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Writes 8-bit RGBA pixels as a PNG.
/// </summary>
/// <remarks>
///     <para>
///         Hand-rolled on purpose, against this project's usual preference for libraries. .NET has
///         no cross-platform PNG encoder in the box, and the alternatives each carry a cost the icon
///         pipeline should not pay: ImageSharp 4.x refuses to build in Release without a licence key
///         issued by its vendor, and pulling a whole imaging stack to serialise a few kilobytes of
///         icon is disproportionate. PNG's stored form is small and completely specified, so this is
///         one of the rare cases where writing it is cheaper than depending on it.
///     </para>
///     <para>
///         Deliberately minimal: colour type 6 (RGBA), bit depth 8, no interlacing, and filter type
///         0 on every scanline. Adaptive filtering would compress better, but these are 26x26 and
///         50x50 sprites - the whole shipped set is a few megabytes either way, and a filter
///         heuristic is exactly the kind of subtle code that would be wrong in a way nothing catches.
///     </para>
/// </remarks>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    ///     Encodes <paramref name="rgba" /> - four bytes per pixel, row-major, top row first.
    /// </summary>
    /// <exception cref="ArgumentException">The buffer is not exactly <c>width * height * 4</c> bytes.</exception>
    public static byte[] Write(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var expected = width * height * 4;
        if (rgba.Length != expected)
            throw new ArgumentException(
                $"Expected {expected} bytes for {width}x{height} RGBA, got {rgba.Length}.", nameof(rgba));

        using var output = new MemoryStream();
        output.Write(Signature);
        WriteChunk(output, "IHDR", BuildHeader(width, height));
        WriteChunk(output, "IDAT", Compress(width, height, rgba));
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte[] BuildHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: truecolour with alpha
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter method
        header[12] = 0; // interlace: none
        return header;
    }

    /// <summary>Zlib-compresses the scanlines, each prefixed with its filter byte (always 0).</summary>
    private static byte[] Compress(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            // raw[y * (stride + 1)] is the filter byte, already 0.
            rgba.Slice(y * stride, stride).CopyTo(raw.AsSpan(y * (stride + 1) + 1));
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            deflate.Write(raw);

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        // The CRC covers the type and the data together, so they are checksummed as one buffer.
        var payload = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, payload);
        data.CopyTo(payload.AsSpan(4));
        output.Write(payload);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(payload));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }
}
