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

    /// <summary>
    ///     What the game does with a name two of the registry's files both define. Set for every
    ///     <see cref="MetafileType.FileRegistry" />; null for a file that holds its own objects.
    /// </summary>
    public DuplicateNameRule? DuplicateNames { get; init; }
}