// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     The file-type registry from an in-memory map: which files are registered and the types each
///     holds. Keys are compared ignoring case, as a URI's drive letter may be spelled either way; a
///     file nobody registered has no types and is not registered.
/// </summary>
public sealed class FakeFileTypeRegistry : IFileTypeRegistry
{
    private readonly Dictionary<string, ImmutableArray<string>> _types = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ImmutableArray<string>> All => _types;

    public ImmutableArray<string> GetTypesForFile(string fileUri)
    {
        return _types.TryGetValue(fileUri, out var types) ? types : ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
        _types[fileUri] = typeNames;
    }

    public void UnregisterFile(string fileUri)
    {
        _types.Remove(fileUri);
    }

    /// <summary>Registers <paramref name="fileUri" /> with the given types; none registers it as a file of unmodelled contents.</summary>
    public FakeFileTypeRegistry With(string fileUri, params string[] types)
    {
        _types[fileUri] = [.. types];
        return this;
    }
}