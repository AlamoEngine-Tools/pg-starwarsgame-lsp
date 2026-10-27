// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Questions answered from an <see cref="ISchemaProvider" />'s data rather than by it, so the
///     interface carries data and lookups only (#304).
/// </summary>
public static class SchemaProviderExtensions
{
    /// <summary>
    ///     Whether this document sits under a directory the engine walks, in which case nothing
    ///     names it and nothing was ever supposed to.
    /// </summary>
    /// <param name="schema">The schema whose <see cref="ISchemaProvider.AllScannedDirectories" /> decide.</param>
    /// <param name="path">
    ///     A path or URI in any spelling; matched case-insensitively on the declared directory as a
    ///     substring, because the declaration is game-relative and the document is not.
    /// </param>
    public static bool IsInScannedDirectory(this ISchemaProvider schema, string path)
    {
        if (schema.AllScannedDirectories.Count == 0) return false;

        var normalized = path.Replace('\\', '/');
        return schema.AllScannedDirectories.Any(d =>
            normalized.Contains(d.Path, StringComparison.OrdinalIgnoreCase));
    }
}