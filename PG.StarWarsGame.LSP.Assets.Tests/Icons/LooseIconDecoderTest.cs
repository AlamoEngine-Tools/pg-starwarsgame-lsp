// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public sealed class LooseIconDecoderTest
{
    private const string Root = @"C:\mod\icons";

    private static readonly MegaTextureFixture.Rgba Colour = new(10, 20, 30);

    /// <summary>
    ///     Builds a source image of <paramref name="format" />. TGA is hand-written by the shared
    ///     fixture, exactly as the game ships it; PNG goes through this project's own writer, which
    ///     is also what the decoder passes straight back for a PNG source.
    /// </summary>
    private static byte[] ImageBytes(string format, int width = 4, int height = 4)
    {
        switch (format)
        {
            case "tga":
                return MegaTextureFixture.BuildTga(width, height, (_, _) => Colour);
            case "png":
                var pixels = new byte[width * height * 4];
                for (var i = 0; i < width * height; i++)
                {
                    pixels[i * 4] = Colour.R;
                    pixels[i * 4 + 1] = Colour.G;
                    pixels[i * 4 + 2] = Colour.B;
                    pixels[i * 4 + 3] = Colour.A;
                }

                return PngWriter.Write(width, height, pixels);
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    // BMP is no longer decodable - it was reachable through the previous imaging library and is not
    // through this one - so it is covered by the unsupported-format test instead.
    [Theory]
    [InlineData("tga")]
    [InlineData("png")]
    public void TryDecode_ReadsEverySupportedFormat(string format)
    {
        var path = $@"{Root}\i_button_luke.{format}";
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(path, new MockFileData(ImageBytes(format)));
        var catalog = LooseIconCatalog.Scan(fileSystem, [Root]);

        var png = LooseIconDecoder.TryDecode(fileSystem, catalog["I_BUTTON_LUKE"]);

        Assert.NotNull(png);
        var image = TestPng.Decode(png);
        Assert.Equal(4, image.Width);
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), image[0, 0]);
    }

    [Fact]
    public void TryDecode_BmpIsUnsupported()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile($@"{Root}\i_button_luke.bmp", new MockFileData([0, 1, 2]));
        var catalog = LooseIconCatalog.Scan(fileSystem, [Root]);

        Assert.Null(LooseIconDecoder.TryDecode(fileSystem, catalog["I_BUTTON_LUKE"]));
    }

    // One corrupt source image must not cost the caller every other icon.
    [Fact]
    public void DecodeAll_SkipsCorruptFilesAndReportsThem()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile($@"{Root}\i_good.png", new MockFileData(ImageBytes("png")));
        fileSystem.AddFile($@"{Root}\i_corrupt.png", new MockFileData([0xDE, 0xAD, 0xBE, 0xEF]));
        fileSystem.AddFile($@"{Root}\i_unsupported.bmp", new MockFileData([0, 1]));
        var catalog = LooseIconCatalog.Scan(fileSystem, [Root]);

        var decoded = LooseIconDecoder.DecodeAll(fileSystem, catalog, out var unsupported);

        Assert.True(decoded.ContainsKey("I_GOOD"));
        Assert.Single(decoded);
        Assert.Equal(2, unsupported.Count);
        Assert.Contains("i_corrupt", unsupported, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("i_unsupported", unsupported, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeAll_EmptyCatalog_YieldsNothing()
    {
        var decoded = LooseIconDecoder.DecodeAll(
            new MockFileSystem(), new Dictionary<string, LooseIcon>(), out var unsupported);

        Assert.Empty(decoded);
        Assert.Empty(unsupported);
    }
}



