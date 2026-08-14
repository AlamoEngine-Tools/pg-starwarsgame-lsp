// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Drawing;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using AnakinRaW.CommonUtilities.Hashing;
using Microsoft.Extensions.DependencyInjection;
using PG.Commons;
using PG.StarWarsGame.Files.MTD;
using PG.StarWarsGame.Files.MTD.Data;
using PG.StarWarsGame.Files.MTD.Services;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

/// <summary>
///     Builds synthetic mega textures and mega texture directories that are byte-for-byte the shape
///     the game ships.
/// </summary>
/// <remarks>
///     <para>
///         The TGA is written BY HAND rather than through an encoder, and deliberately reproduces the
///         shipped format exactly: uncompressed true-colour (image type 2), 32bpp BGRA, descriptor
///         <c>0x00</c> so the first row in the file is the BOTTOM row of the image. Encoding the
///         fixture with the same library that decodes it would let a vertical-flip bug cancel itself
///         out and pass, while every real game texture came out upside down.
///     </para>
///     <para>
///         The MTD is likewise hand-written: a <c>uint32</c> entry count followed by 81-byte records
///         of <c>name[64] | x | y | width | height | alpha</c>. Note 81, not 85 - the widely
///         circulated format description places <c>y</c> four bytes too late. Both shipped files
///         divide exactly by 81 and agree with their own header count.
///     </para>
/// </remarks>
internal static class MegaTextureFixture
{
    private const int RecordSize = 81;
    private const int NameSize = 64;

    /// <summary>
    ///     Writes an uncompressed 32bpp bottom-up TGA whose pixel at (x, y) - measured from the TOP
    ///     left, the way MTD coordinates are - is <paramref name="pixelAt" />.
    /// </summary>
    public static byte[] BuildTga(int width, int height, Func<int, int, Rgba> pixelAt)
    {
        var bytes = new byte[18 + width * height * 4];

        bytes[2] = 2;     // image type: uncompressed true-colour
        bytes[12] = (byte)(width & 0xFF);
        bytes[13] = (byte)((width >> 8) & 0xFF);
        bytes[14] = (byte)(height & 0xFF);
        bytes[15] = (byte)((height >> 8) & 0xFF);
        bytes[16] = 32;   // bits per pixel
        bytes[17] = 0;    // descriptor: origin bottom-left, no alpha bits declared

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var fileRow = height - 1 - y;
            var o = 18 + (fileRow * width + x) * 4;
            var p = pixelAt(x, y);
            bytes[o] = p.B;
            bytes[o + 1] = p.G;
            bytes[o + 2] = p.R;
            bytes[o + 3] = p.A;
        }

        return bytes;
    }

    /// <summary>Writes a mega texture directory containing <paramref name="entries" /> in order.</summary>
    public static byte[] BuildMtd(params (string Name, Rectangle Area, bool Alpha)[] entries)
    {
        var bytes = new byte[4 + entries.Length * RecordSize];
        WriteUInt32(bytes, 0, (uint)entries.Length);

        for (var i = 0; i < entries.Length; i++)
        {
            var (name, area, alpha) = entries[i];
            var o = 4 + i * RecordSize;

            for (var c = 0; c < name.Length; c++)
                bytes[o + c] = (byte)name[c];

            WriteUInt32(bytes, o + NameSize, (uint)area.X);
            WriteUInt32(bytes, o + NameSize + 4, (uint)area.Y);
            WriteUInt32(bytes, o + NameSize + 8, (uint)area.Width);
            WriteUInt32(bytes, o + NameSize + 12, (uint)area.Height);
            bytes[o + NameSize + 16] = alpha ? (byte)1 : (byte)0;
        }

        return bytes;
    }

    /// <summary>
    ///     Parses <paramref name="mtdBytes" /> through the real MTD library.
    /// </summary>
    /// <remarks>
    ///     Goes via a file on a <see cref="MockFileSystem" /> rather than a <see cref="MemoryStream" />
    ///     on purpose: the library calls <c>Stream.GetFilePath</c>, which throws
    ///     <see cref="InvalidOperationException" /> for any stream that carries no path information.
    /// </remarks>
    public static IMegaTextureDirectory LoadDirectory(byte[] mtdBytes, string path = @"C:\game\mt.mtd")
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(path, new MockFileData(mtdBytes));
        return CreateMtdService(fileSystem).Load(path).Content;
    }

    /// <summary>
    ///     An MTD reader backed by <paramref name="fileSystem" />. Callers that also hand a filesystem
    ///     to production code must share ONE instance: the reader opens files through its own
    ///     injected filesystem, so two mocks would silently disagree about what exists.
    /// </summary>
    /// <remarks>
    ///     Mirrors BaselineBuilder's registration order. MTD entries are CRC32-keyed, so the
    ///     directory reader needs the hashing services; PetroglyphCommons.ContributeServices uses
    ///     TryAdd and does not supply IHashingService itself, so it has to be registered first.
    /// </remarks>
    public static IMtdFileService CreateMtdService(IFileSystem fileSystem)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fileSystem);
        services.AddSingleton<IHashingService>(sp => new HashingService(sp));
        PetroglyphCommons.ContributeServices(services);
        services.SupportMTD();

        return services.BuildServiceProvider().GetRequiredService<IMtdFileService>();
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);
}
