// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.Commons.Hashing;
using PG.StarWarsGame.Files.DAT.Binary;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Checks the keys a translation file will hold once a staged batch lands.
///     <para>
///         Works from the key list rather than from composed text, which is what lets a compiled
///         <c>.dat</c> be validated at all - it has no text to compose and re-read.
///     </para>
///     <para>
///         What it finds is nearly always pre-existing:
///         <see cref="KeyedCommandTranslator" /> refuses to create a blank or colliding key, so a
///         problem reported here is one the file already had.
///     </para>
/// </summary>
public static class TranslationKeyInspector
{
    /// <summary>
    ///     Reports blank keys, keys ASCII cannot hold, real collisions, and keys that differ only in
    ///     case.
    /// </summary>
    /// <remarks>
    ///     Collision is decided by the engine's own rule, not by string comparison: an entry is
    ///     addressed by the CRC32 of its key bytes in ASCII (<c>DatBinaryConverter</c>), so two keys
    ///     collide exactly when those hashes match.
    ///     <para>
    ///         This used to compare case-insensitively and call any case-only difference an error, on
    ///         the belief that the game resolves a key however it is spelled. It does not - CRC32 is
    ///         case-sensitive, so <c>TEXT_A</c> and <c>text_a</c> are two entries and both are read.
    ///         They stay reported, as a warning, because telling two keys apart by case alone is a
    ///         trap for whoever reads the file next.
    ///     </para>
    ///     <para>
    ///         Hashing through ASCII also folds every non-ASCII character to <c>?</c>, so two keys
    ///         differing only outside ASCII would collide for real however different they look. In
    ///         practice that pair never reaches the collision check any more: a key ASCII cannot
    ///         hold is refused on its own account first, because the writer cannot store it at all.
    ///         The folding is still what makes the checksum the right identity to compare - it is
    ///         simply no longer the way a collision gets discovered.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<LocTranslationProblemDto> Inspect(
        IReadOnlyList<string> keys, ICrc32HashingService hashing)
    {
        var problems = new List<LocTranslationProblemDto>();

        // Keyed by the engine's own identity for an entry. Case-insensitive lookup is tracked
        // separately because it is a readability concern, not a correctness one.
        var byChecksum = new Dictionary<uint, int>();
        var byCasing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];

            if (string.IsNullOrWhiteSpace(key))
            {
                problems.Add(new LocTranslationProblemDto(null, null, LocProblemSeverity.Error,
                    "This entry has no key, so the game cannot reference it.", i));
                continue;
            }

            // Reported before anything else about this key, and instead of it: a key the file
            // cannot be written with is not worth also discussing collisions or casing for.
            //
            // A compiled .dat stores keys as ASCII bytes and the writer refuses anything else, so
            // this is fatal rather than cosmetic - and it used to surface only when Save reached
            // the writer, once per language file, reading "Value contains non-ASCII characters
            // (Parameter 'value')". That names the wrong field - it is the key being rejected - and
            // arrives after every edit has been made.
            if (NonAsciiIn(key) is { Length: > 0 } offending)
            {
                problems.Add(new LocTranslationProblemDto(key, null, LocProblemSeverity.Error,
                    $"'{key}' cannot be used as a key: {offending}cannot be written to a .dat, "
                    + "which stores keys as ASCII. Rename the entry using unaccented letters - the "
                    + "translation itself is unaffected.", i));
                continue;
            }

            var checksum = ChecksumOf(key, hashing);

            if (byChecksum.TryGetValue(checksum, out var collidesWith))
            {
                problems.Add(new LocTranslationProblemDto(key, null, LocProblemSeverity.Error,
                    CollisionMessage(key, keys[collidesWith], collidesWith, checksum), i));
                continue;
            }

            byChecksum[checksum] = i;

            // Only reached when the checksums differ, so these really are two separate entries.
            if (byCasing.TryGetValue(key, out var differsInCaseFrom))
                problems.Add(new LocTranslationProblemDto(key, null, LocProblemSeverity.Warning,
                    $"'{key}' differs from '{keys[differsInCaseFrom]}' on row {differsInCaseFrom + 1} "
                    + "only in case. The game treats them as two separate entries, so both are read - "
                    + "but nothing else will make it obvious which is which.", i));
            else
                byCasing[key] = i;
        }

        return problems;
    }

    /// <summary>
    ///     The characters of <paramref name="key" /> that ASCII cannot hold, phrased for a message,
    ///     or empty when there are none.
    ///     <para>
    ///         Named rather than merely counted: "this key has non-ASCII characters" leaves the user
    ///         hunting through a string for something their font may render identically to its plain
    ///         counterpart.
    ///     </para>
    /// </summary>
    private static string NonAsciiIn(string key)
    {
        var offending = new List<char>();
        foreach (var c in key)
            if (!char.IsAscii(c) && !offending.Contains(c))
                offending.Add(c);

        if (offending.Count == 0) return string.Empty;

        var quoted = offending.Select(c => $"'{c}'").ToList();
        return quoted.Count == 1
            ? $"{quoted[0]} "
            : $"{string.Join(", ", quoted.Take(quoted.Count - 1))} and {quoted[^1]} ";
    }

    /// <summary>The engine's identity for a key: CRC32 over its ASCII bytes.</summary>
    public static uint ChecksumOf(string key, ICrc32HashingService hashing)
    {
        return (uint)hashing.GetCrc32(key, DatFileConstants.TextKeyEncoding);
    }

    private static string CollisionMessage(string key, string existing, int existingIndex, uint checksum)
    {
        // An identical key is the ordinary case and deserves the ordinary wording; a genuine hash
        // collision between different spellings needs to say so, or it reads as a false positive.
        return string.Equals(key, existing, StringComparison.Ordinal)
            ? $"'{key}' is already defined on row {existingIndex + 1}. "
              + "Only one of the two would ever be read."
            : $"'{key}' has the same checksum (0x{checksum:X8}) as '{existing}' on row "
              + $"{existingIndex + 1}, so the game cannot tell them apart and only one would ever be "
              + "read.";
    }
}
