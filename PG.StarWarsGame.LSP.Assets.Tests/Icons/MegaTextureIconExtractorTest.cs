// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using Rectangle = System.Drawing.Rectangle;
using Rgba = PG.StarWarsGame.LSP.Assets.Tests.Icons.MegaTextureFixture.Rgba;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public sealed class MegaTextureIconExtractorTest
{
    private static readonly Rgba Red = new(255, 0, 0);
    private static readonly Rgba Green = new(0, 255, 0);
    private static readonly Rgba Blue = new(0, 0, 255);
    private static readonly Rgba Background = new(8, 8, 8);

    /// <summary>
    ///     A 16x16 texture with three 4x4 blocks of flat colour at known positions, measured from the
    ///     TOP left. Anything else is <see cref="Background" />.
    /// </summary>
    private static byte[] BuildTexture()
    {
        return MegaTextureFixture.BuildTga(16, 16, (x, y) =>
        {
            if (x is >= 0 and < 4 && y is >= 0 and < 4) return Red;      // top-left corner
            if (x is >= 6 and < 10 && y is >= 2 and < 6) return Green;   // interior
            if (x is >= 12 and < 16 && y is >= 12 and < 16) return Blue; // bottom-right corner
            return Background;
        });
    }

    private static readonly (string Name, Rectangle Area, bool Alpha)[] Entries =
    [
        ("I_RED.TGA", new Rectangle(0, 0, 4, 4), true),
        ("I_GREEN.TGA", new Rectangle(6, 2, 4, 4), true),
        ("I_BLUE.TGA", new Rectangle(12, 12, 4, 4), false)
    ];

    private static TestPng.Decoded DecodePng(byte[] png) => TestPng.Decode(png);

    // ── Extraction ────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractAll_ReturnsOneEntryPerDirectoryRecord()
    {
        var directory = MegaTextureFixture.LoadDirectory(MegaTextureFixture.BuildMtd(Entries));
        using var texture = new MemoryStream(BuildTexture());

        var icons = MegaTextureIconExtractor.ExtractAll(directory, texture);

        Assert.Equal(3, icons.Count);
        Assert.Contains("I_RED.TGA", icons.Keys);
        Assert.Contains("I_GREEN.TGA", icons.Keys);
        Assert.Contains("I_BLUE.TGA", icons.Keys);
    }

    [Fact]
    public void ExtractAll_PreservesEachEntrySize()
    {
        var directory = MegaTextureFixture.LoadDirectory(MegaTextureFixture.BuildMtd(Entries));
        using var texture = new MemoryStream(BuildTexture());

        var icons = MegaTextureIconExtractor.ExtractAll(directory, texture);

        var red = DecodePng(icons["I_RED.TGA"]);
        Assert.Equal(4, red.Width);
        Assert.Equal(4, red.Height);
    }

    /// <summary>
    ///     The one that matters: MTD coordinates are measured from the TOP of the image while the TGA
    ///     stores its rows bottom-up, so a missing flip yields a crop from the wrong end of the
    ///     texture. Each block sits at a different height precisely so an inverted read cannot
    ///     accidentally land on the right colour.
    /// </summary>
    [Theory]
    [InlineData("I_RED.TGA", 255, 0, 0)]
    [InlineData("I_GREEN.TGA", 0, 255, 0)]
    [InlineData("I_BLUE.TGA", 0, 0, 255)]
    public void ExtractAll_CropsTheRegionTheDirectoryPointsAt(string name, byte r, byte g, byte b)
    {
        var directory = MegaTextureFixture.LoadDirectory(MegaTextureFixture.BuildMtd(Entries));
        using var texture = new MemoryStream(BuildTexture());

        var icons = MegaTextureIconExtractor.ExtractAll(directory, texture);

        var image = DecodePng(icons[name]);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var pixel = image[x, y];
            Assert.Equal((r, g, b, (byte)255), pixel);
        }
    }

    [Fact]
    public void ExtractAll_IsCaseInsensitiveOnName()
    {
        var directory = MegaTextureFixture.LoadDirectory(MegaTextureFixture.BuildMtd(Entries));
        using var texture = new MemoryStream(BuildTexture());

        var icons = MegaTextureIconExtractor.ExtractAll(directory, texture);

        Assert.True(icons.ContainsKey("i_red.tga"));
    }

    // ── Robustness ────────────────────────────────────────────────────────────

    /// <summary>
    ///     A malformed third-party directory must not abort a whole bake, matching the
    ///     "one bad record must not stop the scan" contract the asset readers already follow.
    /// </summary>
    [Fact]
    public void ExtractAll_SkipsEntriesReachingOutsideTheTexture()
    {
        var mtd = MegaTextureFixture.BuildMtd(
            ("I_RED.TGA", new Rectangle(0, 0, 4, 4), true),
            ("I_OFF_RIGHT.TGA", new Rectangle(14, 0, 4, 4), true),
            ("I_OFF_BOTTOM.TGA", new Rectangle(0, 14, 4, 4), true));
        var directory = MegaTextureFixture.LoadDirectory(mtd);
        using var texture = new MemoryStream(BuildTexture());

        var icons = MegaTextureIconExtractor.ExtractAll(directory, texture);

        Assert.Equal(["I_RED.TGA"], icons.Keys);
    }

    [Fact]
    public void ExtractAll_SkipsZeroSizedEntries()
    {
        var mtd = MegaTextureFixture.BuildMtd(
            ("I_RED.TGA", new Rectangle(0, 0, 4, 4), true),
            ("I_EMPTY.TGA", new Rectangle(0, 0, 0, 0), true));
        var directory = MegaTextureFixture.LoadDirectory(mtd);
        using var texture = new MemoryStream(BuildTexture());

        var icons = MegaTextureIconExtractor.ExtractAll(directory, texture);

        Assert.Equal(["I_RED.TGA"], icons.Keys);
    }

    [Fact]
    public void ExtractAll_PreservesAlpha()
    {
        var texture = MegaTextureFixture.BuildTga(4, 4, (_, _) => new Rgba(10, 20, 30, 128));
        var directory = MegaTextureFixture.LoadDirectory(
            MegaTextureFixture.BuildMtd(("I_TRANSLUCENT.TGA", new Rectangle(0, 0, 4, 4), true)));

        var icons = MegaTextureIconExtractor.ExtractAll(directory, new MemoryStream(texture));

        var image = DecodePng(icons["I_TRANSLUCENT.TGA"]);
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)128), image[0, 0]);
    }
}


