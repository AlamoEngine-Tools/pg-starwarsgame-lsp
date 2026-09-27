// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     A directory the engine WALKS, taking every file it finds, as opposed to a
///     <see cref="MetafileDefinition" /> that names files one at a time.
///     <para>
///         The distinction is not cosmetic. A file under a scanned directory is loaded because of
///         where it sits, so nothing names it and nothing ever will - asking why it is unregistered
///         would be asking the wrong question of every AI goal set in the game.
///     </para>
/// </summary>
/// <param name="Path">
///     Game-relative directory, lower-case, forward slashes, with a trailing slash so it cannot be
///     confused with a file.
/// </param>
/// <param name="Types">Object types the files in this directory hold, where the schema knows them.</param>
public sealed record ScannedDirectoryDefinition(
    string Path,
    IReadOnlyList<string> Types)
{
    /// <summary>What is worth saying about this directory. See <see cref="SchemaNote" />.</summary>
    public IReadOnlyList<SchemaNote> Notes { get; init; } = [];
}
