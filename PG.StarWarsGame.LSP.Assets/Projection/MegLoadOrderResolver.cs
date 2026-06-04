// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Resolves the correct MEG archive load order for a game installation.
/// </summary>
/// <remarks>
///     Load order:
///     <list type="number">
///         <item>All non-patch MEGs, sorted case-insensitively by full path.</item>
///         <item><c>patch.meg</c> (if present).</item>
///         <item><c>patch2.meg</c> (if present).</item>
///         <item><c>64patch.meg</c> (if present).</item>
///     </list>
///     MEGs under <c>data/audio/sfx/</c> are language-filtered before sorting:
///     <c>_non_localized</c> variants are always included; language variants are collapsed to
///     English when multiple languages are present, or to the sole variant if only one exists.
/// </remarks>
public static class MegLoadOrderResolver
{
    private static readonly string[] PatchOrder = ["patch.meg", "patch2.meg", "64patch.meg"];

    /// <summary>
    ///     Returns <paramref name="megFilePaths" /> ordered for correct engine VFS layering.
    /// </summary>
    /// <param name="megFilePaths">All discovered <c>.meg</c> file paths for a single game installation.</param>
    /// <param name="gameRootPath">Root directory of the game installation (used to identify the SFX subdirectory).</param>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> megFilePaths, string gameRootPath)
    {
        var patchSlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sfxMegs = new List<string>();
        var baseMegs = new List<string>();

        var normalizedRoot = gameRootPath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

        foreach (var path in megFilePaths)
        {
            var fileName = Path.GetFileName(path);

            if (Array.Exists(PatchOrder, p => p.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            {
                patchSlots[fileName] = path;
                continue;
            }

            var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
            var relativePath = normalizedPath.Length > normalizedRoot.Length
                ? normalizedPath[(normalizedRoot.Length + 1)..]
                : normalizedPath;

            if (relativePath.StartsWith("data/audio/sfx/", StringComparison.OrdinalIgnoreCase))
                sfxMegs.Add(path);
            else
                baseMegs.Add(path);
        }

        var sortedBase = baseMegs
            .Concat(FilterSfxMegs(sfxMegs))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var patchName in PatchOrder)
            if (patchSlots.TryGetValue(patchName, out var patchPath))
                sortedBase.Add(patchPath);

        return sortedBase;
    }

    private static IEnumerable<string> FilterSfxMegs(IEnumerable<string> sfxPaths)
    {
        var included = new List<string>();
        var byPrefix = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in sfxPaths)
        {
            var stem = Path.GetFileNameWithoutExtension(path);

            if (stem.EndsWith("_non_localized", StringComparison.OrdinalIgnoreCase))
            {
                included.Add(path);
                continue;
            }

            var lastUnderscore = stem.LastIndexOf('_');
            if (lastUnderscore < 0)
            {
                included.Add(path);
                continue;
            }

            var prefix = stem[..lastUnderscore];
            if (!byPrefix.TryGetValue(prefix, out var list))
                byPrefix[prefix] = list = [];
            list.Add(path);
        }

        foreach (var (_, variants) in byPrefix)
        {
            if (variants.Count == 1)
            {
                included.Add(variants[0]);
            }
            else
            {
                var english = variants.FirstOrDefault(p =>
                    Path.GetFileNameWithoutExtension(p)
                        .EndsWith("_english", StringComparison.OrdinalIgnoreCase));
                if (english is not null)
                    included.Add(english);
            }
        }

        return included;
    }
}
