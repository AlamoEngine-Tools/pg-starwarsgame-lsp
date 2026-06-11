// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed class FileTypeRegistry : IFileTypeRegistry
{
    private readonly ConcurrentDictionary<string, ImmutableArray<string>> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public ImmutableArray<string> GetTypesForFile(string fileUri)
    {
        return _map.TryGetValue(fileUri, out var types) ? types : ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
        _map[fileUri] = typeNames;
    }

    public void UnregisterFile(string fileUri)
    {
        _map.TryRemove(fileUri, out _);
    }

    public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;
}
