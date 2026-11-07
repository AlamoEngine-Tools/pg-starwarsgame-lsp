// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>
///     Concrete <see cref="IAssetFileIndex" /> over a normalised, case-insensitive set of asset
///     file paths. Built by merging the baseline catalog with the runtime workspace glob.
/// </summary>
public sealed class MergedAssetFileIndex : IAssetFileIndex
{
    // Paths pre-bucketed by extension: completion providers enumerate one extension per request,
    // and a linear EndsWith scan over the full catalog (all packed game assets) per keystroke is
    // needlessly expensive.
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _byExtension;

    // Paths bucketed by final segment, for the same reason as _byExtension but a far sharper
    // filter: a reference is usually a bare filename, so an existence check misses the exact-path
    // lookup and would otherwise search the catalog. Built LAZILY - a session that never validates
    // an asset reference should not pay for it, and a workspace catalog runs to tens of thousands
    // of entries.
    private readonly Lazy<ImmutableDictionary<string, ImmutableArray<string>>> _byFileName;

    // The project each workspace path comes from; empty for a catalog merged without projects.
    private readonly ImmutableDictionary<string, int> _layerOf;
    private readonly ImmutableHashSet<string> _packedPaths;
    private readonly ImmutableHashSet<string> _paths;

    public MergedAssetFileIndex(IEnumerable<string> paths)
        : this(paths.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), ImmutableHashSet<string>.Empty,
            ImmutableDictionary<string, int>.Empty)
    {
    }

    private MergedAssetFileIndex(ImmutableHashSet<string> all, ImmutableHashSet<string> packed,
        ImmutableDictionary<string, int> layerOf)
    {
        _paths = all;
        _packedPaths = packed;
        _layerOf = layerOf;
        _byExtension = BucketByExtension(all);
        _byFileName = new Lazy<ImmutableDictionary<string, ImmutableArray<string>>>(
            () => BucketByFileName(all), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool Contains(string normalisedPath)
    {
        return _paths.Contains(normalisedPath);
    }

    public IEnumerable<string> GetByExtension(string ext)
    {
        return _byExtension.GetValueOrDefault(ext, ImmutableArray<string>.Empty);
    }

    public IEnumerable<string> GetByFileName(string fileName)
    {
        return string.IsNullOrEmpty(fileName)
            ? []
            : _byFileName.Value.GetValueOrDefault(fileName, ImmutableArray<string>.Empty);
    }

    public bool IsPackedAsset(string normalisedPath)
    {
        return _packedPaths.Contains(normalisedPath);
    }

    public int? LayerRankOf(string normalisedPath)
    {
        return _layerOf.TryGetValue(normalisedPath, out var rank) ? rank : null;
    }

    /// <summary>
    ///     Buckets by final segment. Keying on the WHOLE segment is what preserves the guarantee
    ///     the caller's leading-separator test used to provide by itself: <c>oo.tga</c> can never
    ///     land in the bucket for <c>foo.tga</c>.
    /// </summary>
    private static ImmutableDictionary<string, ImmutableArray<string>> BucketByFileName(
        ImmutableHashSet<string> paths)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var name = FileNameOf(path);
            if (name.Length == 0) continue;
            if (!buckets.TryGetValue(name, out var list))
                buckets[name] = list = [];
            list.Add(path);
        }

        return buckets.ToImmutableDictionary(
            kv => kv.Key, kv => kv.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The part after the last <c>/</c>. Catalog paths are normalised to forward slashes, so
    ///     this does not need <see cref="Path" /> - which would also split on the platform
    ///     separator and give a different answer on a case-sensitive host.
    /// </summary>
    public static string FileNameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> BucketByExtension(
        ImmutableHashSet<string> paths)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var ext = Path.GetExtension(path);
            if (ext.Length == 0) continue;
            if (!buckets.TryGetValue(ext, out var list))
                buckets[ext] = list = [];
            list.Add(path);
        }

        return buckets.ToImmutableDictionary(
            kv => kv.Key, kv => kv.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Unions the baseline (packed game) catalog with the workspace (loose) asset paths.
    ///     Baseline paths are tracked separately so <see cref="IsPackedAsset" /> can distinguish them.
    /// </summary>
    public static MergedAssetFileIndex Merge(
        IEnumerable<string> baselineFiles, IEnumerable<string> workspaceFiles)
    {
        return MergeLayered(baselineFiles, workspaceFiles.Select(path => (path, (int?)null)));
    }

    /// <summary>
    ///     <see cref="Merge(IEnumerable{string}, IEnumerable{string})" /> over workspace paths that
    ///     carry the precedence rank of the project they come from, or null where no project
    ///     declares their folder. Of two projects shipping one path the higher one's copy is the one
    ///     read, so that rank is the one kept.
    /// </summary>
    public static MergedAssetFileIndex MergeLayered(
        IEnumerable<string> baselineFiles, IEnumerable<(string Path, int? Rank)> workspaceFiles)
    {
        var files = workspaceFiles.ToList();
        var workspace = files.Select(f => f.Path).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var layerOf = files
            .Where(f => f.Rank is not null)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(g => g.Key, g => g.Max(f => f.Rank!.Value), StringComparer.OrdinalIgnoreCase);
        var baseline = baselineFiles.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        // Paths that also exist as loose workspace files are not "packed" - workspace overrides baseline.
        var packed = baseline
            .Where(p => !workspace.Contains(p))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        return new MergedAssetFileIndex(baseline.Union(workspace), packed, layerOf);
    }
}
