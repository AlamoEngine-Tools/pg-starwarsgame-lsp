// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record LocalisationConfig
{
    /// <summary>The language assumed when nothing else names one. See <see cref="Language" />.</summary>
    public const string DefaultLanguage = "ENGLISH";

    /// <summary>
    ///     Format of the mod's localisation source files.
    ///     Accepted values: "Csv" (default), "Xml", "Nls", "Dat".
    /// </summary>
    public string ResourceType { get; init; } = "Csv";

    /// <summary>
    ///     Which of the game's languages the index resolves, as an Alamo language identifier
    ///     ("ENGLISH", "GERMAN", ...). Matched case-insensitively; an identifier the language service
    ///     cannot resolve falls back to its default.
    ///     <para>
    ///         Deliberately NOT <see cref="LspConfiguration.Locale" />. That one is ISO 639-1 and names
    ///         the language the LSP writes its own hover text and diagnostics in; this one names a
    ///         game string table. Wiring them together would make switching the documentation language
    ///         silently change which translations resolve, which is what this setting was added to stop.
    ///     </para>
    /// </summary>
    public string Language { get; init; } = DefaultLanguage;

    /// <summary>
    ///     Explicit paths to localisation files. Normally the loader takes localisation from the
    ///     resolved <c>.pgproj</c> project layers' text directories; these paths are a fallback used
    ///     only when no project is resolved.
    /// </summary>
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
}