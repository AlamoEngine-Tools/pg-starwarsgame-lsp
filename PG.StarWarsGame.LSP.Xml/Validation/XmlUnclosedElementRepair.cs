// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Where to put the end tag of an element the author never closed.
/// </summary>
/// <remarks>
///     <para>
///         The game's reader stops at the parent's end tag, so everything between the unclosed start
///         tag and that end tag was read as the element's content. That reading is the thing in
///         question, so it cannot decide where the end tag goes. The author's layout can: a line
///         indented no deeper than the unclosed start tag does not belong inside it, a deeper one
///         does. Every point where a line starts is a candidate; the one that leaves the fewest lines
///         at a depth their indentation contradicts wins, and a tie goes to the latest point, which
///         keeps what the game read.
///     </para>
///     <para>
///         The end tag is written on a line of its own at the element's indentation, or on the
///         element's own line when nothing but its value comes before the chosen point.
///     </para>
/// </remarks>
internal static class XmlUnclosedElementRepair
{
    private const int TabWidth = 4;

    /// <param name="s">The document text.</param>
    /// <param name="unclosed">Unclosed elements, outermost first: name and offset of the start tag's '&lt;'.</param>
    /// <param name="endTagStart">Offset of the end tag the game stopped at.</param>
    /// <param name="eol">The document's line ending.</param>
    public static IReadOnlyList<XmlTextEdit> Plan(
        string s, IReadOnlyList<(string Name, int Start)> unclosed, int endTagStart, string eol)
    {
        var edits = new List<XmlTextEdit>();
        var earliest = 0;
        // Innermost first: an inner element's end tag must come before its parent's.
        for (var u = unclosed.Count - 1; u >= 0; u--)
        {
            var (name, start) = unclosed[u];
            var contentStart = Math.Max(TagEnd(s, start), earliest);
            // A value makes the element a leaf: it closes right after the value.
            if (ValueEnd(s, contentStart, endTagStart) is { } valueEnd)
            {
                edits.Add(new XmlTextEdit(valueEnd, 0, "</" + name + ">"));
                earliest = valueEnd;
                continue;
            }

            var at = BestPoint(s, name, start, contentStart, endTagStart);
            edits.Add(EndTagAt(s, name, start, at, eol));
            earliest = at;
        }

        return edits;
    }

    private static int BestPoint(string s, string name, int elementStart, int contentStart, int endTagStart)
    {
        // Only the content's top-level tags and comments: anything nested is placed by its own
        // parent, and an end tag put inside a child would not close this element at all.
        var topLevel = TopLevelItems(s, contentStart, endTagStart).ToList();

        // An element does not contain one of its own name (5 of 378,125 elements below the root in
        // vanilla do, all in one file), so the end tag goes no later than the first such sibling.
        var limit = topLevel.FirstOrDefault(at => NameAt(s, at) == name, endTagStart);

        var elementIndent = LineStartsWithTag(s, elementStart) ? Indent(s, elementStart) : -1;
        if (elementIndent < 0) return limit;

        var items = topLevel
            .Where(at => LineStartsWithTag(s, at))
            .Select(at => (At: at, Indent: Indent(s, at)))
            .ToList();
        // Nothing indented deeper: the layout does not show what is inside. The other elements of
        // this name do - an empty leaf (<Reward_Param1></Reward_Param1>) and a container written
        // flush with its children (the B-wing's <Unit_Ability>) look the same once the end tag is gone.
        if (!items.Any(x => x.At < limit && x.Indent > elementIndent))
            switch (Evidence(s, name, elementStart))
            {
                case Shape.Leaf:
                    return contentStart;
                case Shape.Container:
                    return limit;
            }

        var candidates = items.Select(x => x.At).Where(at => at < limit).Append(limit).ToList();

        var best = endTagStart;
        var bestScore = int.MaxValue;
        foreach (var point in candidates)
        {
            // Inside the element when before the point; it should be when indented deeper.
            var score = items.Count(x => x.At < point != x.Indent > elementIndent);
            if (score > bestScore || (score == bestScore && point < best)) continue;
            best = point;
            bestScore = score;
        }

        return best;
    }

