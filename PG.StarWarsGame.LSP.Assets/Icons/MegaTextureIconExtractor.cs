// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Pfim;
using PG.StarWarsGame.Files.MTD.Data;
using DrawingRectangle = System.Drawing.Rectangle;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Cuts every icon out of a Petroglyph mega texture and returns them as PNGs, keyed by the name
///     the mega texture directory (.mtd) records for each one.
/// </summary>
/// <remarks>
///     <para>
///         The game packs its whole UI icon set into one large texture - 2048x1024 for EaW,
///         2048x2048 for FoC - and the accompanying .mtd is the directory of rectangles within it.
///         That was a 2006 memory-budget decision; for editor purposes we simply reverse it.
///     </para>
///     <para>
///         MTD coordinates are measured from the TOP left, while the shipped mega textures store
///         their rows bottom-up (descriptor byte <c>0x00</c>). The decoder is expected to normalise
///         that; nothing here flips anything, and the real-data test is what proves it, by comparing
///         against an independent byte-level read of the same file.
///     </para>
///     <para>
///         THE DESCRIPTOR ALSO DECLARES ZERO ALPHA BITS despite the file being 32bpp, and that
///         detail has already broken this pipeline once: a decoder that believes the header returns
///         every icon fully opaque and the card fills with black boxes. The alpha is taken from the
///         fourth byte regardless, which is what the engine itself evidently does.
///     </para>
///     <para>
///         A single malformed record never aborts the run. Third-party directories are frequently
///         hand-built or stale, and one bad rectangle must not cost the caller every other icon -
///         the same contract the other asset readers in this project follow.
///     </para>
/// </remarks>
public static class MegaTextureIconExtractor
{
    /// <summary>
    ///     Extracts every entry of <paramref name="directory" /> from <paramref name="megaTexture" />.
    ///     Records whose rectangle is empty or reaches outside the texture are skipped.
    /// </summary>
    /// <param name="directory">The parsed mega texture directory.</param>
    /// <param name="megaTexture">The mega texture itself, positioned at its start.</param>
    /// <returns>PNG-encoded icons keyed by directory name, compared case-insensitively.</returns>
    public static IReadOnlyDictionary<string, byte[]> ExtractAll(
        IMegaTextureDirectory directory,
        Stream megaTexture)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(megaTexture);

        var texture = ImageSurface.Load(megaTexture);
        var icons = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in directory)
        {
            if (!IsWithin(entry.Area, texture.Width, texture.Height))
                continue;

            // Last one wins, mirroring the directory's own LastEntryWithCrc lookup: a mega texture
            // may legally carry the same name twice and the engine resolves to the final record.
            icons[entry.FileName] = Encode(texture, entry.Area);
        }

        return icons;
    }

    private static byte[] Encode(ImageSurface texture, DrawingRectangle area)
    {
        var pixels = texture.Crop(area.X, area.Y, area.Width, area.Height);
        return PngWriter.Write(area.Width, area.Height, pixels);
    }

    private static bool IsWithin(DrawingRectangle area, int width, int height)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return false;
        if (area.X < 0 || area.Y < 0)
            return false;

        return area.X + area.Width <= width && area.Y + area.Height <= height;
    }
}

/// <summary>
///     A decoded image as top-down 8-bit RGBA, whatever the source format was.
/// </summary>
/// <remarks>
///     Wraps Pfim, which decodes TGA and DDS but hands back the source layout - bottom-up rows,
///     BGR(A) channel order, and varying bit depths. Normalising once here keeps that awkwardness
///     out of every caller.
/// </remarks>
public sealed class ImageSurface
{
    private readonly byte[] _rgba;

    private ImageSurface(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        _rgba = rgba;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Decodes TGA or DDS from <paramref name="source" />.</summary>
    /// <exception cref="NotSupportedException">The pixel format is one we do not convert.</exception>
    public static ImageSurface Load(Stream source)
    {
        using var image = Pfimage.FromStream(source);
        return new ImageSurface(image.Width, image.Height, ToRgba(image));
    }

    /// <summary>The RGBA bytes of a sub-rectangle, row-major and top row first.</summary>
    public byte[] Crop(int x, int y, int width, int height)
    {
        var cropped = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            var source = ((y + row) * Width + x) * 4;
            Array.Copy(_rgba, source, cropped, row * width * 4, width * 4);
        }

        return cropped;
    }

    private static byte[] ToRgba(IImage image)
    {
        var rgba = new byte[image.Width * image.Height * 4];
        var data = image.Data;

        var bytesPerPixel = image.Format switch
        {
            ImageFormat.Rgba32 => 4,
            ImageFormat.Rgb24 => 3,
            _ => throw new NotSupportedException(
                $"Unsupported icon pixel format '{image.Format}'. Icons must be 24- or 32-bit.")
        };

        for (var y = 0; y < image.Height; y++)
        {
            // Pfim reports the stride it actually produced, which is not always width * bpp.
            var sourceRow = y * image.Stride;
            var targetRow = y * image.Width * 4;

            for (var x = 0; x < image.Width; x++)
            {
                var s = sourceRow + x * bytesPerPixel;
                var t = targetRow + x * 4;

                // Source channels are BGR(A).
                rgba[t] = data[s + 2];
                rgba[t + 1] = data[s + 1];
                rgba[t + 2] = data[s];

                // Alpha from the fourth byte when there is one. NEVER from the TGA descriptor's
                // declared alpha-bit count: the shipped mega textures claim zero alpha bits while
                // carrying a real alpha channel, and honouring that claim renders every icon opaque.
                rgba[t + 3] = bytesPerPixel == 4 ? data[s + 3] : (byte)255;
            }
        }

        return rgba;
    }
}
