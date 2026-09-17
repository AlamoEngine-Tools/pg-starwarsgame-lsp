// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <inheritdoc />
public sealed class CallstackParser : ICallstackParser
{
    private const int TrailingFields = 4;

    public CallstackFrame? ParseEntry(int level, string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Walk back over the last four colons; whatever precedes them is the source.
        var cut = entry.Length;
        var fields = new string[TrailingFields];
        for (var i = TrailingFields - 1; i >= 0; i--)
        {
            var colon = entry.LastIndexOf(':', cut - 1);
            if (colon < 0)
                return null;
            fields[i] = entry[(colon + 1)..cut];
            cut = colon;
        }

        if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
            return null;

        var source = entry[..cut];
        if (source.StartsWith('@'))
            source = source[1..];
        return new CallstackFrame(level, source, line, fields[1], fields[2], fields[3], entry);
    }

    public IReadOnlyList<CallstackFrame> ParseStack(IReadOnlyList<string> entries, bool dropDuplicateOutermost)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var first = 0;
        if (dropDuplicateOutermost && entries.Count >= 2 &&
            string.Equals(entries[0], entries[1], StringComparison.Ordinal))
            first = 1;

        var frames = new List<CallstackFrame>(entries.Count - first);
        for (var level = entries.Count - 1; level >= first; level--)
            frames.Add(
                ParseEntry(level, entries[level]) ?? new CallstackFrame(level, "", 0, "", "", "", entries[level]));
        return frames;
    }
}