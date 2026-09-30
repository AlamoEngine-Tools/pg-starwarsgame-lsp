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

    /// <summary>
    ///     Whether <paramref name="path" /> carries one of the asset types this lookup accepts.
    ///     An empty list accepts anything, which is how a caller asks "does this exact path exist"
    ///     without naming a type.
    /// </summary>
    private static bool HasAllowedExtension(string path, IReadOnlyList<string> allowedExtensions)
    {
        if (allowedExtensions.Count == 0) return true;

        for (var i = 0; i < allowedExtensions.Count; i++)
            if (path.EndsWith(allowedExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
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

    /// <summary>
    ///     The reference as the CATALOG spells paths: forward slashes, no <c>./</c> anchor, no
    ///     leading separator.
    /// </summary>
    /// <remarks>
    ///     A reference may be written the way the ENGINE spells paths. Measured: vanilla writes
    ///     <c>./Data/Music/Credits.mp3</c>, EaWX writes 304 references as <c>Data\Audio\SFX\...</c>,
    ///     and the engine's own compiled-in paths are backslash (<c>.\Data\XML\...</c>) - it opens
    ///     files through the Win32 API, which takes either separator. The catalog is normalised to
    ///     lowercase forward-slash when it is built, so without this the two spellings never meet
    ///     and every such reference is reported as a missing file.
    /// </remarks>
    private static string Canonical(string reference)
    {
        var path = reference.Replace('\\', '/');
        if (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        return path.TrimStart('/');
    }

    private static bool Exists(
        IAssetFileIndex index, string normalised, IReadOnlyList<string> allowedExtensions)
    {
        var canonical = Canonical(normalised);

        // Exact relative-path match (e.g. "data/art/textures/foo.tga").
        if (index.Contains(canonical))
            return true;

        // Bare filename or partial path (e.g. "foo.tga", "textures/foo.tga"): match any catalog
        // entry of the right asset type whose path ends with "/<value>". The leading separator is
        // what keeps this anchored at a segment boundary - "oo.tga" must not satisfy "foo.tga".
        //
        // Candidates come from the filename bucket rather than from every path of the allowed
        // extensions. A reference's final segment IS the filename of anything that can match it,
        // so the bucket cannot exclude a true match, and the two tests below still decide - the
        // answer is identical, the search is not. Scanning instead made this quadratic: MEASURED,
        // one prop file spent 24.6s of an 83s workspace sweep here, because each of its models
        // names textures that each rescanned ~38,000 paths, worst of all when the texture really
        // was missing and every extension was scanned to no purpose.
        var fileName = MergedAssetFileIndex.FileNameOf(canonical);
        if (fileName.Length == 0)
            return false;

        var suffix = "/" + canonical;
        foreach (var path in index.GetByFileName(fileName))
        {
            if (!HasAllowedExtension(path, allowedExtensions))
                continue;

            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, canonical, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}