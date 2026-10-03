// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Hashes each name once. A name's hash never changes, and the same names come back on every edit
///     - when a document is re-applied, when a symbol leaves the index, when a document is diagnosed -
///     so the index service and the diagnostics share one of these.
/// </summary>
/// <remarks>
///     Case-insensitive, because the engine hashes the upper-cased name. Grows with the distinct names
///     seen, which is the workspace plus the game: tens of thousands of entries, no more.
/// </remarks>
public sealed class CachedObjectNameHash : IObjectNameHash
{
    private readonly ConcurrentDictionary<string, uint> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IObjectNameHash _inner;

    public CachedObjectNameHash(IObjectNameHash inner)
    {
        _inner = inner;
    }

    public uint Of(string name)
    {
        return _hashes.GetOrAdd(name, static (n, inner) => inner.Of(n), _inner);
    }
}
