// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>What the game's reader makes of a document: the error that drops it, or what it tolerated.</summary>
public sealed record XmlGameReadResult(XmlStructureError? Error, IReadOnlyList<XmlStructureError> Tolerances);

/// <summary>
///     Emulates the game's own XML reader, rule for rule as measured in the game:
///     <list type="bullet">
///         <item>anything before the first <c>&lt;</c> is skipped; that <c>&lt;</c> must open the
///         declaration (<c>&lt;?</c>), which is skipped to the next <c>&gt;</c>;</item>
///         <item>one root element, and it must have a child;</item>
///         <item>an attribute is <c>name="value"</c>: an <c>=</c> and a double quote are required;</item>
///         <item>a value is the text up to the next <c>&lt;</c> that does not open a comment; a
///         comment inside a value is cut out and the text on both sides joined; the value is
///         trimmed of space, tab, CR and LF only;</item>
///         <item>character data after a child element is skipped with a message;</item>
///         <item>an end tag must repeat the start tag byte for byte;</item>
///         <item><c>--</c> inside a comment, or a comment left open, is an error;</item>
///         <item>any error drops the whole file.</item>
///     </list>
///     Where the two games differ the stricter one is the rule: EaW reads a mismatched end tag
///     with a message, FoC drops the file, so a mismatch is an error here.
///     Constructs whose handling was not measured (processing instructions, CDATA, DOCTYPE, text
///     outside the root) are stepped over; the strict XML pass reports them.
/// </summary>
public static class XmlGameReader
{
    public static XmlGameReadResult Read(string text)
    {
        var reader = new Reader(text);
        var error = reader.Run();
        return new XmlGameReadResult(error, reader.Tolerances);
    }

    private sealed class Fail(
        XmlStrictnessCategory category,
        int position,
        int length,
        string reason,
        XmlRepair? repair = null)
        : Exception(reason)
    {
        public XmlStrictnessCategory Category { get; } = category;
        public int Position { get; } = position;
        public int Length { get; } = length;
        public XmlRepair? Repair { get; } = repair;
    }

    private sealed class Reader(string s)
    {
        private readonly List<(string Name, int Start)> _open = [];
        private ((string Name, int Start) Outer, int Inner)? _selfNesting;

        public List<XmlStructureError> Tolerances { get; } = [];

        public XmlStructureError? Run()
        {
            try
            {
                ReadDocument();
                return null;
            }
            catch (Fail f)
            {
                var (line, col) = Position(f.Position);
                return new XmlStructureError(line, col, f.Message, f.Category, Math.Max(1, f.Length), f.Repair);
            }
        }

        private string Eol => s.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        private void ReadDocument()
        {
            var pos = s.IndexOf('<');
            if (pos < 0 || pos + 1 >= s.Length || s[pos + 1] != '?')
            {
                // After a byte order mark, which must stay first.
                var at = s.Length > 0 && s[0] == (char)0xFEFF ? 1 : 0;
                throw new Fail(XmlStrictnessCategory.MissingDeclaration, Math.Max(0, pos), 1,
                    "No XML declaration: The game requires '<?xml ...?>' before the root element and drops the whole file",
                    Single("Insert the XML declaration", at, 0, "<?xml version=\"1.0\"?>" + Eol));
            }

            pos = s.IndexOf('>', pos);
            if (pos < 0)
                throw new Fail(XmlStrictnessCategory.UnexpectedEndOfFile, s.Length, 1,
                    "File ends inside the XML declaration: The game drops the whole file");
            pos++;

            var rootSeen = false;
            var rootHasChildren = false;
            var rootStart = 0;
            while (true)
            {
                pos = SkipTo(pos, '<');
                if (pos >= s.Length) break;
                if (StartsWith(pos, "<!--"))
                {
                    pos = SkipComment(pos);
                    continue;
                }

                if (StartsWith(pos, "<?"))
                {
                    pos = SkipPast(pos, "?>");
                    continue;
                }

                if (StartsWith(pos, "<!"))
                {
                    pos = SkipPast(pos, ">");
                    continue;
                }

                if (StartsWith(pos, "</"))
                {
                    var gt = s.IndexOf('>', pos);
                    throw new Fail(XmlStrictnessCategory.StrayEndTag, pos, 2,
                        "End tag outside any element: The game drops the whole file",
                        gt < 0 ? null : Single("Remove the end tag", pos, gt + 1 - pos, ""));
                }

                if (rootSeen)
                    throw new Fail(XmlStrictnessCategory.MultipleRoots, pos, NameLength(pos + 1) + 1,
                        "Second root element: The game reads one root per file and drops the whole file");
                rootSeen = true;
                rootStart = pos;
                rootHasChildren = ReadElement(ref pos);
            }

            if (!rootSeen)
                throw new Fail(XmlStrictnessCategory.EmptyRoot, s.Length, 1,
                    "No root element: The game drops the whole file");
            if (!rootHasChildren)
                throw new Fail(XmlStrictnessCategory.EmptyRoot, rootStart, NameLength(rootStart + 1) + 1,
                    "Root element without child elements: The game drops the whole file");
        }