    private static XmlTextEdit EndTagAt(string s, string name, int elementStart, int point, string eol)
    {
        var endTag = "</" + name + ">";
        var contentEnd = point;
        while (contentEnd > 0 && char.IsWhiteSpace(s[contentEnd - 1])) contentEnd--;

        // Only the value (or nothing) before the point, on the element's own line: close it there.
        if (LineStart(s, contentEnd) <= elementStart)
            return new XmlTextEdit(contentEnd, 0, endTag);

        if (!LineStartsWithTag(s, point))
            return new XmlTextEdit(point, 0, endTag);

        var indent = LineStartsWithTag(s, elementStart)
            ? s[LineStart(s, elementStart)..elementStart]
            : "";
        return new XmlTextEdit(LineStart(s, point), 0, indent + endTag + eol);
    }

    // Offset just past the element's value: the last text before its first tag, comments inside
    // the value skipped as the game skips them. Null when there is no text, so no value.
    private static int? ValueEnd(string s, int from, int to)
    {
        int? end = null;
        var i = from;
        while (i < to)
        {
            if (s[i] == '<')
            {
                if (string.CompareOrdinal(s, i, "<!--", 0, 4) != 0) break;
                var close = s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (close < 0) break;
                i = close + 3;
                continue;
            }

            if (!char.IsWhiteSpace(s[i])) end = i + 1;
            i++;
        }

        return end;
    }

    // Start tags and comments at depth 0 of [from, to). The game's reader got through this span,
    // so its tags balance.
    private static IEnumerable<int> TopLevelItems(string s, int from, int to)
    {
        var depth = 0;
        for (var i = s.IndexOf('<', from); i >= 0 && i < to; i = s.IndexOf('<', i + 1))
        {
            if (string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
            {
                if (depth == 0) yield return i;
                var close = s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (close < 0) yield break;
                i = close + 2;
                continue;
            }

            if (i + 1 >= s.Length || s[i + 1] is '?' or '!') continue;
            if (s[i + 1] == '/')
            {
                depth--;
                continue;
            }

            if (depth == 0) yield return i;
            var end = TagEnd(s, i);
            if (end < 2 || s[end - 2] != '/') depth++;
            i = end - 1;
        }
    }

    private enum Shape
    {
        Unknown,
        Leaf,
        Container
    }

    // What the other elements of this name hold, by majority: a value or nothing (a leaf), or
    // child elements (a container). The unclosed element itself is left out.
    private static Shape Evidence(string s, string name, int except)
    {
        int leaves = 0, containers = 0;
        var open = "<" + name;
        for (var i = s.IndexOf(open, StringComparison.Ordinal);
             i >= 0;
             i = s.IndexOf(open, i + 1, StringComparison.Ordinal))
        {
            var after = i + open.Length;
            if (i == except || after >= s.Length || !(char.IsWhiteSpace(s[after]) || s[after] is '>' or '/')) continue;
            var end = TagEnd(s, i);
            if (s[end - 2] == '/')
            {
                leaves++;
                continue;
            }

            var next = s.IndexOf('<', end);
            if (next < 0) continue;
            if (ValueEnd(s, end, next) is not null || string.CompareOrdinal(s, next, "</", 0, 2) == 0) leaves++;
            else containers++;
        }

        return leaves > containers ? Shape.Leaf : containers > leaves ? Shape.Container : Shape.Unknown;
    }

    // The element name of the start tag at 'at'; empty for a comment.
    private static string NameAt(string s, int at)
    {
        var i = at + 1;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('>' or '/' or '<' or '!')) i++;
        return s[(at + 1)..i];
    }

    // Offset just past the '>' of the tag at 'at', honouring quoted attribute values.
    private static int TagEnd(string s, int at)
    {
        var quote = '\0';
        for (var i = at + 1; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i + 1;
            }
        }

        return s.Length;
    }

    private static int LineStart(string s, int at)
    {
        var i = at;
        while (i > 0 && s[i - 1] != '\n' && s[i - 1] != '\r') i--;
        return i;
    }

    private static bool LineStartsWithTag(string s, int at)
    {
        for (var i = LineStart(s, at); i < at; i++)
            if (s[i] is not (' ' or '\t'))
                return false;
        return true;
    }

    private static int Indent(string s, int at)
    {
        var width = 0;
        for (var i = LineStart(s, at); i < at; i++) width += s[i] == '\t' ? TabWidth : 1;
        return width;
    }
}