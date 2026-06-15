// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record LocalisationConfig
{
    /// <summary>
    ///     Format of the mod's localisation source files.
    ///     Accepted values: "Csv" (default), "Xml", "Nls", "Dat".
    /// </summary>
    public string ResourceType { get; init; } = "Csv";

    /// <summary>
    ///     Explicit paths to localisation files. Normally the loader takes localisation from the
    ///     resolved <c>.pgproj</c> project layers' text directories; these paths are a fallback used
    ///     only when no project is resolved.
    /// </summary>
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
}