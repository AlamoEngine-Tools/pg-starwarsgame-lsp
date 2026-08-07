// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Finds sections of a credits crawl that would play with a heading and nothing under it.
/// </summary>
/// <remarks>
///     Deliberately NOT "this language has fewer entries than that one". A credits list is allowed
///     to differ between languages and routinely does - an English cast with thirty voice actors
///     and a German dub recorded with twelve is not a mistake, and the export dropping the empty
///     entries is exactly what lets one file carry both. Warning about that would be noise on every
///     correctly translated file.
///     <para>
///         What it does report is a <c>HEADER</c> that survives into the crawl with no <c>CENTER</c>
///         line beneath it: the heading is read out, and then the crawl moves on to the next section.
///         That is usually a label translated without the names under it - but it is not an error.
///         A credits key is a formatting directive, so such a file is valid and renders exactly as
///         written, which is why the finding is graded as information rather than as a problem.
///     </para>
///     <para>
///         Keyed on the shipped directives. A file using its own vocabulary simply reports nothing
///         rather than guessing at structure it cannot see.
///     </para>
/// </summary>
public static class CreditsCoverageInspector
{
    private const string Header = "HEADER";
    private const string BlankSentinel = "[TBL]";

    /// <param name="rows">The rows as they will be once the staged batch lands.</param>
    /// <param name="languages">The file's declared languages.</param>
    public static IReadOnlyList<(string Language, int Index, string Label)> Inspect(
        IReadOnlyList<LocRowDto> rows, IReadOnlyList<string> languages)
    {
        var orphaned = new List<(string, int, string)>();

        foreach (var language in languages)
        {
            // A language the file does not use at all is not half-done, it is absent - reporting
            // every section of it would bury the one that is genuinely broken.
            if (!rows.Any(r => HasText(r, language))) continue;

            for (var i = 0; i < rows.Count; i++)
            {
                if (!IsHeader(rows[i]) || !HasText(rows[i], language)) continue;

                var hasContent = false;
                for (var j = i + 1; j < rows.Count && !IsHeader(rows[j]); j++)
                    if (HasText(rows[j], language)) { hasContent = true; break; }

                if (!hasContent)
                    orphaned.Add((language, i, ValueOf(rows[i], language) ?? string.Empty));
            }
        }

        return orphaned;
    }

    public static string Message(string language, string label)
    {
        return $"In {language}, '{label}' is a heading with nothing under it. The crawl reads it out "
               + "and moves straight on, which is valid - a credits key is a formatting directive, "
               + "not an entry. Usually it means the label was translated and the names under it were "
               + "not; leaving the heading itself empty skips the whole section.";
    }

    private static bool IsHeader(LocRowDto row)
    {
        return string.Equals(row.Key, Header, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The blank-line marker is a spacer, not content - a section of gaps is still empty.</summary>
    private static bool HasText(LocRowDto row, string language)
    {
        var value = ValueOf(row, language);
        return !string.IsNullOrWhiteSpace(value)
               && !string.Equals(value, BlankSentinel, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValueOf(LocRowDto row, string language)
    {
        foreach (var value in row.Values)
            if (string.Equals(value.Language, language, StringComparison.OrdinalIgnoreCase))
                return value.Value;

        return null;
    }
}
