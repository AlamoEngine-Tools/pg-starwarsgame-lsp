// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Reports languages the file leaves half-filled.
/// </summary>
/// <remarks>
///     The DAT export writes only the entries that have a value in the language being written, so
///     an entry left empty is <em>dropped</em> from that language's file rather than exported blank.
///     In a keyed text file that is worth knowing: every key is meant to resolve, and one that does
///     not falls back to a lower layer or to nothing.
///     <para>
///         For an <em>ordered</em> credits file this check does not apply and is not used - a
///         shorter list there is ordinary (a dub recorded with a smaller cast), and the drop is the
///         mechanism that lets one file carry lists of different lengths. See
///         <see cref="CreditsCoverageInspector" /> for the check that does hold there.
///     </para>
///     <para>
///         One problem per language, carrying a count, rather than one per entry: a partly
///         translated MasterTextFile would otherwise report tens of thousands of them and bury
///         everything else in the panel.
///     </para>
/// </remarks>
public static class LanguageCoverageInspector
{
    /// <param name="rows">The rows as they will be once the staged batch lands.</param>
    /// <param name="languages">The file's declared languages, in declaration order.</param>
    public static IReadOnlyList<(string Language, int Empty, int Total)> Inspect(
        IReadOnlyList<LocRowDto> rows, IReadOnlyList<string> languages)
    {
        // Nothing to compare against: a single-language file cannot be half-filled, it is simply
        // as long as it is.
        if (languages.Count < 2) return [];

        var empty = languages.ToDictionary(l => l, _ => 0, StringComparer.OrdinalIgnoreCase);
        var counted = 0;

        foreach (var row in rows)
        {
            // Only rows that say something somewhere. A row blank in every language is a spacer or
            // an empty line, and is not evidence that any one language is behind.
            var anyText = languages.Any(l => !string.IsNullOrWhiteSpace(ValueOf(row, l)));
            if (!anyText) continue;

            counted++;
            foreach (var language in languages)
                if (string.IsNullOrWhiteSpace(ValueOf(row, language)))
                    empty[language]++;
        }

        return languages
            .Where(l => empty[l] > 0)
            .Select(l => (Language: l, Empty: empty[l], Total: counted))
            .ToList();
    }

    /// <summary>The warning text, phrased once so both editors say the same thing.</summary>
    public static string Message(string language, int empty, int total)
    {
        return $"{language} has no text in {empty} of {total} entries. Those entries are left out "
               + $"of the {language} build entirely, so the game falls back to whatever a lower "
               + "layer provides - or shows nothing.";
    }

    private static string? ValueOf(LocRowDto row, string language)
    {
        foreach (var value in row.Values)
            if (string.Equals(value.Language, language, StringComparison.OrdinalIgnoreCase))
                return value.Value;

        return null;
    }
}
