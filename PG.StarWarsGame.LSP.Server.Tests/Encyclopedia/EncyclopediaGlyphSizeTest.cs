// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Encyclopedia;

namespace PG.StarWarsGame.LSP.Server.Tests.Encyclopedia;

/// <summary>
///     How large the popup's glyphs are drawn, which the game derives from the screen it is running
///     on rather than from any constant.
/// </summary>
/// <remarks>
///     This replaced a single fitted multiplier between point size and card units. That multiplier
///     could not be right at two resolutions at once, because the game's own arithmetic is not
///     linear in the point size - it truncates to an integer twice.
/// </remarks>
public sealed class EncyclopediaGlyphSizeTest
{
    private const double BodyPoints = 7d;

    /// <summary>
    ///     The two truncations are the whole point: the exact arithmetic gives 16.8 pixels at
    ///     1080p, the game gives 16, and the difference is 5% of every line's length.
    /// </summary>
    [Fact]
    public void Pixels_AtTenEightyP_TruncatesTwice()
    {
        // 1080 * 96 / 600 = 172.8 -> 172 dots per inch, then 172 * 7 / 72 = 16.72 -> 16 pixels.
        Assert.Equal(16, EncyclopediaGlyphSize.Pixels(BodyPoints, 1080));
    }

    [Fact]
    public void Pixels_AtTheDesignResolution_IsEleven()
    {
        // 768 * 96 / 600 = 122.88 -> 122, then 122 * 7 / 72 = 11.86 -> 11.
        Assert.Equal(11, EncyclopediaGlyphSize.Pixels(BodyPoints, 768));
    }

    /// <summary>
    ///     Card units, not pixels, are what the preview draws in: the card is 262 or 340 of them
    ///     wide. The conversion is the camera's own width over the screen's, which is the same
    ///     ratio the game uses to place every other piece of the popup.
    /// </summary>
    /// <summary>
    ///     The drawn size follows the UNROUNDED height: 172.8 dpi gives 16.8 px, not the 16 the
    ///     font object is built at. Rounding here left the body about 5% narrow against the game.
    /// </summary>
    [Fact]
    public void Units_AtTenEightyP_ConvertsThroughTheCameraWidth()
    {
        // 16.8 px * 1024 / 1920, then the measured drawn scale of 96/72.
        Assert.Equal(11.947d, EncyclopediaGlyphSize.Units(BodyPoints, 1d, 1920, 1080), 3);
    }

    /// <summary>
    ///     At the design resolution a pixel is a unit before the drawn scale, so this isolates that
    ///     scale on its own - again from the unrounded height, which is 11.947 rather than 11.
    /// </summary>
    [Fact]
    public void Units_AtTheDesignResolution_IsExactPixelsTimesTheDrawnScale()
    {
        Assert.Equal(122.88d * 7d / 72d * 96d / 72d,
            EncyclopediaGlyphSize.Units(BodyPoints, 1d, 1024, 768), 3);
    }

    /// <summary>
    ///     The rounding still belongs to the font the loader builds, which is a whole number of
    ///     pixels - it is only the LAYOUT that follows the exact value.
    /// </summary>
    [Fact]
    public void Pixels_StayRounded_EvenThoughUnitsDoNot()
    {
        Assert.Equal(16, EncyclopediaGlyphSize.Pixels(BodyPoints, 1080));
    }

    /// <summary>
    ///     The one number the in-game screenshot actually pins: how many glyph heights fit across
    ///     the card. Measured at 28.2 on EaWX's 340-unit card; anything near 38 is the bug this
    ///     replaced, where the body filled about two thirds of the width it should.
    /// </summary>
    [Fact]
    public void Units_PutTheMeasuredNumberOfGlyphHeightsAcrossTheCard()
    {
        const double eawxCardWidth = 340d;

        var across = eawxCardWidth / EncyclopediaGlyphSize.Units(BodyPoints, 1d, 1920, 1080);

        Assert.InRange(across, 28d, 30.5d);
    }

    /// <summary>
    ///     A component's <c>Scale</c> multiplies the drawn size, exactly as it does for every other
    ///     measured string in the command bar.
    /// </summary>
    [Fact]
    public void Units_AppliesTheComponentScale()
    {
        var plain = EncyclopediaGlyphSize.Units(BodyPoints, 1d, 1920, 1080);

        Assert.Equal(plain * 1.5d, EncyclopediaGlyphSize.Units(BodyPoints, 1.5d, 1920, 1080), 3);
    }

    /// <summary>
    ///     A nonsensical screen must not divide by zero or hand the card a glyph size of nothing.
    /// </summary>
    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, -1)]
    public void Units_NonsenseScreen_FallsBackToTheDesignResolution(int width, int height)
    {
        Assert.Equal(
            EncyclopediaGlyphSize.Units(BodyPoints, 1d, 1024, 768),
            EncyclopediaGlyphSize.Units(BodyPoints, 1d, width, height), 3);
    }

    /// <summary>
    ///     800x600 is the one resolution that also loses 5% of its wrap budget - the only place the
    ///     engine treats a screen size as anything but arithmetic.
    /// </summary>
    [Fact]
    public void WrapBudget_AtEightHundredBySixHundred_LosesFivePercent()
    {
        Assert.Equal(39, EncyclopediaGlyphSize.WrapBudget(41, 800, 600));
        Assert.Equal(53, EncyclopediaGlyphSize.WrapBudget(56, 800, 600));
    }

    [Fact]
    public void WrapBudget_AtEveryOtherResolution_IsUntouched()
    {
        Assert.Equal(41, EncyclopediaGlyphSize.WrapBudget(41, 1920, 1080));
        Assert.Equal(41, EncyclopediaGlyphSize.WrapBudget(41, 1024, 768));
        // Same width, different height: the rule is the whole screen, not one dimension.
        Assert.Equal(41, EncyclopediaGlyphSize.WrapBudget(41, 800, 480));
    }
}