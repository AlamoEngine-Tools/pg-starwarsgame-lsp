// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

public sealed class FallbackIconTest
{
    [Fact]
    public void Png_IsAValidImageMatchingThePortraitBox()
    {
        var image = TestPng.Decode(FallbackIcon.Png);

        Assert.Equal(50, image.Width);
        Assert.Equal(50, image.Height);
    }

    /// <summary>
    ///     Opaque, unlike a real icon. Real portraits are alpha-blended onto the popup and the card
    ///     paints nothing behind them - but the game's own missing-texture fallback is a flat blue
    ///     square, so a solid block is what a modder actually sees when an icon fails to resolve.
    /// </summary>
    [Fact]
    public void Png_IsOpaque()
    {
        var image = TestPng.Decode(FallbackIcon.Png);

        Assert.Equal(255, image[25, 5].A);
        Assert.Equal(255, image[0, 0].A);
        Assert.Equal(255, image[25, 25].A);
    }

    // Loud enough that it never reads as artwork: strongly blue, and not a grey.
    [Fact]
    public void Png_UsesAnUnmistakableBlue()
    {
        var image = TestPng.Decode(FallbackIcon.Png);

        var fill = image[25, 5];
        Assert.True(fill.B > 150, $"expected a strong blue channel, got {fill}");
        Assert.True(fill.B > fill.R + 80 && fill.B > fill.G + 80, $"expected blue to dominate, got {fill}");
    }

    [Fact]
    public void Png_DrawsABorderAndAMark()
    {
        var image = TestPng.Decode(FallbackIcon.Png);

        var fill = image[25, 5];
        Assert.NotEqual(fill, image[0, 0]);   // border
        Assert.NotEqual(fill, image[25, 25]); // the diagonals cross at the centre
    }

    // Rendered once and shared; callers must not be handed a buffer that changes under them.
    [Fact]
    public void Png_IsStableAcrossCalls()
    {
        Assert.Same(FallbackIcon.Png, FallbackIcon.Png);
    }
}

