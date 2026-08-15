// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace PG.StarWarsGame.LSP.Assets.ShipNames;

/// <summary>
///     Reads GameConstants' <c>ShipNameTextFiles</c> wiring and the name lists it points at.
/// </summary>
/// <remarks>
///     Objects listed here get an individual ship name drawn from a pool instead of showing their
///     class. The engine's <c>TheShipNamesManagerClass</c> picks one and remembers which are spent,
///     persisting that across saves; a preview has no campaign to remember anything for, so it just
///     picks.
/// </remarks>
public static class ShipNameTextFiles
{
    /// <summary>
    ///     Parses the flat, comma-separated <c>&lt;objectId&gt;, &lt;path&gt;</c> token list.
    /// </summary>
    /// <remarks>
    ///     The mapping is MANY-TO-ONE, not a pairing: the shipped data points
    ///     <c>Star_Destroyer</c>, <c>Generic_Star_Destroyer</c> and
    ///     <c>Star_Destroyer_Tractor_Fighters</c> at one file. Keyed case-insensitively, as object
    ///     ids are everywhere else.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ParsePairs(string? rawValue)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawValue))
            return pairs;

        var tokens = rawValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Step by two. A trailing odd token is an id with no file - incomplete rather than a pool
        // of its own, so it is dropped here rather than paired with an invented filename.
        for (var i = 0; i + 1 < tokens.Length; i += 2)
            pairs[tokens[i]] = tokens[i + 1];

        return pairs;
    }

    /// <summary>
    ///     Decodes one name list: UTF-16 LE with a BOM, CRLF, one literal name per line.
    /// </summary>
    /// <remarks>
    ///     These hold LITERAL names, not localisation keys, so they bypass translation entirely and
    ///     are the same in every language.
    ///     <para>
    ///         The BOM is REQUIRED. Read as UTF-8 these produce mojibake that looks like a data bug,
    ///         and a file saved without the BOM would come back as one long garbage "name" that the
    ///         preview would then display as if it were real. Returning nothing is the honest
    ///         answer; the caller can tell it apart from a missing file.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<string> ReadNames(byte[]? content)
    {
        var bom = Encoding.Unicode.GetPreamble();
        if (content is null || content.Length < bom.Length)
            return [];

        for (var i = 0; i < bom.Length; i++)
            if (content[i] != bom[i])
                return [];

        var text = Encoding.Unicode.GetString(content, bom.Length, content.Length - bom.Length);

        return text
            .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
