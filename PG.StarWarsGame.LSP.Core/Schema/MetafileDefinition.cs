// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public sealed record MetafileDefinition(
    string Path,
    MetafileType MetafileType,
    IReadOnlyList<string> Types)
{
    /// <summary>What is worth saying about this metafile. See <see cref="SchemaNote" />.</summary>
    public IReadOnlyList<SchemaNote> Notes { get; init; } = [];
}