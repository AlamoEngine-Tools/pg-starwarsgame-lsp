// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Converts the raw icon sources a project keeps on disk into the PNGs the preview renders.
/// </summary>
/// <remarks>
///     Covers TGA, PNG and BMP. DDS is catalogued by <see cref="LooseIconCatalog" /> but not decoded
///     here - that format lives in a separate prerelease package we have not taken on - so it is
///     reported through <see cref="DecodeAll" />'s <c>unsupported</c> list rather than being silently
///     dropped, letting a caller say "unsupported icon source format" instead of "icon not found".
///     A file that is catalogued but unreadable or corrupt is treated the same way: one bad source
///     image must not cost the caller every other icon.
/// </remarks>
public static class LooseIconDecoder
{
    /// <summary>
    ///     Decodes everything decodable in <paramref name="catalog" />, keyed the same way.
    /// </summary>
    /// <param name="fileSystem">Filesystem the catalog's paths refer to.</param>
    /// <param name="catalog">Output of <see cref="LooseIconCatalog.Scan" />.</param>
    /// <param name="unsupported">
    ///     Names that were catalogued but could not be turned into a preview, either because of the
    ///     format or because the file would not decode.
    /// </param>
    public static IReadOnlyDictionary<string, byte[]> DecodeAll(
        IFileSystem fileSystem,
        IReadOnlyDictionary<string, LooseIcon> catalog,
        out IReadOnlyList<string> unsupported)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(catalog);

        var decoded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        foreach (var (name, icon) in catalog)
        {
            var png = TryDecode(fileSystem, icon);
            if (png is null)
                failures.Add(name);
            else
                decoded[name] = png;
        }

        unsupported = failures;
        return decoded;
    }

    /// <summary>
    ///     Reads and re-encodes one source image as PNG. Returns <see langword="null" /> when the
    ///     format is unsupported or the file does not decode.
    /// </summary>
    public static byte[]? TryDecode(IFileSystem fileSystem, LooseIcon icon)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(icon);

        if (!icon.IsDecodable)
            return null;

        try
        {
            // A PNG source is already the format the preview wants, so it passes through untouched -
            // decoding and re-encoding it would only lose time and risk changing the pixels. It is
            // still checked for a valid signature, because passthrough otherwise means a corrupt
            // file is never noticed here and instead surfaces as a broken image in the webview,
            // where there is no way to explain it.
            if (icon.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = fileSystem.File.ReadAllBytes(icon.FilePath);
                return HasPngSignature(bytes) ? bytes : null;
            }

            using var source = fileSystem.File.OpenRead(icon.FilePath);
            var surface = ImageSurface.Load(source);
            return PngWriter.Write(
                surface.Width, surface.Height, surface.Crop(0, 0, surface.Width, surface.Height));
        }
        catch
        {
            return null;
        }
    }

    private static bool HasPngSignature(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return bytes.Length > signature.Length && bytes[..signature.Length].SequenceEqual(signature);
    }
}
