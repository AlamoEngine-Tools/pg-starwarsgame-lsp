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
    private readonly ImmutableHashSet<string> _packedPaths;
    private readonly ImmutableHashSet<string> _paths;

    public MergedAssetFileIndex(IEnumerable<string> paths)
    {
        _paths = paths.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        _packedPaths = ImmutableHashSet<string>.Empty;
    }

    private MergedAssetFileIndex(ImmutableHashSet<string> all, ImmutableHashSet<string> packed)
    {
        _paths = all;
        _packedPaths = packed;
    }

    public bool Contains(string normalisedPath)
    {
        return _paths.Contains(normalisedPath);
    }

    public IEnumerable<string> GetByExtension(string ext)
    {
        return _paths.Where(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsPackedAsset(string normalisedPath)
    {
        return _packedPaths.Contains(normalisedPath);
    }

    /// <summary>
    ///     Unions the baseline (packed game) catalog with the workspace (loose) asset paths.
    ///     Baseline paths are tracked separately so <see cref="IsPackedAsset" /> can distinguish them.
    /// </summary>
    public static MergedAssetFileIndex Merge(
        IEnumerable<string> baselineFiles, IEnumerable<string> workspaceFiles)
    {
        var workspace = workspaceFiles.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var baseline = baselineFiles.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        // Paths that also exist as loose workspace files are not "packed" — workspace overrides baseline.
        var packed = baseline
            .Where(p => !workspace.Contains(p))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        return new MergedAssetFileIndex(baseline.Union(workspace), packed);
    }
}