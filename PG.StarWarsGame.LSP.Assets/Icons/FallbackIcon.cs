// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     The placeholder drawn when an object names an icon but no layer could supply it.
/// </summary>
/// <remarks>
///     <para>
///         DRAWN HERE, NOT EXTRACTED. The game ships a perfectly good <c>I_BUTTON_UNKNOWN.TGA</c>,
///         and using it would mean committing Petroglyph artwork to a public MIT repository - which
///         is also why <c>eaw/</c> and <c>foc/</c> are gitignored. This is a few lines of geometry
///         instead, owned by this project and safe to redistribute.
///     </para>
///     <para>
///         Generated in code rather than committed as a binary resource. It cannot then fail to
///         load, go missing from a publish, or drift from the size the card expects - which matters
///         precisely because this is the thing that has to work when everything else has failed.
///     </para>
///     <para>
///         Deliberately does NOT look like a real icon: a hollow box with a diagonal cross reads as
///         "nothing here" at a glance, where a plausible-looking placeholder would leave an author
///         wondering why their unit had the wrong portrait.
///     </para>
///     <para>
///         OPAQUE, unlike a real icon. Mega-texture icons carry an alpha channel and the engine
///         composites them onto the popup, so the card must not paint anything behind them - but a
///         MISSING icon is not a real icon. The game's own fallback is a flat 50x50 square in a
///         garish blue, so a solid block here is what a modder would actually see in game, and the
///         loud colour does the same job: it is unmistakably a defect rather than artwork.
///     </para>
/// </remarks>
public static class FallbackIcon
{
    /// <summary>Matches the 50-unit box the card draws portraits into.</summary>
    private const int Size = 50;

    /// <summary>
    ///     Approximates the engine's missing-texture blue. Deliberately garish - it should never be
    ///     mistaken for art. Not sampled from the game, so the exact hue is ours.
    /// </summary>
    private static readonly byte[] Background = [38, 54, 214, 255];

    private static readonly byte[] Stroke = [228, 234, 255, 255];

    private static readonly Lazy<byte[]> LazyPng = new(Render, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>PNG bytes for the placeholder. Rendered once, then shared.</summary>
    public static byte[] Png => LazyPng.Value;

    private static byte[] Render()
    {
        var pixels = new byte[Size * Size * 4];
        for (var i = 0; i < Size * Size; i++)
            Background.CopyTo(pixels, i * 4);

        // Border.
        for (var i = 0; i < Size; i++)
        {
            Plot(pixels, i, 0);
            Plot(pixels, i, Size - 1);
            Plot(pixels, 0, i);
            Plot(pixels, Size - 1, i);
        }

        // Diagonal cross, inset so it reads as a mark inside the box rather than as its corners.
        const int inset = 12;
        for (var i = inset; i < Size - inset; i++)
        {
            PlotWide(pixels, i, i);
            PlotWide(pixels, i, Size - 1 - i);
        }

        return PngWriter.Write(Size, Size, pixels);
    }

    private static void Plot(byte[] pixels, int x, int y)
    {
        Stroke.CopyTo(pixels, (y * Size + x) * 4);
    }

    /// <summary>Sets a pixel and its right-hand neighbour, giving the diagonals visible weight.</summary>
    private static void PlotWide(byte[] pixels, int x, int y)
    {
        Plot(pixels, x, y);
        if (x + 1 < Size)
            Plot(pixels, x + 1, y);
    }
}
