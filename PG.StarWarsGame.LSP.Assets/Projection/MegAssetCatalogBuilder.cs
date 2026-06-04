// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Builds <see cref="Core.Symbols.BaselineIndex.AssetFiles" /> and
///     <see cref="Core.Symbols.BaselineIndex.ModelBones" /> from the game's MEG archives and loose files.
/// </summary>
/// <remarks>
///     MEG entry paths come as archive-root-relative strings (typically uppercase Windows paths such as
///     <c>DATA\ART\TEXTURES\FOO.TGA</c>) and are normalised to lowercase forward-slash before storage.
///     When the same normalised path appears in multiple MEG files the last-loaded entry wins (matching
///     engine VFS layering semantics); each collision is logged at Warning level.
/// </remarks>
public static class MegAssetCatalogBuilder
{
    private static readonly ImmutableHashSet<string> AssetExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            ".tga", ".dds", ".alo", ".wav", ".mp3", ".ted");

    /// <summary>
    ///     Builds the combined asset catalog.
    /// </summary>
    /// <param name="megEntries">
    ///     Pre-loaded MEG archive data: each tuple is a MEG display-name (for log messages) paired with
    ///     the raw entry paths from that archive, in load order (later archives override earlier ones).
    /// </param>
    /// <param name="looseFileSystem">Filesystem used to enumerate loose game files.</param>
    /// <param name="gameRootPath">Root of the game installation; passed to <see cref="AssetFileEnumerator" />.</param>
    /// <param name="openEntry">
    ///     Callback that opens an asset file by its <em>normalised</em> path for streaming (used for
    ///     bone extraction). Returns <see langword="null" /> when the file is unavailable.
    /// </param>
    /// <param name="extractBones">Extracts bone names from an open model stream.</param>
    /// <param name="extractMtdIcons">
    ///     Extracts icon filenames from an open <c>.mtd</c> stream. When <see langword="null" />,
    ///     MTD files are skipped and no icon names are added from mega-texture atlases.
    /// </param>
    /// <param name="logger">Logger for collision warnings.</param>
    public static (
        ImmutableHashSet<string> assetFiles,
        ImmutableDictionary<string, ImmutableArray<string>> modelBones
    ) Build(
        IEnumerable<(string megName, IEnumerable<string> entryPaths)> megEntries,
        IFileSystem looseFileSystem,
        string gameRootPath,
        Func<string, Stream?> openEntry,
        Func<Stream, IReadOnlyList<string>> extractBones,
        Func<Stream, IEnumerable<string>>? extractMtdIcons,
        ILogger logger)
    {
        // Track source MEG for each normalised path — used for collision detection.
        var pathSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assetBuilder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var bonesBuilder = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (megName, entryPaths) in megEntries)
        {
            foreach (var rawPath in entryPaths)
            {
                var normalized = ApplySfxConventions(NormalizeMegPath(rawPath), megName);
                var ext = Path.GetExtension(normalized);

                if (ext.Equals(".mtd", StringComparison.OrdinalIgnoreCase))
                {
                    if (extractMtdIcons is not null)
                        TryExtractMtdIcons(normalized, openEntry, extractMtdIcons, assetBuilder);
                    continue;
                }

                if (!IsAssetExtension(ext)) continue;

                if (pathSource.TryGetValue(normalized, out var prior))
                    logger.LogWarning(
                        "Path collision: {Path}\n  First seen in:  {Prior}\n  Overridden by:  {Current}",
                        normalized, prior, megName);

                pathSource[normalized] = megName;
                assetBuilder.Add(normalized);

                if (ext.Equals(".alo", StringComparison.OrdinalIgnoreCase))
                    TryExtractBones(normalized, openEntry, extractBones, bonesBuilder);
            }
        }

        // Merge loose files on top (workspace loose files may extend the MEG catalog).
        var looseFiles = AssetFileEnumerator.Enumerate(looseFileSystem, gameRootPath);
        foreach (var path in looseFiles)
            assetBuilder.Add(path);

        // Extract icon names from loose .mtd files (mega-texture atlases not packed into MEGs).
        if (extractMtdIcons is not null)
        {
            var root = looseFileSystem.Path.GetFullPath(gameRootPath);
            if (looseFileSystem.Directory.Exists(root))
            {
                foreach (var mtdFile in looseFileSystem.Directory.EnumerateFiles(
                    root, "*.mtd", SearchOption.AllDirectories))
                {
                    var rel = looseFileSystem.Path.GetRelativePath(root, mtdFile);
                    var normalizedRel = NormalizeMegPath(rel);
                    if (!normalizedRel.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        using var stream = looseFileSystem.File.OpenRead(mtdFile);
                        AddMtdIconNames(stream, extractMtdIcons, assetBuilder);
                    }
                    catch { /* skip corrupt/unreadable loose MTD files */ }
                }
            }
        }

        return (assetBuilder.ToImmutable(),
            bonesBuilder.ToImmutableDictionary(
                kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Normalises a raw MEG entry path to lowercase forward-slash with no leading separator.
    ///     e.g. <c>DATA\ART\TEXTURES\FOO.TGA</c> → <c>data/art/textures/foo.tga</c>.
    /// </summary>
    public static string NormalizeMegPath(string rawPath) =>
        rawPath.Replace('\\', '/').ToLowerInvariant().TrimStart('/');

    /// <summary>
    ///     Applies SFX packaging conventions to an already-normalised MEG entry path.
    ///     <list type="bullet">
    ///         <item>Flat paths (no directory) from SFX MEGs are prefixed with <c>data/audio/sfx/</c>.</item>
    ///         <item><c>_eng</c> stem suffix is stripped from <c>.wav</c> and <c>.mp3</c> files
    ///         (localized audio; XML references the base name without the language suffix).</item>
    ///     </list>
    /// </summary>
    public static string ApplySfxConventions(string normalizedPath, string megName)
    {
        // Flat path from an SFX MEG → the MEG itself is the DATA/AUDIO/SFX directory.
        if (!normalizedPath.Contains('/') &&
            megName.Contains("sfx", StringComparison.OrdinalIgnoreCase))
            normalizedPath = "data/audio/sfx/" + normalizedPath;

        // Strip _eng suffix from audio file stems — XML references sounds without language suffix.
        var ext = Path.GetExtension(normalizedPath);
        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(normalizedPath);
            if (stem.EndsWith("_eng", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
                var newStem = stem[..^4];
                normalizedPath = string.IsNullOrEmpty(dir)
                    ? newStem + ext
                    : $"{dir}/{newStem}{ext}";
            }
        }

        return normalizedPath;
    }

    /// <summary>Returns true when the file extension belongs to the tracked asset categories.</summary>
    public static bool IsAssetExtension(string extension) =>
        AssetExtensions.Contains(extension);

    private static void TryExtractMtdIcons(
        string normalizedMtdPath,
        Func<string, Stream?> openEntry,
        Func<Stream, IEnumerable<string>> extractIcons,
        ImmutableHashSet<string>.Builder assetBuilder)
    {
        try
        {
            using var stream = openEntry(normalizedMtdPath);
            if (stream is null) return;
            AddMtdIconNames(stream, extractIcons, assetBuilder);
        }
        catch { /* skip corrupt/unreadable MTD entries */ }
    }

    private static void AddMtdIconNames(
        Stream stream,
        Func<Stream, IEnumerable<string>> extractIcons,
        ImmutableHashSet<string>.Builder assetBuilder)
    {
        foreach (var name in extractIcons(stream))
        {
            var normalized = name.ToLowerInvariant().Trim();
            if (!string.IsNullOrEmpty(normalized))
                assetBuilder.Add(normalized);
        }
    }

    private static void TryExtractBones(
        string normalizedPath,
        Func<string, Stream?> openEntry,
        Func<Stream, IReadOnlyList<string>> extractBones,
        Dictionary<string, ImmutableArray<string>> bonesBuilder)
    {
        try
        {
            using var stream = openEntry(normalizedPath);
            if (stream is null) return;
            var bones = extractBones(stream);
            if (bones.Count > 0)
                bonesBuilder[normalizedPath] = bones.ToImmutableArray();
        }
        catch
        {
            // Silently skip models that fail to load — corrupt/unsupported files should not abort the build.
        }
    }
}
