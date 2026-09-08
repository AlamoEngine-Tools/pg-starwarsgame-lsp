// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Whether an asset name resolves against the merged catalog, the way the engine resolves it.
/// </summary>
/// <remarks>
///     Extracted from <see cref="AssetFileExistenceHandlerBase" /> so the handler that checks the
///     textures a MODEL names inside itself answers the question exactly the same way. Two copies of
///     these rules would drift, and the failure would be silent: a texture reported missing by one
///     handler and found by the other.
/// </remarks>
public static class AssetFileLookup
{
    /// <summary>
    ///     True when <paramref name="normalised" /> names something in the catalog.
    /// </summary>
    /// <param name="allowedExtensions">
    ///     The extensions a bare filename may be matched against, so a partial path is only matched
    ///     to catalog entries of the right asset type.
    /// </param>
    /// <param name="interchangeable">
    ///     Extensions the engine treats as ONE asset - a .tga reference is satisfied by the .dds and
    ///     the other way round. Empty means exact-extension matching only.
    /// </param>
    public static bool Resolves(
        IAssetFileIndex index, string normalised, IReadOnlyList<string> allowedExtensions,
        IReadOnlyList<string> interchangeable)
    {
        if (Exists(index, normalised, allowedExtensions))
            return true;

        return AlternateNames(normalised, interchangeable)
            .Any(alternate => Exists(index, alternate, allowedExtensions));
    }

    /// <summary>The same name with each other interchangeable extension.</summary>
    public static IEnumerable<string> AlternateNames(
        string normalised, IReadOnlyList<string> interchangeable)
    {
        var ext = Path.GetExtension(normalised);
        if (string.IsNullOrEmpty(ext) ||
            !interchangeable.Contains(ext, StringComparer.OrdinalIgnoreCase))
            yield break;

        foreach (var other in interchangeable)
            if (!other.Equals(ext, StringComparison.OrdinalIgnoreCase))
                yield return normalised[..^ext.Length] + other;
    }

    private static bool Exists(
        IAssetFileIndex index, string normalised, IReadOnlyList<string> allowedExtensions)
    {
        // Exact relative-path match (e.g. "data/art/textures/foo.tga").
        if (index.Contains(normalised))
            return true;

        // Bare filename or partial path (e.g. "foo.tga"): match any catalog entry of the right
        // asset type whose path ends with "/<value>".
        var suffix = "/" + normalised;
        foreach (var ext in allowedExtensions)
        foreach (var path in index.GetByExtension(ext))
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, normalised, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}
