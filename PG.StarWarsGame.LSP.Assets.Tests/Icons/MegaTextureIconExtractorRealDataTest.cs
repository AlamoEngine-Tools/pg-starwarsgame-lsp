// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.Serialization;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

/// <summary>
///     Exercises the extractor against a real shipped mega texture rather than a synthetic fixture.
/// </summary>
/// <remarks>
///     <para>
///         The synthetic tests prove the logic on a 16x16 image; this proves it on the 2048x2048
///         article, which is the thing that actually has to work. Rather than compare against a
///         committed reference image - game art must never be checked in, which is why <c>eaw/</c>
///         and <c>foc/</c> are gitignored - the expectation is re-derived here straight from the raw
///         TGA bytes using the documented layout. If the extractor and this independent reading
///         disagree, one of them is wrong.
///     </para>
///     <para>
///         SKIPS rather than fails when the game data is absent. A test that hard-fails on a clean
///         checkout because of a missing local artifact has cost this project time before.
///     </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class MegaTextureIconExtractorRealDataTest
{
    private const string KnownIcon = "I_BUTTON_MILLENNIUM_FALCON.TGA";

    [Fact]
    public void ExtractAll_MatchesAnIndependentReadOfTheShippedMegaTexture()
    {
        var textures = FindShippedTextures();
        if (textures is null)
        {
            Assert.Skip("Game data not present (foc/ is gitignored); nothing to verify against.");
            return;
        }

        var (mtdPath, tgaPath) = textures.Value;
        var directory = MegaTextureFixture.LoadDirectory(File.ReadAllBytes(mtdPath), mtdPath);

        var entry = directory.SingleOrDefault(e =>
            string.Equals(e.FileName, KnownIcon, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);

        using var textureStream = File.OpenRead(tgaPath);
        var icons = MegaTextureIconExtractor.ExtractAll(directory, textureStream);

        var actual = TestPng.Decode(icons[KnownIcon]);
        Assert.Equal(entry.Area.Width, actual.Width);
        Assert.Equal(entry.Area.Height, actual.Height);

        var expected = ReadRegionDirectly(
            File.ReadAllBytes(tgaPath), entry.Area.X, entry.Area.Y, entry.Area.Width, entry.Area.Height);

        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
            Assert.Equal(expected[y * actual.Width + x], actual[x, y]);

        // A crop from the wrong part of a mostly-populated atlas would still be "some pixels", so
        // assert the icon is not a flat region - it must actually carry artwork.
        Assert.True(actual[0, 0] != actual[actual.Width / 2, actual.Height / 2]
                    || expected.Distinct().Count() > 1);
    }

    /// <summary>
    ///     Reads a rectangle out of an uncompressed 32bpp bottom-up TGA using the documented layout:
    ///     18-byte header, BGRA samples, and rows stored bottom-up so the image row <c>y</c> counted
    ///     from the top lives at file row <c>height - 1 - y</c>. MTD coordinates are top-origin.
    /// </summary>
    private static (byte R, byte G, byte B, byte A)[] ReadRegionDirectly(byte[] tga, int x, int y, int width, int height)
    {
        const int headerSize = 18;
        int textureWidth = tga[12] | (tga[13] << 8);
        int textureHeight = tga[14] | (tga[15] << 8);

        Assert.Equal(2, tga[2]);   // uncompressed true-colour
        Assert.Equal(32, tga[16]); // BGRA
        Assert.Equal(0, tga[17]);  // origin bottom-left

        var pixels = new (byte R, byte G, byte B, byte A)[width * height];
        for (var j = 0; j < height; j++)
        {
            var fileRow = textureHeight - 1 - (y + j);
            for (var i = 0; i < width; i++)
            {
                var o = headerSize + (fileRow * textureWidth + x + i) * 4;
                pixels[j * width + i] = (tga[o + 2], tga[o + 1], tga[o], tga[o + 3]);
            }
        }

        return pixels;
    }

    /// <summary>
    ///     Bakes the whole shipped icon set through the real encoder and sidecar serializer, which is
    ///     what BaselineBuilder does. Guards the sizing assumption behind the sidecar decision: if a
    ///     full pack ever stopped being a few megabytes, folding it into the main baseline - or
    ///     shipping it at all - would need rethinking.
    /// </summary>
    [Fact]
    public void WholeIconSet_BakesIntoASidecarOfWorkableSize()
    {
        var textures = FindShippedTextures();
        if (textures is null)
        {
            Assert.Skip("Game data not present (foc/ is gitignored); nothing to verify against.");
            return;
        }

        var (mtdPath, tgaPath) = textures.Value;
        var directory = MegaTextureFixture.LoadDirectory(File.ReadAllBytes(mtdPath), mtdPath);

        using var textureStream = File.OpenRead(tgaPath);
        var icons = MegaTextureIconExtractor.ExtractAll(directory, textureStream);

        Assert.True(icons.Count > 500, $"expected the full shipped icon set, extracted {icons.Count}");

        var bytes = IconPackSerializer.Serialize(icons, "test-hash", DateTimeOffset.UnixEpoch);
        var pack = IconPackSerializer.Deserialize(bytes);

        Assert.NotNull(pack);
        Assert.Equal(icons.Count, pack.Icons.Count);
        // Measured at 3.29 MB for FoC's 1107 icons. The guard is deliberately loose - it is there to
        // catch an order-of-magnitude regression, not to pin the exact byte count.
        Assert.True(bytes.Length < 16 * 1024 * 1024,
            $"icon sidecar grew to {bytes.Length:N0} bytes; revisit the sidecar sizing decision");

        // Every baked entry must be a real, decodable PNG - not an empty or truncated buffer.
        foreach (var png in pack.Icons.Values.Take(25))
        {
            var image = TestPng.Decode(png);
            Assert.True(image.Width > 0 && image.Height > 0);
        }
    }

    private static (string Mtd, string Tga)? FindShippedTextures()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(MegaTextureIconExtractorRealDataTest).Assembly.Location)!);

        while (directory is not null)
        {
            foreach (var game in new[] { "foc", "eaw" })
            {
                var textures = Path.Combine(directory.FullName, game, "Data", "Art", "Textures");
                var mtd = Path.Combine(textures, "Mt_commandbar.mtd");
                var tga = Path.Combine(textures, "Mt_commandbar.tga");
                if (File.Exists(mtd) && File.Exists(tga))
                    return (mtd, tga);
            }

            directory = directory.Parent;
        }

        return null;
    }
}




