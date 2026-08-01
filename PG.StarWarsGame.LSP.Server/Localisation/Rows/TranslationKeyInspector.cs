// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Checks the keys a translation file will hold once a staged batch lands.
///     <para>
///         Works from the key list rather than from composed text, which is what lets a compiled
///         <c>.dat</c> be validated at all - it has no text to compose and re-read.
///     </para>
///     <para>
///         What it finds is nearly always pre-existing:
///         <see cref="KeyedCommandTranslator" /> refuses to create a blank or clashing key, so a
///         problem reported here is one the file already had.
///     </para>
/// </summary>
public static class TranslationKeyInspector
{
    public static IReadOnlyList<LocTranslationProblemDto> Inspect(IReadOnlyList<string> keys)
    {
        var problems = new List<LocTranslationProblemDto>();
        // Case-insensitive because the game resolves a key regardless of how it is spelled, so two
        // castings of one name are one entry and only one of them is ever read.
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];

            if (string.IsNullOrWhiteSpace(key))
            {
                problems.Add(new LocTranslationProblemDto(null, null, LocProblemSeverity.Error,
                    "This entry has no key, so the game cannot reference it."));
                continue;
            }

            if (seen.TryGetValue(key, out var first))
                problems.Add(new LocTranslationProblemDto(key, null, LocProblemSeverity.Error,
                    $"'{key}' is already defined on row {first + 1}. "
                    + "Only one of the two would ever be read."));
            else
                seen[key] = i;
        }

        return problems;
    }
}
