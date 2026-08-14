// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

/// <summary>
///     A minimal PNG reader for asserting on what <c>PngWriter</c> produced.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately NOT a general PNG decoder. It understands exactly the subset the writer
///         emits - 8-bit RGBA, no interlacing, filter type 0 on every scanline - and throws on
///         anything else. That narrowness is the point: it will not quietly cope with output the
///         writer should never have produced, so a regression in the encoder surfaces here rather
///         than being absorbed.
///     </para>
///     <para>
///         It also keeps the assertions honest in a way a shared library could not: reader and
///         writer were written from the format, not from each other, so a matching round-trip is
///         evidence about the bytes rather than about one implementation agreeing with itself.
///     </para>
/// </remarks>
internal static class TestPng
{
    internal sealed record Decoded(int Width, int Height, byte[] Rgba)
    {
        /// <summary>The RGBA quadruple at (x, y), top row first.</summary>
        public (byte R, byte G, byte B, byte A) this[int x, int y]
        {
            get
            {
                var i = (y * Width + x) * 4;
                return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
            }
        }
    }

    public static Decoded Decode(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.True(png.Length > 8 && png.Take(8).SequenceEqual(signature), "not a PNG");

        int width = 0, height = 0;
        var idat = new MemoryStream();
        var offset = 8;

        while (offset + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);

            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    Assert.Equal(8, data[8]);  // bit depth
                    Assert.Equal(6, data[9]);  // colour type: RGBA
                    Assert.Equal(0, data[12]); // interlace: none
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
            }

            offset += 12 + length; // length + type + data + crc
            if (type == "IEND")
                break;
        }

        Assert.True(width > 0 && height > 0, "PNG had no IHDR");

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        var bytes = raw.ToArray();

        var stride = width * 4;
        var rgba = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            Assert.Equal(0, bytes[row]); // only filter type 0 is expected
            Array.Copy(bytes, row + 1, rgba, y * stride, stride);
        }

        return new Decoded(width, height, rgba);
    }
}
