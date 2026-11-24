// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Localisation.Rows;

/// <summary>
///     Takes a batch staged against a set of single-language files apart into what each file is to
///     be given.
///     <para>
///         A format that holds one language per file makes a project translated into six languages
///         six files, and the grid shows them as one table. Which file a change belongs to is
///         decided here, on the server: the editor names the set, never the file a change goes to.
///     </para>
///     <para>
///         A value belongs to the one file that holds its language. Everything that changes the
///         shape of the table - adding, renaming or deleting a key - belongs to every file in the
///         set, or the files would drift apart into different key lists and stop being one table.
///     </para>
/// </summary>
public static class TranslationSetPartition
{
    /// <summary>
    ///     For each file, in the order given, the commands it takes with the position each had in
    ///     the batch. A file the batch does not touch gets none.
    /// </summary>
    /// <param name="commands">The batch as staged.</param>
    /// <param name="languages">The languages each file of the set holds.</param>
    public static IReadOnlyList<IReadOnlyList<(int Index, LocKeyedCommandDto Command)>> Split(
        IReadOnlyList<LocKeyedCommandDto> commands, IReadOnlyList<IReadOnlyList<string>> languages)
    {
        var parts = languages.Select(_ => new List<(int, LocKeyedCommandDto)>()).ToList();

        // One file is not a set: it takes the batch as it is, a new language column included.
        if (languages.Count == 1)
        {
            for (var i = 0; i < commands.Count; i++) parts[0].Add((i, commands[i]));
            return parts;
        }

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            switch (command.Kind)
            {
                case "setValue":
                    // A value for a language no file holds has nowhere to go. Dropped rather than
                    // guessed at: writing it into an arbitrary file would put German text in the
                    // French file.
                    if (command.Language is { } language && Holder(languages, language) is { } owner)
                        parts[owner].Add((i, command));
                    break;

                case "addEntry":
                    // Each file gets only the values for the languages it holds; the rest of the row
                    // is that file's business to leave blank.
                    for (var member = 0; member < languages.Count; member++)
                        parts[member].Add((i, command with
                        {
                            Values = (command.Values ?? [])
                            .Where(v => Holds(languages[member], v.Language))
                            .ToList()
                        }));
                    break;

                case "renameKey" or "deleteEntry":
                    foreach (var part in parts) part.Add((i, command));
                    break;

                // addLanguage has no meaning in a set: another language is another file there.
            }
        }

        return parts;
    }

    private static int? Holder(IReadOnlyList<IReadOnlyList<string>> languages, string language)
    {
        for (var member = 0; member < languages.Count; member++)
            if (Holds(languages[member], language))
                return member;
        return null;
    }

    private static bool Holds(IReadOnlyList<string> languages, string language)
    {
        return languages.Any(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
    }
}