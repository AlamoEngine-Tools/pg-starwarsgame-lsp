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

    // The conventional file name used when generating a brand-new localisation file for a
    // format. Null for a format with no generator (currently DAT).
    public static string? ToSeedFileName(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "csv" => "MasterTextFile.csv",
            "xml" => "MasterTextFile.xml",
            "nls" => "MasterTextFile.properties",
            _ => null
        };
    }
}