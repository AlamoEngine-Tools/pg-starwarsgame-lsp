// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     Default <see cref="IProjectLayerMap" />. Builds a list of (directory-URI-prefix, rank) entries
///     from the project layers and classifies a file by longest-prefix match, mirroring the
///     URI-prefix matching in <see cref="EaWXmlContext" />.
/// </summary>
public sealed class ProjectLayerMap : IProjectLayerMap
{
    private readonly IFileHelper _fileHelper;
    private ImmutableDictionary<int, string> _names = ImmutableDictionary<int, string>.Empty;

    // Snapshots replaced atomically by SetLayers; readers take a single Volatile.Read.
    private ImmutableArray<(string Prefix, int Rank)> _prefixes = [];
    private int _topRank;

    public ProjectLayerMap(IFileHelper fileHelper)
    {
        _fileHelper = fileHelper;
    }

    public void SetLayers(IReadOnlyList<ProjectLayer> layers)
    {
        var prefixes = ImmutableArray.CreateBuilder<(string, int)>();
        var names = ImmutableDictionary.CreateBuilder<int, string>();
        var top = 0;

        foreach (var layer in layers)
        {
            names[layer.Rank] = layer.Name;
            if (layer.Rank > top) top = layer.Rank;

            foreach (var dir in layer.XmlDirectories
                         .Concat(layer.ScriptRoots)
                         .Concat(layer.TextRoots)
                         .Concat(layer.AssetRoots))
                prefixes.Add((ToPrefix(dir), layer.Rank));
        }

        // Longest prefix first so the first match in GetRank is the most specific.
        prefixes.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));

        Volatile.Write(ref _topRank, top);
        _names = names.ToImmutable();
        _prefixes = prefixes.ToImmutable();
    }

    public int GetRank(string fileUri)
    {
        var prefixes = _prefixes;
        if (prefixes.IsDefaultOrEmpty)
            return 0;

        var normalized = _fileHelper.NormalizeUri(fileUri);
        foreach (var (prefix, rank) in prefixes)
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                return rank;

        // Not under any known layer: treat as the top layer so ad-hoc opened files win.
        return Volatile.Read(ref _topRank);
    }

    public string? GetLayerName(int rank)
    {
        return _names.GetValueOrDefault(rank);
    }

    private string ToPrefix(string directory)
    {
        var uri = directory.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? _fileHelper.NormalizeUri(directory)
            : _fileHelper.PathToFileUri(directory);
        return uri.TrimEnd('/') + '/';
    }
}