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
    ///     Explicit paths to localisation files. When empty the loader auto-detects from
    ///     <see cref="LspConfiguration.ModPaths" /> by looking for <c>Data/Text/</c> sub-directories.
    /// </summary>
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
}