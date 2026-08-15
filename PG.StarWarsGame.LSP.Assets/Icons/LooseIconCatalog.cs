// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     A loose icon source file sitting on disk, outside any mega texture.
/// </summary>
/// <param name="Name">Base name without extension, as recorded in the catalog key.</param>
/// <param name="FilePath">Absolute path to the file.</param>
/// <param name="Extension">Lowercase extension including the leading dot.</param>
public sealed record LooseIcon(string Name, string FilePath, string Extension)
{
    /// <summary>
    ///     Whether this file is in a format we can actually turn into a preview.
    /// </summary>
    /// <remarks>
    ///     Covers TGA and DDS (both decoded) plus PNG (passed through as-is). BMP is catalogued but
    ///     NOT decodable - it was reachable through the previous imaging library and is not through
    ///     this one - so callers can still report "unsupported icon source format" rather than the
    ///     misleading "icon not found". DDS moved the other way: it used to be the unsupported case
    ///     and now works, since the decoder handles it natively.
    /// </remarks>
    public bool IsDecodable => LooseIconCatalog.DecodableExtensions.Contains(Extension);
}

/// <summary>
///     Indexes the raw icon files a mod keeps alongside its mega texture.
/// </summary>
/// <remarks>
///     <para>
///         Most mods drive a third-party MTD packer from a folder of source images and keep those
///         sources in the repository. The packed .mtd always wins when present - it is what the game
///         actually reads - but knowing what is in the source folder lets us tell "you never drew
///         this icon" apart from "you drew it but never repacked", which are very different problems
///         with very different fixes.
///     </para>
///     <para>
///         Entries are keyed by BASE NAME, without extension. A mega texture directory records every
///         entry with a <c>.TGA</c> suffix whatever the packer was fed, so a <c>foo.png</c> source
///         still has to line up with an <c>I_FOO.TGA</c> record.
///     </para>
/// </remarks>
public static class LooseIconCatalog
{
    internal static readonly ImmutableHashSet<string> DecodableExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".tga", ".png", ".dds");

    /// <summary>Extensions catalogued at all - decodable ones plus those we can only diagnose.</summary>
    private static readonly ImmutableHashSet<string> KnownExtensions =
        DecodableExtensions.Add(".bmp");

    /// <summary>
    ///     Recursively indexes every icon source under <paramref name="roots" />. Roots read as a
    ///     priority list: where two declare the same name, the earlier one wins. Roots that do not
    ///     exist are skipped rather than reported - a configured folder a modder has not created yet
    ///     is not an error.
    /// </summary>
    public static IReadOnlyDictionary<string, LooseIcon> Scan(IFileSystem fileSystem, IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(roots);

        var icons = new Dictionary<string, LooseIcon>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.Directory.Exists(root))
                continue;

            foreach (var file in fileSystem.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var extension = fileSystem.Path.GetExtension(file);
                if (!KnownExtensions.Contains(extension))
                    continue;

                var name = fileSystem.Path.GetFileNameWithoutExtension(file);
                if (name.Length == 0 || icons.ContainsKey(name))
                    continue;

                icons[name] = new LooseIcon(name, file, extension.ToLowerInvariant());
            }
        }

        return icons;
    }
}
