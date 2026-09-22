// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Finding a position in a fixture document, for the requests that are addressed by one.
/// </summary>
/// <remarks>
///     <para>
///         An E2E test has to say WHERE it is hovering or renaming, and the fixtures are real game
///         files that move, so the position is computed rather than written down. Five of these had
///         been copied into thirteen test classes - sixteen copies in all - and the copies were
///         still character-identical, which is luck rather than design: each is off-by-one-prone
///         string offset arithmetic, and a correction applied to one copy would have left twelve
///         tests passing against the old behaviour.
///     </para>
///     <para>
///         Imported with <c>using static</c> so the call sites read exactly as before.
///     </para>
/// </remarks>
public static class DocumentPositions
{
    /// <summary>
    ///     The first child element of the root - the first type container in a game XML file.
    /// </summary>
    public static (int line, int col) FindFirstChildElementPosition(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt <= 0) continue; // skip root (lt==0) and blank lines
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            return (i, lt + 1); // col on first char of tag name
        }

        return (1, 1);
    }

    /// <summary>
    ///     The first grandchild element - the first field tag inside the first type container - so
    ///     hover and completion tests hit a known tag.
    /// </summary>
    public static (int line, int col) FindFirstGrandchildElementPosition(string[] lines)
    {
        var firstChildLine = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt <= 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            firstChildLine = i;
            break;
        }

        if (firstChildLine < 0) return (1, 1);

        for (var i = firstChildLine + 1; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt < 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            return (i, lt + 1);
        }

        return (1, 1);
    }

    /// <summary>
    ///     A value inside <c>&lt;tagName&gt;</c>, on the line the opening tag is on.
    /// </summary>
    /// <remarks>
    ///     Searches from after the opening tag so a value that also appears in the tag name, or in
    ///     an attribute before it, does not win.
    /// </remarks>
    public static (int line, int col) FindXmlTagBodyValuePosition(
        string[] lines, string tagName, string value)
    {
        var tagOpen = $"<{tagName}>";
        for (var i = 0; i < lines.Length; i++)
        {
            var tagIdx = lines[i].IndexOf(tagOpen, StringComparison.OrdinalIgnoreCase);
            if (tagIdx < 0) continue;
            var searchFrom = tagIdx + tagOpen.Length;
            var valueIdx = lines[i].IndexOf(value, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (valueIdx < 0) continue;
            return (i, valueIdx);
        }

        return (-1, -1);
    }

    /// <summary>
    ///     The first occurrence of a value anywhere, for list values that do not share a line with
    ///     their opening tag.
    /// </summary>
    public static (int line, int col) FindFirstOccurrencePosition(string[] lines, string value)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(value, StringComparison.Ordinal);
            if (idx >= 0) return (i, idx);
        }

        return (-1, -1);
    }

    /// <summary>
    ///     The string argument of a Lua call, positioned inside the quotes.
    /// </summary>
    public static (int line, int col) FindLuaStringArgPosition(
        string[] lines, string funcName, string value)
    {
        var marker = $"{funcName}(\"{value}\"";
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            return (i, idx + funcName.Length + 2); // +2 for '(' and '"'
        }

        return (-1, -1);
    }
}
