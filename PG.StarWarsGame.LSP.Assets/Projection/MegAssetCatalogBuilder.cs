// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

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
            // `.ala` earns its place by being ENUMERABLE rather than by being resolvable: a model
            // preview has to ask which clips exist for a skeleton, and the animations sit beside
            // their model under a name prefix with nothing else to discover them by.
            // Scripts are catalogued so a game object's `Lua_Script` resolves the way the game loads
            // it - vanilla ships them packed, so a mod's reference to one has nothing loose to find.
            ".tga", ".dds", ".alo", ".ala", ".wav", ".mp3", ".ted", ".lua", ".lc");

    /// <summary>Every extension the catalog records, for reporting counts per extension.</summary>
    public static IReadOnlyCollection<string> KnownAssetExtensions => AssetExtensions;

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
    /// <param name="extractTextures">
    ///     Reads what an open <c>.alo</c> is and the texture names it carries inside itself. When
    ///     <see langword="null" />, no texture catalog, particle-system set or proxy catalog is built, and the
    ///     validator falls back to parsing each model on demand - which is what it did before, and
    ///     what MEASURED as the entire cost of a workspace diagnostics sweep.
    ///     <para>
    ///         Opens the entry a SECOND time rather than sharing the bone pass's stream. That is
    ///         deliberate: the bone path hands the ALO loader the original entry stream because the
    ///         loader resolves the model's path from it, and a <c>MemoryStream</c> throws - a trap
    ///         that once turned into a silently empty baseline. Threading a second reader through
    ///         that sequence buys one open per model in an OFFLINE build and risks the same
    ///         silence; textures come straight off the bytes and need none of it.
    ///     </para>
    /// </param>
    public static (
        ImmutableHashSet<string> assetFiles,
        ImmutableDictionary<string, ImmutableArray<string>> modelBones,
        ImmutableDictionary<string, ImmutableArray<string>> modelTextures,
        ImmutableHashSet<string> particleSystems,
        ImmutableDictionary<string, ImmutableArray<string>> modelProxies
        ) Build(
            IEnumerable<(string megName, IEnumerable<string> entryPaths)> megEntries,
            IFileSystem looseFileSystem,
            string gameRootPath,
            Func<string, Stream?> openEntry,
            Func<Stream, IReadOnlyList<string>> extractBones,
            Func<Stream, IEnumerable<string>>? extractMtdIcons,
            ILogger logger,
            Func<Stream, AloScan>? extractTextures = null)
    {
        // Track source MEG for each normalised path - used for collision detection.
        var pathSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assetBuilder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var bonesBuilder = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        var texturesBuilder = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        var particleBuilder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var proxiesBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (megName, entryPaths) in megEntries)
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

            if (!ext.Equals(".alo", StringComparison.OrdinalIgnoreCase)) continue;

            TryExtractBones(normalized, openEntry, extractBones, bonesBuilder);
            if (extractTextures is not null)
                TryExtractTextures(normalized, openEntry, extractTextures, texturesBuilder, particleBuilder,
                    proxiesBuilder);
        }

        // Merge loose files on top (workspace loose files may extend the MEG catalog).
        var looseFiles = AssetFileEnumerator.Enumerate(looseFileSystem, gameRootPath);
        foreach (var path in looseFiles)
            assetBuilder.Add(path);

        if (extractMtdIcons is not null)
            AddLooseMtdIconNames(looseFileSystem, gameRootPath, extractMtdIcons, assetBuilder);

        return (assetBuilder.ToImmutable(),
            bonesBuilder.ToImmutableDictionary(
                kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            texturesBuilder.ToImmutableDictionary(
                kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            particleBuilder.ToImmutable(),
            proxiesBuilder.ToImmutable());
    }

    /// <summary>
    ///     Normalises a raw MEG entry path to lowercase forward-slash with no leading separator.
    ///     e.g. <c>DATA\ART\TEXTURES\FOO.TGA</c> -> <c>data/art/textures/foo.tga</c>.
    /// </summary>
    public static string NormalizeMegPath(string rawPath)
    {
        return GamePaths.Key(rawPath);
    }

    /// <summary>
    ///     Applies SFX packaging conventions to an already-normalised MEG entry path: a flat path
    ///     (no directory) from an SFX MEG is prefixed with <c>data/audio/sfx/</c>, because the MEG
    ///     itself IS that directory.
    /// </summary>
    /// <remarks>
    ///     The entry name is otherwise kept verbatim, language suffix included. This used to strip a
    ///     <c>_eng</c> stem suffix from <c>.wav</c> and <c>.mp3</c> entries on the premise that XML
    ///     names sounds without one, and the premise was false on every side: the localised MEG
    ///     spells 6825 of its 6835 audio entries <c>I000_EHD0101_ENG.WAV</c>, the loose install
    ///     spells the same line <c>Data/Audio/Speech/English/C000_emp0101_eng.mp3</c>, and the XML
    ///     asks for <c>U000_SPD0101_ENG.wav</c>. Folding the suffix away in the catalog alone left
    ///     every localised reference in the game unresolvable - 7480 warnings over one message.
    /// </remarks>
    public static string ApplySfxConventions(string normalizedPath, string megName)
    {
        return !normalizedPath.Contains('/') &&
               megName.Contains("sfx", StringComparison.OrdinalIgnoreCase)
            ? "data/audio/sfx/" + normalizedPath
            : normalizedPath;
    }

    /// <summary>Returns true when the file extension belongs to the tracked asset categories.</summary>
    public static bool IsAssetExtension(string extension)
    {
        return AssetExtensions.Contains(extension);
    }

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
        catch
        {
            /* skip corrupt/unreadable MTD entries */
        }
    }

    /// <summary>Icon names from loose .mtd files (mega-texture atlases not packed into MEGs).</summary>
    private static void AddLooseMtdIconNames(
        IFileSystem looseFileSystem,
        string gameRootPath,
        Func<Stream, IEnumerable<string>> extractIcons,
        ImmutableHashSet<string>.Builder assetBuilder)
    {
        var root = looseFileSystem.Path.GetFullPath(gameRootPath);
        if (!looseFileSystem.Directory.Exists(root)) return;

        foreach (var mtdFile in looseFileSystem.Directory.EnumerateFiles(root, "*.mtd", SearchOption.AllDirectories))
        {
            var normalizedRel = NormalizeMegPath(looseFileSystem.Path.GetRelativePath(root, mtdFile));
            if (!normalizedRel.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                using var stream = looseFileSystem.File.OpenRead(mtdFile);
                AddMtdIconNames(stream, extractIcons, assetBuilder);
            }
            catch
            {
                /* skip corrupt/unreadable loose MTD files */
            }
        }
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

    /// <summary>
    ///     Records the textures a model names inside itself. Unlike the bone pass, an EMPTY list is
    ///     kept: "this model names no textures" is a real answer, and it is what stops the
    ///     validator parsing the file again on every reference to it.
    /// </summary>
    private static void TryExtractTextures(
        string normalizedPath,
        Func<string, Stream?> openEntry,
        Func<Stream, AloScan> extractTextures,
        Dictionary<string, ImmutableArray<string>> texturesBuilder,
        ImmutableHashSet<string>.Builder particleBuilder,
        ImmutableDictionary<string, ImmutableArray<string>>.Builder proxiesBuilder)
    {
        try
        {
            using var stream = openEntry(normalizedPath);
            if (stream is null) return;
            // Keyed by bare filename, like the bones: XML references models by name and the engine
            // resolves them by name across the VFS.
            var key = ModelBoneKey.From(normalizedPath);
            var scan = extractTextures(stream);
            texturesBuilder[key] = [.. scan.Textures];
            if (scan.Kind == AloFileKind.Particle) particleBuilder.Add(key);
            else proxiesBuilder[key] = [.. scan.Proxies];
        }
        catch
        {
            // Corrupt or unsupported files must not abort the build.
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
                // Keyed by bare filename: XML references models by name and the engine resolves them
                // by name across the VFS. Later archives override earlier ones at the same filename.
                bonesBuilder[ModelBoneKey.From(normalizedPath)] = [.. bones];
        }
        catch
        {
            // Silently skip models that fail to load - corrupt/unsupported files should not abort the build.
        }
    }
}