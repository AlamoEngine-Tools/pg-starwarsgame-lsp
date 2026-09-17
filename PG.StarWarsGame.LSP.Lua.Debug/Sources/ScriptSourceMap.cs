// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <inheritdoc />
public sealed class ScriptSourceMap : IScriptSourceMap
{
    private const string DataSegment = "data";
    private const string ScriptsSegment = "scripts";

    private readonly IFileHelper _fileHelper;
    private readonly List<string> _rootUris;
    private readonly Lock _gate = new();

    /// <summary>Game path (folded) to document URI, for paths already resolved.</summary>
    private readonly Dictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Document URI to the spelling the game used for it. Keyed like the index.</summary>
    private readonly Dictionary<string, string> _reportedSpelling = new(DocumentUris.Comparer);

    public ScriptSourceMap(IFileHelper fileHelper, IReadOnlyList<string> sourceRoots)
    {
        ArgumentNullException.ThrowIfNull(sourceRoots);
        _fileHelper = fileHelper;
        Roots = sourceRoots.ToList();
        _rootUris = Roots.Select(root => _fileHelper.NormalizeUri(root).TrimEnd('/')).ToList();
    }

    public IReadOnlyList<string> Roots { get; }

    public string? ResolveToDocumentUri(string gamePath)
    {
        ArgumentNullException.ThrowIfNull(gamePath);
        var key = _fileHelper.NormalizeGamePath(gamePath);

        lock (_gate)
        {
            if (_resolved.TryGetValue(key, out var cached))
                return cached;
        }

        var uri = Resolve(gamePath);
        lock (_gate)
        {
            _resolved[key] = uri;
            if (uri is not null)
                _reportedSpelling.TryAdd(uri, gamePath);
        }

        return uri;
    }

    public string? ToGamePath(string documentUri)
    {
        ArgumentNullException.ThrowIfNull(documentUri);
        var uri = _fileHelper.NormalizeUri(documentUri);

        lock (_gate)
        {
            if (_reportedSpelling.TryGetValue(uri, out var reported))
                return reported;
        }

        for (var i = 0; i < _rootUris.Count; i++)
        {
            var rootUri = _rootUris[i];
            if (!DocumentUris.StartsWith(uri, rootUri + "/"))
                continue;

            var relative = uri[(rootUri.Length + 1)..];
            return DeriveGamePath(rootUri, relative);
        }

        return null;
    }

    private string? Resolve(string gamePath)
    {
        var forward = gamePath.Replace('\\', '/');
        if (LooksAbsolute(forward) && _fileHelper.FileSystem.File.Exists(gamePath))
            return _fileHelper.NormalizeUri(gamePath);

        var parts = forward.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != "." && !part.EndsWith(':'))
            .ToArray();
        if (parts.Length == 0)
            return null;

        foreach (var suffix in Suffixes(parts))
        {
            var relative = string.Join('/', suffix);
            foreach (var root in Roots)
            {
                var found = _fileHelper.FindInWorkspace([root], relative);
                if (found is not null)
                    return _fileHelper.NormalizeUri(found);
            }
        }

        return null;
    }

    /// <summary>
    ///     The tails worth trying, most specific first: the whole relative path, from its
    ///     <c>Data</c> segment, from its <c>Scripts</c> segment, after that segment, and the bare
    ///     file name last.
    /// </summary>
    private static IEnumerable<string[]> Suffixes(string[] parts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        yield return parts;
        seen.Add(string.Join('/', parts));

        foreach (var anchor in new[] { DataSegment, ScriptsSegment })
        {
            var index = Array.FindIndex(parts, p => string.Equals(p, anchor, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                continue;
            foreach (var candidate in new[] { parts[index..], parts[(index + 1)..] })
            {
                if (candidate.Length > 0 && seen.Add(string.Join('/', candidate)))
                    yield return candidate;
            }
        }

        if (seen.Add(parts[^1]))
            yield return [parts[^1]];
    }

    /// <summary>
    ///     <c>Data\Scripts\&lt;relative&gt;</c>, keeping whatever of that prefix the root itself
    ///     already carries: a root of <c>.../Data/Scripts</c> contributes both segments, a root of
    ///     <c>.../Data</c> contributes one, any other root contributes none and gets both added.
    /// </summary>
    private static string DeriveGamePath(string rootUri, string relative)
    {
        var rootSegments = rootUri.Split('/');
        var dataIndex = Array.FindLastIndex(rootSegments,
            s => string.Equals(s, DataSegment, StringComparison.OrdinalIgnoreCase));
        var prefix = dataIndex >= 0
            ? rootSegments[dataIndex..]
            : ["Data", "Scripts"];
        if (dataIndex >= 0 && prefix.Length == 1)
            prefix = [prefix[0], "Scripts"];

        return string.Join('\\', prefix.Concat(relative.Split('/')));
    }

    private static bool LooksAbsolute(string forwardSlashed)
    {
        return forwardSlashed.StartsWith('/') ||
               (forwardSlashed.Length > 2 && forwardSlashed[1] == ':' && forwardSlashed[2] == '/');
    }
}