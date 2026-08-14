// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public sealed class IconCatalogTest
{
    private static readonly byte[] FromMegaTexture = [1];
    private static readonly byte[] FromLoose = [2];
    private static readonly byte[] FromBaseline = [3];

    private static Dictionary<string, byte[]> Packed(params string[] names) =>
        names.ToDictionary(n => n, _ => FromMegaTexture, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, byte[]> Loose(params string[] names) =>
        names.ToDictionary(n => n, _ => FromLoose, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, byte[]> Baseline(params string[] names) =>
        names.ToDictionary(n => n, _ => FromBaseline, StringComparer.OrdinalIgnoreCase);

    // ── pixel dimensions ─────────────────────────────────────────────────────

    // The card scales artwork by its natural size, so a resolution has to carry one. Reading it off
    // the PNG rather than storing it alongside means every layer reports it - baked baseline,
    // workspace pack, loose source - with no change to how any of them are stored.
    [Fact]
    public void Resolve_ReportsThePixelDimensionsOfTheIcon()
    {
        var png = PngWriter.Write(262, 20, new byte[262 * 20 * 4]);
        var catalog = new IconCatalog(
            null, new Dictionary<string, byte[]>(),
            new Dictionary<string, byte[]> { ["E_TOPBAR.TGA"] = png });

        var result = catalog.Resolve("E_TOPBAR.TGA");

        Assert.NotNull(result);
        Assert.Equal(262, result.Width);
        Assert.Equal(20, result.Height);
    }

    // Unreadable bytes must not take the card down with them: the caller falls back to a nominal
    // size and still draws, which is why this reports zero rather than throwing.
    [Fact]
    public void Resolve_BytesAreNotAPng_ReportsZeroRatherThanThrowing()
    {
        var catalog = new IconCatalog(null, Loose(), Baseline("I_BUTTON_LUKE.TGA"));

        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");

        Assert.NotNull(result);
        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
    }

    // ── No workspace mega texture: the baked base game answers ────────────────

    [Fact]
    public void Resolve_NoWorkspaceMegaTexture_FallsBackToBaseline()
    {
        var catalog = new IconCatalog(null, Loose(), Baseline("I_BUTTON_LUKE.TGA"));

        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");

        Assert.Equal(IconSource.Baseline, result!.Source);
        Assert.False(result.IsMegaTextureStale);
    }

    /// <summary>A project's own art outranks the base game's icon of the same name.</summary>
    [Fact]
    public void Resolve_NoWorkspaceMegaTexture_LooseSourceBeatsBaseline()
    {
        var catalog = new IconCatalog(null, Loose("I_BUTTON_LUKE"), Baseline("I_BUTTON_LUKE.TGA"));

        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");

        Assert.Equal(IconSource.LooseSource, result!.Source);
        Assert.Equal(FromLoose, result.Png);
    }

    // ── Workspace mega texture present: it wins, and it is exhaustive ─────────

    [Fact]
    public void Resolve_WorkspaceMegaTexture_WinsOverLooseAndBaseline()
    {
        var catalog = new IconCatalog(
            Packed("I_BUTTON_LUKE.TGA"), Loose("I_BUTTON_LUKE"), Baseline("I_BUTTON_LUKE.TGA"));

        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");

        Assert.Equal(IconSource.WorkspaceMegaTexture, result!.Source);
        Assert.Equal(FromMegaTexture, result.Png);
    }

    /// <summary>
    ///     The key rule: a .mtd is not mergeable, so a mod that ships one must carry the base game's
    ///     entries too. Silently falling back to the baseline would hide a genuinely broken mod - the
    ///     game would show nothing, so neither should the preview.
    /// </summary>
    [Fact]
    public void Resolve_WorkspaceMegaTextureMissingEntry_DoesNotFallBackToBaseline()
    {
        var catalog = new IconCatalog(
            Packed("I_BUTTON_OTHER.TGA"), Loose(), Baseline("I_BUTTON_LUKE.TGA"));

        Assert.Null(catalog.Resolve("I_BUTTON_LUKE.TGA"));
    }

    /// <summary>Drawn but not repacked: render it, but say so.</summary>
    [Fact]
    public void Resolve_WorkspaceMegaTextureMissingEntry_ButLooseSourceExists_IsFlaggedStale()
    {
        var catalog = new IconCatalog(
            Packed("I_BUTTON_OTHER.TGA"), Loose("I_BUTTON_LUKE"), Baseline("I_BUTTON_LUKE.TGA"));

        var result = catalog.Resolve("I_BUTTON_LUKE.TGA");

        Assert.Equal(IconSource.LooseSource, result!.Source);
        Assert.True(result.IsMegaTextureStale);
        Assert.Equal(FromLoose, result.Png);
    }

    [Fact]
    public void Resolve_UnknownName_ReturnsNull()
    {
        var catalog = new IconCatalog(null, Loose(), Baseline("I_BUTTON_LUKE.TGA"));

        Assert.Null(catalog.Resolve("I_BUTTON_NOBODY.TGA"));
    }

    // ── Name shapes ───────────────────────────────────────────────────────────

    /// <summary>XML authors write the name either way; a .mtd always records the suffixed form.</summary>
    [Theory]
    [InlineData("I_BUTTON_LUKE.TGA")]
    [InlineData("I_BUTTON_LUKE")]
    [InlineData("i_button_luke.tga")]
    [InlineData("  I_BUTTON_LUKE.TGA  ")]
    public void Resolve_AcceptsSuffixedAndBareNames(string requested)
    {
        var catalog = new IconCatalog(null, Loose(), Baseline("I_BUTTON_LUKE.TGA"));

        Assert.NotNull(catalog.Resolve(requested));
    }

    [Theory]
    [InlineData("I_BUTTON_LUKE.TGA")]
    [InlineData("I_BUTTON_LUKE")]
    public void Resolve_MatchesLooseSourcesByBaseName(string requested)
    {
        var catalog = new IconCatalog(null, Loose("I_BUTTON_LUKE"), Baseline());

        Assert.Equal(IconSource.LooseSource, catalog.Resolve(requested)!.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankName_ReturnsNull(string requested)
    {
        Assert.Null(new IconCatalog(null, Loose(), Baseline("I_X.TGA")).Resolve(requested));
    }

    // ── Shape of the catalog itself ───────────────────────────────────────────

    // Null and empty mean different things: "ships no mega texture" versus "ships an empty one".
    [Fact]
    public void HasWorkspaceMegaTexture_DistinguishesAbsentFromEmpty()
    {
        Assert.False(new IconCatalog(null, Loose(), Baseline()).HasWorkspaceMegaTexture);
        Assert.True(new IconCatalog(Packed(), Loose(), Baseline()).HasWorkspaceMegaTexture);
    }

    // ── The backing set for the "rebuild your mega texture" warning ───────────

    [Fact]
    public void IconsAwaitingRepack_ListsSourcesMissingFromTheMegaTexture()
    {
        var catalog = new IconCatalog(
            Packed("I_BUTTON_PACKED.TGA"),
            Loose("I_BUTTON_PACKED", "I_BUTTON_DRAWN_ONLY"),
            Baseline());

        Assert.Equal(["I_BUTTON_DRAWN_ONLY"], catalog.IconsAwaitingRepack);
    }

    // With no mega texture there is nothing for the sources to be out of sync with.
    [Fact]
    public void IconsAwaitingRepack_IsEmptyWithoutAWorkspaceMegaTexture()
    {
        var catalog = new IconCatalog(null, Loose("I_BUTTON_LUKE"), Baseline());

        Assert.Empty(catalog.IconsAwaitingRepack);
    }

    [Fact]
    public void IconsAwaitingRepack_IsEmptyWhenEverythingIsPacked()
    {
        var catalog = new IconCatalog(Packed("I_BUTTON_LUKE.TGA"), Loose("I_BUTTON_LUKE"), Baseline());

        Assert.Empty(catalog.IconsAwaitingRepack);
    }

    [Fact]
    public void Resolve_EmptyWorkspaceMegaTexture_StillSuppressesBaseline()
    {
        var catalog = new IconCatalog(Packed(), Loose(), Baseline("I_BUTTON_LUKE.TGA"));

        Assert.Null(catalog.Resolve("I_BUTTON_LUKE.TGA"));
    }
}
