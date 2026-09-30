// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     A cheap key over the <c>.alo</c> files under a set of asset roots, used to decide whether a
///     persisted <see cref="ModelBoneCatalogSnapshot" /> still describes the tree on disk.
/// </summary>
/// <remarks>
///     <para>
///         Path, length and write time per model - deliberately NOT the bytes. Hashing the content
///         of tens of thousands of models would cost roughly what re-extracting them costs, which
///         would leave the cache pointless; the whole reason this is worth doing is that stat-ing a
///         file is orders of magnitude cheaper than parsing it.
///     </para>
///     <para>
///         The residual risk is a model rewritten to the same length within the filesystem's write
///         time resolution. That is the accepted trade, and it is bounded: the catalog only feeds
///         boneName completion and validation, so the failure is a stale name list for one model
///         until it is touched again - not a wrong parse of anything. Anything the key cannot see
///         must therefore be handled by bumping
///         <see cref="ModelBoneCatalogSnapshot.CurrentSchemaVersion" /> instead.
///     </para>
/// </remarks>
public static class ModelBoneFingerprint
{
    /// <summary>
    ///     Computes the fingerprint for <paramref name="assetRoots" />. Missing roots contribute
    ///     nothing and are not an error - a layer may legitimately declare a root it does not ship.
    /// </summary>
    public static string Compute(IFileHelper fileHelper, IReadOnlyList<string> assetRoots)
    {
        var fs = fileHelper.FileSystem;

        // Sorted, because the filesystem makes no ordering promise: an unsorted key would differ
        // between runs over an unchanged tree and the cache would never hit.
        var entries = new List<string>();

        foreach (var root in assetRoots)
        {
            if (string.IsNullOrEmpty(root) || !fs.Directory.Exists(root)) continue;

            foreach (var file in fs.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!fs.Path.GetExtension(file).Equals(".alo", StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = fs.FileInfo.New(file);
                long length;
                long ticks;
                try
                {
                    length = info.Length;
                    ticks = info.LastWriteTimeUtc.Ticks;
                }
                catch
                {
                    // Vanished between enumeration and stat. Treat it as absent rather than
                    // failing the whole fingerprint - the next start will see the settled tree.
                    continue;
                }

                entries.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{fileHelper.NormalizeGamePath(file)}|{length}|{ticks}"));
            }
        }

        entries.Sort(StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var entry in entries)
            sb.Append(entry).Append('\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