        /// <summary>Reads one element at <paramref name="pos" />; returns whether it has a child element.</summary>
        private bool ReadElement(ref int pos)
        {
            var start = pos;
            pos++; // '<'
            if (pos < s.Length && s[pos] == '/')
                throw new Fail(XmlStrictnessCategory.StrayEndTag, start, 2,
                    "End tag where a start tag is expected: The game drops the whole file");
            var nameStart = pos;
            while (pos < s.Length && !IsSpace(s[pos]) && s[pos] != '>' && s[pos] != '/') pos++;
            var name = s[nameStart..pos];

            while (true)
            {
                pos = SkipSpace(pos);
                if (pos >= s.Length)
                    throw new Fail(XmlStrictnessCategory.UnexpectedEndOfFile, s.Length, 1,
                        $"File ends inside the start tag of <{name}>: The game drops the whole file");
                if (s[pos] == '/')
                {
                    if (pos + 1 < s.Length && s[pos + 1] == '>')
                    {
                        pos += 2;
                        return false;
                    }

                    throw new Fail(XmlStrictnessCategory.AttributeSyntax, pos, 1,
                        $"'/' not followed by '>' in <{name}>: The game drops the whole file");
                }

                if (s[pos] == '>')
                {
                    pos++;
                    break;
                }

                ReadAttribute(ref pos, name);
            }

            // Below the root, an element inside one of its own name is where an end tag went missing:
            // the later end tags then pair one level off. Remembered for the repair at the file's end.
            if (_selfNesting is null && _open.Count >= 2 && _open[^1].Name == name)
                _selfNesting = (_open[^1], start);
            _open.Add((name, start));
            var hasChildren = false;
            var valueEnd = ReadValue(ref pos, start);
            while (true)
            {
                if (pos >= s.Length)
                    throw UnclosedAtEndOfFile();
                if (StartsWith(pos, "</"))
                {
                    ReadEndTag(ref pos, name);
                    _open.RemoveAt(_open.Count - 1);
                    if (valueEnd >= 0) CheckUntrimmed(valueEnd);
                    return hasChildren;
                }

                if (StartsWith(pos, "<!--"))
                {
                    // A comment between child elements, or after the value: skipped.
                    pos = SkipComment(pos);
                }
                else if (StartsWith(pos, "<?"))
                {
                    pos = SkipPast(pos, "?>");
                }
                else if (StartsWith(pos, "<!"))
                {
                    pos = SkipPast(pos, ">");
                }
                else
                {
                    ReadElement(ref pos);
                    hasChildren = true;
                    valueEnd = -1;
                }

                // Anything but whitespace before the next '<' is character data after a child.
                var cd = pos;
                pos = SkipTo(pos, '<');
                if (hasChildren && TrimEnd(cd, pos) > SkipSpace(cd))
                {
                    var from = SkipSpace(cd);
                    var to = TrimEnd(cd, pos);
                    Tolerate(XmlStrictnessCategory.CharacterDataAfterChild, from, to - from,
                        "Text after a child element: The game skips it; values must come before the first child",
                        Single("Remove the text the game skips", from, to - from, ""));
                }
            }
        }

