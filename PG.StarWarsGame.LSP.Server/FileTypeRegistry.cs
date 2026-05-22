// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server;

public sealed class FileTypeRegistry : IFileTypeRegistry
{
    private readonly ConcurrentDictionary<string, ImmutableArray<string>> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public ImmutableArray<string> GetTypesForFile(string normalizedPath)
    {
        return _map.TryGetValue(normalizedPath, out var types) ? types : ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
        _map[normalizedPath] = typeNames;
    }

    public void UnregisterFile(string normalizedPath)
    {
        _map.TryRemove(normalizedPath, out _);
    }

    public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;
}