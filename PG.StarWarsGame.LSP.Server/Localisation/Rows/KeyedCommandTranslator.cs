// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Turns key-addressed translation edits into the positional commands the document editor
///     applies.
///     <para>
///         This exists so that exactly one layer knows about row positions. A file is a sequence of
///         lines, so writing one back unchanged is inherently positional - that is what
///         <see cref="LocalisationDocumentEditor" /> does, and it must not be duplicated or
///         reworked. Everything above this line addresses translations the way the user thinks of
///         them: by key.
///     </para>
///     <para>
///         Pure and static: no IO, no state between calls, so the addressing rules are testable on
///         their own.
///     </para>
/// </summary>
public static class KeyedCommandTranslator
{
    /// <summary>
    ///     A blank key is a row the game can never look up. Credits files use blank rows as spacers;
    ///     a lookup table has no such concept, so one is refused outright rather than written and
    ///     reported as a problem afterwards.
    /// </summary>
    private const string BlankKey =
        "A key is required - it is how the game refers to this text.";


    /// <summary>
    ///     Translates a whole batch, or fails without producing anything.
    ///     <para>
    ///         Keys resolve against the document <em>as of all preceding commands</em>, matching how
    ///         a positional batch already composes: adding an entry and then filling it in is a
    ///         normal thing to stage, and the second command has to see the first.
    ///     </para>
    /// </summary>
    public static KeyedTranslationResult Translate(
        LocDocument document, IReadOnlyList<LocKeyedCommandDto> commands)
    {
        // The working key list mirrors what the document will look like as each command lands. Only
        // the keys are tracked, because position is the only thing that has to be derived.
        var keys = document.Rows.Select(row => row.Key).ToList();
        var translated = new List<LocEditCommandDto>(commands.Count);

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];

            switch (command.Kind)
            {
                case "setValue":
                {
                    if (Resolve(keys, command.Key, out var index, out var error))
                        translated.Add(new LocEditCommandDto(
                            "setCell", index, Language: command.Language, Value: command.Value));
                    else
                        return KeyedTranslationResult.Fail(i, error);
                    break;
                }

                case "addEntry":
                {
                    var key = command.Key ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(key))
                        return KeyedTranslationResult.Fail(i, BlankKey);

                    if (Clashes(keys, key))
                        return KeyedTranslationResult.Fail(i,
                            $"'{key}' is already in this file. Only one row per key is ever read.");

                    translated.Add(new LocEditCommandDto(
                        "insertRow", keys.Count, Key: key, Values: command.Values));
                    keys.Add(key);
                    break;
                }

                case "deleteEntry":
                {
                    if (!Resolve(keys, command.Key, out var index, out var error))
                        return KeyedTranslationResult.Fail(i, error);

                    translated.Add(new LocEditCommandDto("deleteRow", index));
                    keys.RemoveAt(index);
                    break;
                }

                case "renameKey":
                {
                    if (!Resolve(keys, command.Key, out var index, out var error))
                        return KeyedTranslationResult.Fail(i, error);

                    var newKey = command.NewKey ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(newKey))
                        return KeyedTranslationResult.Fail(i, BlankKey);

                    // Excluding the row being renamed, so changing only a key's casing is allowed.
                    if (Clashes(keys, newKey, index))
                        return KeyedTranslationResult.Fail(i,
                            $"'{newKey}' is already in this file. Only one row per key is ever read.");

                    translated.Add(new LocEditCommandDto("setKey", index, Key: newKey));
                    keys[index] = newKey;
                    break;
                }

                // Nothing to resolve - it applies to every row at once.
                case "addLanguage":
                    translated.Add(new LocEditCommandDto("addLanguage", Language: command.Language));
                    break;

                default:
                    return KeyedTranslationResult.Fail(i,
                        $"'{command.Kind}' is not a translation command. "
                        + "Row positions are not addressable in a translation file.");
            }
        }

        return KeyedTranslationResult.Ok(translated, keys);
    }

    /// <summary>
    ///     Finds the single row carrying a key.
    ///     <para>
    ///         Matching is exact. A duplicate key is invalid but loadable - the batch validator
    ///         reports it rather than refusing the file - so an ambiguous address is refused by name
    ///         instead of silently taking the first match and editing a row the user was not looking
    ///         at.
    ///     </para>
    /// </summary>
    private static bool Resolve(List<string> keys, string? key, out int index, out string error)
    {
        index = -1;
        error = string.Empty;

        if (string.IsNullOrEmpty(key))
        {
            error = "This change does not say which entry it applies to.";
            return false;
        }

        var found = -1;
        for (var i = 0; i < keys.Count; i++)
        {
            if (!string.Equals(keys[i], key, StringComparison.Ordinal)) continue;

            if (found >= 0)
            {
                error = $"'{key}' is on both row {found + 1} and row {i + 1}. "
                        + "Only one of them would ever be read - remove one before editing it.";
                return false;
            }

            found = i;
        }

        if (found < 0)
        {
            error = $"'{key}' is not in this file. Reload before editing again.";
            return false;
        }

        index = found;
        return true;
    }

    /// <summary>
    ///     Whether a name is already taken, ignoring case: the game reads one row per key regardless
    ///     of how it is spelled, so a differently-cased addition would create a row that is never
    ///     read.
    /// </summary>
    private static bool Clashes(List<string> keys, string key, int ignoreIndex = -1)
    {
        for (var i = 0; i < keys.Count; i++)
            if (i != ignoreIndex && string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}
