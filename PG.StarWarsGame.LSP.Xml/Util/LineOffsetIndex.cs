// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
///     Precomputed offset→(line, column) lookup over a document text. Building the index walks the
///     text once (O(n)); each <see cref="GetPosition" /> is then a binary search over the line
///     starts (O(log lines)). Use this instead of <see cref="XmlUtility.OffsetToPosition" /> wherever
///     positions are resolved in a loop — the latter rescans the text from the start on every call,
///     which turns per-token position lookups quadratic on large documents.
///     Semantics match <see cref="XmlUtility.OffsetToPosition" /> exactly: lines split on '\n' only
///     ('\r' counts as a column character), a negative offset clamps the column to 0, and an offset
///     past the end resolves against the last line with an unclamped column.
/// </summary>
public sealed class LineOffsetIndex
{
    // Offset of each line's first character; [0] is always 0.
    private readonly int[] _lineStarts;
    private readonly int _textLength;

    public LineOffsetIndex(string text)
    {
        _textLength = text.Length;

        var count = 1;
        foreach (var c in text)
            if (c == '\n')
                count++;

        var starts = new int[count];
        var line = 1;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts[line++] = i + 1;

        _lineStarts = starts;
    }

    public (int Line, int Col) GetPosition(int offset)
    {
        // Only newlines strictly BEFORE the offset count, so search with the same bound the linear
        // scan uses: characters in [0, min(offset, length)).
        var scanEnd = Math.Min(offset, _textLength);

        var idx = Array.BinarySearch(_lineStarts, scanEnd);
        // An exact hit means the offset sits at a line start (the '\n' before it was crossed);
        // otherwise BinarySearch returns the complement of the next-larger index.
        var line = idx >= 0 ? idx : ~idx - 1;
        if (line < 0) line = 0; // negative offsets fall before _lineStarts[0]

        return (line, Math.Max(0, offset - _lineStarts[line]));
    }
}
