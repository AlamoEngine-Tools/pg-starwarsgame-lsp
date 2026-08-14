// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public sealed class LooseIconCatalogTest
{
    private const string Root = @"C:\mod\data\art\textures\icons";

    private static MockFileSystem FileSystemWith(params string[] paths)
    {
        var fileSystem = new MockFileSystem();
        foreach (var path in paths)
            fileSystem.AddFile(path, new MockFileData([0]));
        return fileSystem;
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    [Fact]
    public void Scan_FindsSupportedImageFiles()
    {
        var fileSystem = FileSystemWith(
            $@"{Root}\i_button_luke.tga",
            $@"{Root}\i_button_vader.png",
            $@"{Root}\i_button_yoda.bmp");

        var icons = LooseIconCatalog.Scan(fileSystem, [Root]);

        // Lookups go through the dictionary's case-insensitive comparer; the key itself keeps the
        // casing found on disk, so asserting against the Keys collection would compare ordinally.
        Assert.Equal(3, icons.Count);
        Assert.True(icons.ContainsKey("I_BUTTON_LUKE"));
        Assert.True(icons.ContainsKey("I_BUTTON_VADER"));
        Assert.True(icons.ContainsKey("I_BUTTON_YODA"));
    }

    /// <summary>
    ///     Entries are keyed by base name because a mega texture directory always records its entries
    ///     with a <c>.TGA</c> suffix regardless of what the packer was actually fed, so a
    ///     <c>foo.png</c> source still has to match an <c>I_FOO.TGA</c> record.
    /// </summary>
    [Fact]
    public void Scan_KeysByBaseNameIgnoringExtension()
    {
        var fileSystem = FileSystemWith($@"{Root}\i_button_luke.png");

        var icons = LooseIconCatalog.Scan(fileSystem, [Root]);

        Assert.True(icons.ContainsKey("I_BUTTON_LUKE"));
        Assert.True(icons.ContainsKey("i_button_luke"));
        Assert.False(icons.ContainsKey("I_BUTTON_LUKE.TGA"));
    }

    [Fact]
    public void Scan_RecursesIntoSubdirectories()
    {
        var fileSystem = FileSystemWith($@"{Root}\units\empire\i_button_isd.tga");

        var icons = LooseIconCatalog.Scan(fileSystem, [Root]);

        Assert.True(icons.ContainsKey("I_BUTTON_ISD"));
    }

    [Fact]
    public void Scan_IgnoresNonImageFiles()
    {
        var fileSystem = FileSystemWith(
            $@"{Root}\i_button_luke.tga",
            $@"{Root}\notes.txt",
            $@"{Root}\source.psd",
            $@"{Root}\build.xml");

        var icons = LooseIconCatalog.Scan(fileSystem, [Root]);

        Assert.Single(icons);
        Assert.True(icons.ContainsKey("I_BUTTON_LUKE"));
    }

    // ── Formats we can and cannot decode ──────────────────────────────────────

    /// <summary>
    ///     BMP is catalogued but flagged undecodable, so a caller can say "this format is not
    ///     supported" instead of the much less useful "icon not found".
    /// </summary>
    /// <remarks>
    ///     DDS used to be the undecodable case and is now supported - the decoder handles it
    ///     natively - while BMP went the other way when the imaging dependency changed. The point of
    ///     the test is the CATALOGUED-BUT-UNDECODABLE distinction, not which format occupies it.
    /// </remarks>
    [Fact]
    public void Scan_IncludesBmpButMarksItUndecodable()
    {
        var fileSystem = FileSystemWith(
            $@"{Root}\i_button_luke.tga",
            $@"{Root}\i_button_vader.bmp");

        var icons = LooseIconCatalog.Scan(fileSystem, [Root]);

        Assert.True(icons["I_BUTTON_LUKE"].IsDecodable);
        Assert.False(icons["I_BUTTON_VADER"].IsDecodable);
    }

    [Fact]
    public void Scan_TreatsDdsAsDecodable()
    {
        var icons = LooseIconCatalog.Scan(FileSystemWith($@"{Root}\i_button_vader.dds"), [Root]);

        Assert.True(icons["I_BUTTON_VADER"].IsDecodable);
    }

    // ── Roots ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan_MissingRootIsIgnored()
    {
        var fileSystem = FileSystemWith($@"{Root}\i_button_luke.tga");

        var icons = LooseIconCatalog.Scan(fileSystem, [Root, @"C:\mod\does\not\exist"]);

        Assert.Single(icons);
    }

    [Fact]
    public void Scan_NoRootsYieldsEmptyCatalog()
    {
        Assert.Empty(LooseIconCatalog.Scan(new MockFileSystem(), []));
    }

    /// <summary>The first root that declares a name wins, so roots read as a priority list.</summary>
    [Fact]
    public void Scan_EarlierRootWinsOnDuplicateName()
    {
        const string second = @"C:\mod\data\art\textures\icons2";
        var fileSystem = FileSystemWith(
            $@"{Root}\i_button_luke.tga",
            $@"{second}\i_button_luke.png");

        var icons = LooseIconCatalog.Scan(fileSystem, [Root, second]);

        Assert.Single(icons);
        Assert.Equal(".tga", icons["I_BUTTON_LUKE"].Extension);
    }
}