        /// <summary>
        ///     Reads the value after a start tag: text up to the next '&lt;' that does not open a
        ///     comment, with comments inside it cut out. Returns the offset just past the value's
        ///     last character, or -1 when the element has no text.
        /// </summary>
        private int ReadValue(ref int pos, int elementStart)
        {
            var lastText = -1;
            pos = SkipTo(pos, '<', ref lastText);
            while (pos < s.Length && StartsWith(pos, "<!--"))
            {
                // A run of comments separated only by whitespace is cut out as one; the value goes
                // on if text follows the run before the next other markup. Text before the run
                // alone makes the comments trailing ones, not part of the value.
                var comments = new List<(int Start, int End)>();
                var next = pos;
                var textAfter = -1;
                while (next < s.Length && StartsWith(next, "<!--") && textAfter < 0)
                {
                    var end = SkipComment(next);
                    comments.Add((next, end));
                    next = SkipTo(end, '<', ref textAfter);
                }

                if (textAfter < 0)
                {
                    // Nothing follows: leave the cursor on the run for the element loop.
                    break;
                }

                if (lastText >= 0)
                    foreach (var (start, end) in comments)
                        Tolerate(XmlStrictnessCategory.CommentInsideValue, start, end - start,
                            "Comment inside a value: The game cuts it out and joins the text on both sides, but standard XML readers return two separate text nodes, so other tools may read a different value",
                            MoveCommentAbove(start, end, elementStart));
                lastText = textAfter;
                pos = next;
            }

            return lastText < 0 ? -1 : lastText + 1;
        }

        private void CheckUntrimmed(int valueEnd)
        {
            // The game trims space, tab, CR and LF; any other whitespace at the end stays in the value.
            var i = valueEnd - 1;
            if (i < 0) return;
            var c = s[i];
            if (char.IsWhiteSpace(c) && !IsSpace(c))
                Tolerate(XmlStrictnessCategory.UntrimmedValueCharacter, i, 1,
                    $"Value ends in U+{(int)c:X4}: The game trims only space, tab, CR and LF, so the character is part of the value it reads",
                    Single($"Remove U+{(int)c:X4} from the end of the value", i, 1, ""));
        }

        /// <summary>
        ///     Moves a comment out of a value to its own line above the element. The game reads the
        ///     same value either way, since it cuts the comment out; standard readers then see one
        ///     text node. A comment alone on its line takes the line with it.
        /// </summary>
        private XmlRepair MoveCommentAbove(int start, int end, int elementStart)
        {
            var comment = s[start..end];
            var lineStart = LineStart(start);
            var lineEnd = s.IndexOf('\n', end);
            var aloneOnLine = OnlySpaceOrTab(lineStart, start) && lineEnd >= 0 && OnlySpaceOrTab(end, lineEnd);
            var removal = aloneOnLine
                ? new XmlTextEdit(lineStart, lineEnd + 1 - lineStart, "")
                : new XmlTextEdit(start, end - start, "");

            var elementLine = LineStart(elementStart);
            var indent = s[elementLine..elementStart];
            var insertion = OnlySpaceOrTab(elementLine, elementStart)
                ? new XmlTextEdit(elementLine, 0, indent + comment + Eol)
                : new XmlTextEdit(elementStart, 0, comment + Eol);
            return new XmlRepair("Move the comment above the element", [insertion, removal]);
        }

        private int LineStart(int pos)
        {
            var nl = pos == 0 ? -1 : s.LastIndexOf('\n', pos - 1);
            return nl + 1;
        }

        private bool OnlySpaceOrTab(int from, int to)
        {
            for (var i = from; i < to; i++)
                if (s[i] is not (' ' or '\t' or '\r'))
                    return false;
            return true;
        }

        private static XmlRepair Single(string title, int start, int length, string newText)
        {
            return new XmlRepair(title, [new XmlTextEdit(start, length, newText)]);
        }

