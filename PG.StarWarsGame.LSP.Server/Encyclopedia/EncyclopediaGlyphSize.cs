// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>
///     How large the popup's glyphs are drawn, and how wide its lines may run, for a given screen.
/// </summary>
/// <remarks>
///     <para>
///         The game sizes this text from the screen it is running on. It builds a real font at a
///         pixel height derived from the screen's HEIGHT against a 600-line reference, then converts
///         to the card's own units through the camera width over the screen's WIDTH - the same ratio
///         it uses to place every other piece of the popup.
///     </para>
///     <para>
///         Both steps truncate to an integer, and that is not a rounding detail: at 1920x1080 the
///         exact arithmetic gives 16.8 pixels and the game gives 16, a 5% difference in the length
///         of every line. This replaced a single fitted multiplier between point size and card
///         units, which could not be correct at two resolutions at once because the real
///         relationship is not linear in the point size.
///     </para>
/// </remarks>
public static class EncyclopediaGlyphSize
{
    /// <summary>The screen the preview assumes when nothing says otherwise.</summary>
    public const int DefaultScreenWidth = 1920;

    public const int DefaultScreenHeight = 1080;

    /// <summary>
    ///     The width of the camera the command bar is drawn through, in card units. Card geometry
    ///     is expressed against this, which is why a full screen is this many units across whatever
    ///     the display actually measures.
    /// </summary>
    private const double CameraWidth = 1024d;

    /// <summary>The screen height the dots-per-inch figure is referenced to.</summary>
    private const double ReferenceScreenHeight = 600d;

    private const double ReferenceDpi = 96d;

    private const double PointsPerInch = 72d;

    /// <summary>
    ///     The factor between the glyph height the font is built at and the height it is DRAWN at,
    ///     in card units.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         MEASURED, not derived, and the distinction matters. Fitting thirteen known body lines
    ///         off an in-game screenshot gives a glyph height of 16.0 px with a per-character extra
    ///         of -0.03 px - so no letter spacing, and Arial's own metrics to within 0.6%, which is
    ///         also what confirms the face. Against the card's measured width that puts the card at
    ///         28.2 glyph heights across, where converting pixels straight through the camera ratio
    ///         predicts 37.9.
    ///     </para>
    ///     <para>
    ///         The ratio between those, 1.344, is <see cref="ReferenceDpi" /> over
    ///         <see cref="PointsPerInch" /> to within the measurement's own error, and applying it
    ///         predicts 28.45 against the measured 28.2. Both numbers are scale-free and, at a fixed
    ///         aspect ratio, resolution-free, so neither depends on knowing which screen the
    ///         screenshot came from or whether it was resized.
    ///     </para>
    ///     <para>
    ///         WHAT, not why: the arithmetic matches but the mechanism is not confirmed in the
    ///         engine. Re-fit against a fresh screenshot before trusting it somewhere new.
    ///     </para>
    /// </remarks>
    private const double DrawnScale = ReferenceDpi / PointsPerInch;

    /// <summary>The design resolution, and the fallback for a screen that makes no sense.</summary>
    private const int DesignWidth = 1024;

    private const int DesignHeight = 768;

    /// <summary>
    ///     The glyph height in whole pixels, as the FONT IS BUILT - the integer height handed to the
    ///     font loader.
    /// </summary>
    /// <remarks>
    ///     Not what the card should draw at: see <see cref="Units" />. Measuring the game's own
    ///     output shows the drawn text following the unrounded height, so the rounding here belongs
    ///     to the font object and not to the layout.
    /// </remarks>
    public static int Pixels(double pointSize, int screenHeight)
    {
        return (int)ExactPixels(pointSize, screenHeight);
    }

    /// <summary>
    ///     The same height before either rounding, which is what the drawn glyphs follow.
    /// </summary>
    /// <remarks>
    ///     Using the rounded height left the body about 5% narrow: at 1920x1080 it gives 16 where
    ///     the exact arithmetic gives 16.8, and the game's own divider measures 321.8 units wide
    ///     against the 308 the rounded value produces. The unrounded one predicts 323.
    /// </remarks>
    private static double ExactPixels(double pointSize, int screenHeight)
    {
        if (screenHeight <= 0) screenHeight = DesignHeight;

        var dpi = screenHeight * ReferenceDpi / ReferenceScreenHeight;
        return dpi * pointSize / PointsPerInch;
    }

    /// <summary>
    ///     The glyph height in CARD units - the same units the card's width is given in, so a client
    ///     may draw at any zoom and keep the proportions.
    /// </summary>
    /// <param name="pointSize">The component's <c>Font_Point_Size</c>.</param>
    /// <param name="scale">
    ///     The component's own <c>Scale</c> tag, which multiplies the drawn size exactly as it does
    ///     for every other measured string in the command bar.
    /// </param>
    /// <param name="screenWidth">Target screen width in pixels.</param>
    /// <param name="screenHeight">Target screen height in pixels.</param>
    public static double Units(double pointSize, double scale, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            screenWidth = DesignWidth;
            screenHeight = DesignHeight;
        }

        return ExactPixels(pointSize, screenHeight) * (CameraWidth / screenWidth) * scale * DrawnScale;
    }

    /// <summary>
    ///     The line's character budget for this screen.
    /// </summary>
    /// <remarks>
    ///     800x600 - and only that exact pair - loses 5% of its budget. It is the one place the
    ///     engine treats a screen size as a special case rather than as arithmetic, so it is
    ///     reproduced as the special case it is rather than generalised into a formula.
    /// </remarks>
    public static int WrapBudget(int declaredBudget, int screenWidth, int screenHeight)
    {
        if (screenWidth != 800 || screenHeight != 600) return declaredBudget;

        return (int)Math.Round(declaredBudget * 0.95d, MidpointRounding.AwayFromZero);
    }
}