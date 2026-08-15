// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Project;
using Rectangle = System.Drawing.Rectangle;
using Rgba = PG.StarWarsGame.LSP.Assets.Tests.Icons.MegaTextureFixture.Rgba;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public sealed class IconCatalogLoaderTest
{
    private const string Root = @"C:\mod";

    private static readonly IconProjectSettings Settings =
        new("data/art/textures/mt_mymod", ["data/art/textures/icons"]);

    private static IconPack BaselineWith(params string[] names)
    {
        return new IconPack(
            names.ToImmutableDictionary(n => n, _ => new byte[] { 9 }, StringComparer.OrdinalIgnoreCase),
            "hash",
            DateTimeOffset.UnixEpoch);
    }

    private static byte[] SolidPng(byte r, byte g, byte b)
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
        {
            pixels[i * 4] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = 255;
        }

        return PngWriter.Write(4, 4, pixels);
    }

    /// <summary>Writes a one-entry mega texture pair for <paramref name="iconName" />.</summary>
    private static void AddMegaTexture(MockFileSystem fileSystem, string iconName, Rgba colour)
    {
        fileSystem.AddFile($@"{Root}\data\art\textures\mt_mymod.mtd",
            new MockFileData(MegaTextureFixture.BuildMtd((iconName, new Rectangle(0, 0, 4, 4), true))));
        fileSystem.AddFile($@"{Root}\data\art\textures\mt_mymod.tga",
            new MockFileData(MegaTextureFixture.BuildTga(4, 4, (_, _) => colour)));
    }

    private static IconCatalog Load(MockFileSystem fileSystem, IconPack baseline)
    {
        return IconCatalogLoader.Load(
            fileSystem, MegaTextureFixture.CreateMtdService(fileSystem), Root, Settings, baseline);
    }

    // ── Layers ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NoWorkspaceMegaTexture_UsesBaseline()
    {
        var catalog = Load(new MockFileSystem(), BaselineWith("I_BUTTON_LUKE.TGA"));

        Assert.False(catalog.HasWorkspaceMegaTexture);
        Assert.Equal(IconSource.Baseline, catalog.Resolve("I_BUTTON_LUKE.TGA")!.Source);
    }

    [Fact]
    public void Load_WorkspaceMegaTexture_IsReadAndWins()
    {
        var fileSystem = new MockFileSystem();
        AddMegaTexture(fileSystem, "I_BUTTON_LUKE.TGA", new Rgba(255, 0, 0));

        var catalog = Load(fileSystem, BaselineWith("I_BUTTON_LUKE.TGA"));

        Assert.True(catalog.HasWorkspaceMegaTexture);
        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");
        Assert.Equal(IconSource.WorkspaceMegaTexture, result!.Source);
        var image = TestPng.Decode(result.Png);
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), image[0, 0]);
    }

    [Fact]
    public void Load_LooseSourcesAreDecoded()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile($@"{Root}\data\art\textures\icons\i_button_luke.png",
            new MockFileData(SolidPng(0, 255, 0)));

        var catalog = Load(fileSystem, BaselineWith());

        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");
        Assert.Equal(IconSource.LooseSource, result!.Source);
        var image = TestPng.Decode(result.Png);
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), image[0, 0]);
    }

    /// <summary>Drawn but not repacked - the case the out-of-sync warning exists for.</summary>
    [Fact]
    public void Load_IconInSourceFolderButNotInMegaTexture_IsFlaggedStale()
    {
        var fileSystem = new MockFileSystem();
        AddMegaTexture(fileSystem, "I_BUTTON_OTHER.TGA", new Rgba(255, 0, 0));
        fileSystem.AddFile($@"{Root}\data\art\textures\icons\i_button_luke.png",
            new MockFileData(SolidPng(0, 255, 0)));

        var catalog = Load(fileSystem, BaselineWith("I_BUTTON_LUKE.TGA"));

        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");
        Assert.Equal(IconSource.LooseSource, result!.Source);
        Assert.True(result.IsMegaTextureStale);
    }

    // ── Survivability ─────────────────────────────────────────────────────────

    // Half a pair is not a mega texture; treating it as absent lets the baseline still answer.
    [Fact]
    public void Load_MtdWithoutTexture_CountsAsNoWorkspaceMegaTexture()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile($@"{Root}\data\art\textures\mt_mymod.mtd",
            new MockFileData(MegaTextureFixture.BuildMtd(("I_X.TGA", new Rectangle(0, 0, 4, 4), true))));

        var catalog = Load(fileSystem, BaselineWith("I_BUTTON_LUKE.TGA"));

        Assert.False(catalog.HasWorkspaceMegaTexture);
        Assert.Equal(IconSource.Baseline, catalog.Resolve("I_BUTTON_LUKE.TGA")!.Source);
    }

    [Fact]
    public void Load_CorruptMegaTexture_DegradesToBaselineInsteadOfThrowing()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile($@"{Root}\data\art\textures\mt_mymod.mtd", new MockFileData([0xDE, 0xAD]));
        fileSystem.AddFile($@"{Root}\data\art\textures\mt_mymod.tga", new MockFileData([0xBE, 0xEF]));

        var catalog = Load(fileSystem, BaselineWith("I_BUTTON_LUKE.TGA"));

        Assert.False(catalog.HasWorkspaceMegaTexture);
        Assert.Equal(IconSource.Baseline, catalog.Resolve("I_BUTTON_LUKE.TGA")!.Source);
    }

    [Fact]
    public void Load_MissingSourceRoot_IsNotAnError()
    {
        var catalog = Load(new MockFileSystem(), BaselineWith("I_BUTTON_LUKE.TGA"));

        Assert.NotNull(catalog.Resolve("I_BUTTON_LUKE.TGA"));
    }
}



