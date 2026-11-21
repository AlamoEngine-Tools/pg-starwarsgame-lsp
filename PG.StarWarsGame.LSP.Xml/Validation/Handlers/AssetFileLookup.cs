// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Workspace;

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
    /// <param name="index">The asset catalog.</param>
    /// <param name="normalised">The referenced path, normalised the way the catalog keys are.</param>
    /// <param name="allowedExtensions">
    ///     The extensions a bare filename may be matched against, so a partial path is only matched
    ///     to catalog entries of the right asset type.
    /// </param>
    /// <param name="textureLookup">
    ///     Whether the name is looked up as the game looks up a texture (<see cref="GameTextureName.Candidates" />):
    ///     as written, then its first-dot stem as a <c>.dds</c>. False means the name as written only.
    /// </param>
    public static bool Resolves(
        IAssetFileIndex index, string normalised, IReadOnlyList<string> allowedExtensions,
        bool textureLookup)
    {
        return Resolve(index, normalised, allowedExtensions, textureLookup) is not null;
    }

    /// <summary>The catalog path <paramref name="name" /> resolves to by <paramref name="rule" />, or null.</summary>
    public static string? Resolve(IAssetFileIndex index, string name, AssetKindRule rule)
    {
        return Resolve(index, name, rule.AllowedExtensions, rule.TextureLookup);
    }

    /// <summary>
    ///     The catalog path <paramref name="normalised" /> resolves to, or null: the first of the names the
    ///     lookup tries that the catalog holds. See <see cref="Resolves" /> for the parameters.
    /// </summary>
    public static string? Resolve(
        IAssetFileIndex index, string normalised, IReadOnlyList<string> allowedExtensions, bool textureLookup)
    {
        foreach (var candidate in textureLookup ? GameTextureName.Candidates(normalised) : [normalised])
            if (Find(index, candidate, allowedExtensions) is { } found)
                return found;

        return null;
    }

    /// <summary>
    ///     For a missing texture named as a <c>.dds</c> whose <c>.tga</c> IS in the catalog, the sentence that
    ///     the game does not look for that <c>.tga</c> (#420, FR-5), with a leading space; empty otherwise.
    /// </summary>
    public static string UnsearchedTgaHint(
        IAssetFileIndex index, string name, IReadOnlyList<string> allowedExtensions, IMessageCatalog messages)
    {
        return GameTextureName.UnsearchedTga(name) is { } tga && Find(index, tga, allowedExtensions) is not null
            ? " " + messages.Format("AssetFileExistence_GameDoesNotLookForTga", tga)
            : string.Empty;
    }

    /// <summary>
    ///     The <c>.dds</c> the game never reaches for the texture <paramref name="name" />, or null: the catalog
    ///     path of the <c>.dds</c> of its stem, when <paramref name="found" /> is the name as written and that
    ///     <c>.dds</c> ships in a layer ABOVE it.
    /// </summary>
    /// <remarks>
    ///     Measured in the game (#420): the name as written is looked for through the whole file system - mod
    ///     folders, loose files, archives - before the <c>.dds</c> is tried anywhere. So a project's
    ///     <c>foo.dds</c> does not replace the <c>foo.tga</c> a lower layer or the game ships for a reference
    ///     that says <c>foo.tga</c>, where any other file of the higher layer would win. Only that case is
    ///     answered: in one layer the name as written is the obvious pick, and a <c>.dds</c> below it loses
    ///     as every lower file does.
    /// </remarks>
    public static string? UnreachedDds(
        IAssetFileIndex index, string name, string found, IReadOnlyList<string> allowedExtensions)
    {
        var candidates = GameTextureName.Candidates(name);
        if (candidates.Count < 2)
            return null;

        // A bare catalog name is an icon inside a mega texture, not a file the texture lookup opens: which
        // file a name finds is then not this rule's to say.
        if (!found.Contains('/'))
            return null;

        // Found by the fallback already: the .dds IS what the game opens.
        if (!string.Equals(MergedAssetFileIndex.FileNameOf(found),
                MergedAssetFileIndex.FileNameOf(Canonical(candidates[0])), StringComparison.OrdinalIgnoreCase))
            return null;

        return Find(index, candidates[1], allowedExtensions) is { } dds &&
               Precedence(index, dds) > Precedence(index, found)
            ? dds
            : null;
    }

    /// <summary>
    ///     Where a catalog path stands among the layers: the rank of its project, with the game's own files
    ///     below every project. A loose file no project declares stands with the lowest project.
    /// </summary>
    private static int Precedence(IAssetFileIndex index, string path)
    {
        return index.LayerRankOf(path) ?? (index.IsPackedAsset(path) ? -1 : 0);
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

    /// <summary>The names the lookup tries after the name as written: none unless it is a texture lookup.</summary>
    public static IEnumerable<string> AlternateNames(string normalised, bool textureLookup)
    {
        return textureLookup ? GameTextureName.Candidates(normalised).Skip(1) : [];
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
        var path = GamePaths.Forward(reference);
        if (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        return GamePaths.Normalize(path);
    }

    private static string? Find(
        IAssetFileIndex index, string normalised, IReadOnlyList<string> allowedExtensions)
    {
        var canonical = Canonical(normalised);

        // Exact relative-path match (e.g. "data/art/textures/foo.tga").
        if (index.Contains(canonical))
            return canonical;

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
            return null;

        var suffix = "/" + canonical;
        foreach (var path in index.GetByFileName(fileName))
        {
            if (!HasAllowedExtension(path, allowedExtensions))
                continue;

            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, canonical, StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return null;
    }
}