        private void ReadAttribute(ref int pos, string element)
        {
            var nameStart = pos;
            while (pos < s.Length && s[pos] != '=' && !IsSpace(s[pos]) && s[pos] != '>' && s[pos] != '/') pos++;
            var attr = s[nameStart..pos];
            pos = SkipSpace(pos);
            if (pos >= s.Length || s[pos] != '=')
                throw new Fail(XmlStrictnessCategory.AttributeSyntax, nameStart, Math.Max(1, attr.Length),
                    $"Attribute {attr} on <{element}> has no '=': The game drops the whole file");
            pos = SkipSpace(pos + 1);
            if (pos < s.Length && s[pos] == '\'')
            {
                // Double quotes fix it, unless the value holds one itself.
                var closeSingle = s.IndexOf('\'', pos + 1);
                var repair = closeSingle < 0 || s.AsSpan(pos + 1, closeSingle - pos - 1).Contains('"')
                    ? null
                    : new XmlRepair("Use double quotes",
                        [new XmlTextEdit(pos, 1, "\""), new XmlTextEdit(closeSingle, 1, "\"")]);
                throw new Fail(XmlStrictnessCategory.AttributeSyntax, nameStart, Math.Max(1, attr.Length),
                    $"Attribute {attr} on <{element}> uses single quotes: The game requires double quotes and drops the whole file",
                    repair);
            }

            if (pos >= s.Length || s[pos] != '"')
                throw new Fail(XmlStrictnessCategory.AttributeSyntax, nameStart, Math.Max(1, attr.Length),
                    $"Attribute {attr} on <{element}> has no opening '\"': The game drops the whole file");
            var close = s.IndexOf('"', pos + 1);
            if (close < 0)
                throw new Fail(XmlStrictnessCategory.UnexpectedEndOfFile, s.Length, 1,
                    $"File ends inside attribute {attr} of <{element}>: The game drops the whole file");
            pos = close + 1;
        }

        private void ReadEndTag(ref int pos, string name)
        {
            var tagStart = pos;
            var en = pos + 2;
            var ee = en;
            while (ee < s.Length && s[ee] != '>' && !IsSpace(s[ee])) ee++;
            var endName = s[en..ee];
            var close = SkipSpace(ee);
            if (close >= s.Length || s[close] != '>')
                throw new Fail(XmlStrictnessCategory.UnexpectedEndOfFile, tagStart, ee - tagStart,
                    $"End tag </{endName}> has no '>': The game drops the whole file");
            if (!string.Equals(endName, name, StringComparison.Ordinal))
                throw MismatchedEndTag(name, endName, tagStart, en, close + 1);
            pos = close + 1;
        }

        private Fail UnclosedAtEndOfFile()
        {
            if (_selfNesting is { } nesting)
            {
                var (outer, inner) = nesting;
                return new Fail(XmlStrictnessCategory.UnexpectedEndOfFile, outer.Start, outer.Name.Length + 1,
                    $"<{outer.Name}> is never closed: The game pairs every later end tag one level off and drops the whole file",
                    new XmlRepair($"Close <{outer.Name}>", XmlUnclosedElementRepair.Plan(s, [outer], inner, Eol)));
            }

            var unclosed = _open.ToList();
            var (name, start) = unclosed[^1];
            var names = unclosed.AsEnumerable().Reverse().Select(o => $"<{o.Name}>").ToList();
            return new Fail(XmlStrictnessCategory.UnexpectedEndOfFile, start, name.Length + 1,
                $"<{name}> is never closed: The game drops the whole file",
                new XmlRepair(CloseTitle(names), XmlUnclosedElementRepair.Plan(s, unclosed, s.Length, Eol)));
        }

        private static string CloseTitle(IReadOnlyList<string> names)
        {
            return names.Count == 1
                ? $"Close {names[0]}"
                : $"Close {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
        }

