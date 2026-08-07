// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

public static class LocalisationFormatUtility
{
    /// <summary>
    ///     The file extension a localisation format is stored in, or null if it is not a format
    ///     this understands.
    /// </summary>
    /// <remarks>
    ///     Covers DAT, unlike <see cref="ILocalisationFormatConverter.ExtensionFor" /> - that one
    ///     answers "can this be written", which DAT deliberately cannot be. Identifying which format
    ///     a file already is, is a different question, and conflating the two meant a project
    ///     declared as DAT never matched its own files: converting one wrote the new file and left
    ///     the .pgproj pointing at DAT, so the tree still listed the old format.
    /// </remarks>
    public static string? ToExtension(string? format)
    {
        return format?.ToLowerInvariant() switch
        {
            "csv" => ".csv",
            "xml" => ".xml",
            "nls" => ".properties",
            "dat" => ".dat",
            _ => null
        };
    }

    /// <summary>
    ///     The conventional file name for a brand-new localisation file. Null for a format with no
    ///     generator (currently DAT), and null for a single-language format asked for without a
    ///     language, since such a file cannot be named without one.
    /// </summary>
    /// <remarks>
    ///     Written lowercase to match the engine, whose own files are <c>mastertextfile_english.dat</c>
    ///     and <c>creditstext_english.dat</c>. The game reads names case-insensitively, so this is a
    ///     convention rather than a requirement - but writing PascalCase into a directory of lowercase
    ///     files made a stock text folder look like two different projects.
    ///     <para>
    ///         <paramref name="language" /> is required exactly when the format carries its language in
    ///         the file name - see <see cref="LocalisationFileNameLanguageResolver" />.
    ///     </para>
    /// </remarks>
    public static string? ToSeedFileName(string format, string? language = null)
    {
        var extension = ToExtension(format);
        if (extension is null || extension == ".dat") return null;

        if (!LocalisationFileNameLanguageResolver.CarriesLanguageInFileName(extension))
            return $"mastertextfile{extension}";

        return string.IsNullOrWhiteSpace(language)
            ? null
            : $"mastertextfile_{language.ToLowerInvariant()}{extension}";
    }
}