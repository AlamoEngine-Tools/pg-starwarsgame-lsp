// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

public static class LocalisationFormatUtility
{
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