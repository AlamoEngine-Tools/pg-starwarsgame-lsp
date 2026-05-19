// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IFileTypeRegistry
{
    ImmutableArray<string> GetTypesForFile(string normalizedPath);
    void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames);
    void UnregisterFile(string normalizedPath);
    IReadOnlyDictionary<string, ImmutableArray<string>> All { get; }
}