        // The game drops the file either way; what differs is the likely cause, and so the repair.
        // An end tag naming an element still open further up is right where it is: the elements in
        // between were never closed. One naming nothing open is a typo, or a tag too many.
        private Fail MismatchedEndTag(string name, string endName, int tagStart, int nameStart, int tagEnd)
        {
            if (string.Equals(endName, name, StringComparison.OrdinalIgnoreCase))
                return new Fail(XmlStrictnessCategory.EndTagCaseMismatch, tagStart, tagEnd - tagStart,
                    $"End tag </{endName}> does not match <{name}>: The game compares them byte for byte and drops the whole file",
                    Single($"Rename the end tag to </{name}>", nameStart, endName.Length, name));

            var ancestor = _open.Count < 2 ? -1 : _open.FindLastIndex(_open.Count - 2, o => o.Name == endName);
            if (ancestor >= 0)
            {
                var unclosed = _open.Skip(ancestor + 1).ToList();
                var edits = XmlUnclosedElementRepair.Plan(s, unclosed, tagStart, Eol);
                var names = unclosed.AsEnumerable().Reverse().Select(o => $"<{o.Name}>").ToList();
                var title = CloseTitle(names);
                var (innerName, innerStart) = unclosed[^1];
                return new Fail(XmlStrictnessCategory.EndTagMismatch, innerStart, innerName.Length + 1,
                    $"<{innerName}> is never closed: The game reads </{endName}> where it expects </{innerName}> and drops the whole file",
                    new XmlRepair(title, edits));
            }

            var rename = Single($"Rename the end tag to </{name}>", nameStart, endName.Length, name);
            var remove = Single("Remove the end tag", tagStart, tagEnd - tagStart, "");
            return new Fail(XmlStrictnessCategory.EndTagMismatch, tagStart, tagEnd - tagStart,
                $"End tag </{endName}> does not match <{name}>: The game compares them byte for byte and drops the whole file",
                ReadsFurther(remove, rename) ? remove : rename);
        }

        // Whether the game's reader gets further through the text with the first repair than with
        // the second; a repair after which it reads the whole file counts as furthest.
        private bool ReadsFurther(XmlRepair first, XmlRepair second)
        {
            return Reach(first) > Reach(second);

            int Reach(XmlRepair repair)
            {
                var error = XmlGameReader.Read(XmlStructureRepairs.Apply(s, repair)).Error;
                return error is null ? int.MaxValue : error.Line * 100_000 + error.Column;
            }
        }

        private int SkipComment(int pos)
        {
            var body = pos + 4;
            var end = s.IndexOf("-->", body, StringComparison.Ordinal);
            if (end < 0)
                throw new Fail(XmlStrictnessCategory.CommentSyntax, pos, 4,
                    "Comment is never closed: The game drops the whole file");
            var dd = s.IndexOf("--", body, StringComparison.Ordinal);
            if (dd >= 0 && dd < end)
                throw new Fail(XmlStrictnessCategory.CommentSyntax, dd, 2,
                    "'--' inside a comment: The game drops the whole file",
                    Single("Separate the hyphens", dd, 2, "- -"));
            return end + 3;
        }

        private int SkipPast(int pos, string terminator)
        {
            var end = s.IndexOf(terminator, pos, StringComparison.Ordinal);
            if (end < 0)
                throw new Fail(XmlStrictnessCategory.UnexpectedEndOfFile, s.Length, 1,
                    "File ends inside markup: The game drops the whole file");
            return end + terminator.Length;
        }

        private void Tolerate(XmlStrictnessCategory category, int position, int length, string reason,
            XmlRepair? repair)
        {
            var (line, col) = Position(position);
            Tolerances.Add(new XmlStructureError(line, col, reason, category, Math.Max(1, length), repair));
        }

        private int SkipTo(int pos, char c)
        {
            var dummy = -1;
            return SkipTo(pos, c, ref dummy);
        }

        /// <summary>Advances to <paramref name="c" />, noting the last non-whitespace character passed.</summary>
        private int SkipTo(int pos, char c, ref int lastText)
        {
            while (pos < s.Length && s[pos] != c)
            {
                if (!IsSpace(s[pos])) lastText = pos;
                pos++;
            }

            return pos;
        }

        private int SkipSpace(int pos)
        {
            while (pos < s.Length && IsSpace(s[pos])) pos++;
            return pos;
        }

        private int TrimEnd(int from, int to)
        {
            while (to > from && IsSpace(s[to - 1])) to--;
            return to;
        }

        private int NameLength(int pos)
        {
            var i = pos;
            while (i < s.Length && !IsSpace(s[i]) && s[i] != '>' && s[i] != '/') i++;
            return i - pos;
        }

        private bool StartsWith(int pos, string what) =>
            string.CompareOrdinal(s, pos, what, 0, what.Length) == 0;

        // The game's whitespace: space, tab, CR, LF.
        private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n';

        private (int Line, int Column) Position(int offset)
        {
            offset = Math.Clamp(offset, 0, s.Length);
            var line = 0;
            var lineStart = 0;
            for (var i = 0; i < offset; i++)
                if (s[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }

            return (line, offset - lineStart);
        }
    }
}