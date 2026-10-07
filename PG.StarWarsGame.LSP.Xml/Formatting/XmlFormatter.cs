// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Formatting;

/// <summary>The edits that format a document, or why it was not formatted.</summary>
public sealed record XmlFormatResult(IReadOnlyList<XmlTextEdit> Edits, string? Refusal)
{
    public static XmlFormatResult Refused(string reason)
    {
        return new XmlFormatResult([], reason);
    }
}

/// <summary>
///     Formats well-formed XML by re-indenting the whitespace between markup, and nothing else.
///     <para>
///         Only a whitespace run holding a line break, between two tags or comments of an element
///         that has no text of its own, is touched: its trailing whitespace goes, more than one
///         blank line becomes one, and the last line is indented by depth. Values, comments,
///         attributes, entities, same-line layout and every line break kept are left as written,
///         so line endings survive per line. A leaf's whitespace-only value is a value and is kept.
///         An element with text anywhere in it is kept whole: the game joins the text around a
///         comment, so whitespace between its comments is part of the value.
///     </para>
///     <para>
///         Gate: the strict reader. A document standard XML tools reject is refused. The result is
///         checked against the original before it is returned, and refused if what the document
///         reads would change.
///     </para>
/// </summary>
public static class XmlFormatter
{
    private enum Kind
    {
        Start,
        End,
        Other
    }

    /// <param name="doc">The document; its text is what the edits are computed against.</param>
    /// <param name="indentUnit">One level of indentation: a tab, or the editor's spaces.</param>
    /// <param name="range">Only edits lying wholly inside these offsets [start, end] are returned.</param>
    public static XmlFormatResult Format(ParsedXmlDocument doc, string indentUnit, (int Start, int End)? range = null)
    {
        var text = doc.Text;
        if (XmlStructuralValidator.StrictReadError(text) is { } error)
            return XmlFormatResult.Refused(
                $"Formatting refused: Not well-formed XML at line {error.LineNumber}, column {error.LinePosition}");

        var edits = new List<XmlTextEdit>();
        var tokens = Tokens(doc.Spans);
        var mixed = MixedElements(text, tokens);

        var open = new Stack<XmlElementSpan>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var (_, end, kind, element) = tokens[i];
            if (kind == Kind.Start) open.Push(element!);
            else if (kind == Kind.End && open.Count > 0) open.Pop();
            if (i + 1 >= tokens.Count) continue;

            // The gap after this token belongs to whatever is open now.
            var next = tokens[i + 1];
            var enclosing = open.Count > 0 ? open.Peek() : null;
            var leaf = kind == Kind.Start && next.Kind == Kind.End && ReferenceEquals(element, next.Element);
            if (leaf || (enclosing is not null && mixed.Contains(enclosing))) continue;

            var depth = open.Count - (next.Kind == Kind.End ? 1 : 0);
            Reindent(text, end, next.Start, Indent(indentUnit, depth), edits);
        }

        if (range is { } r) edits.RemoveAll(e => e.Start < r.Start || e.Start + e.Length > r.End);
        if (edits.Count == 0) return new XmlFormatResult(edits, null);

        var formatted = XmlStructureRepairs.Apply(text, edits);
        return Signature(doc.Html) == Signature(XmlUtility.CreateHtmlDocument(formatted))
            ? new XmlFormatResult(edits, null)
            : XmlFormatResult.Refused("Formatting refused: The result would change the document's content");
    }

    // Tags and comments in document order. The declaration, CDATA and anything else are not
    // tokens, so they sit inside a gap that holds text and is never touched.
    private static List<(int Start, int End, Kind Kind, XmlElementSpan? Element)> Tokens(XmlElementSpans spans)
    {
        var tokens = new List<(int, int, Kind, XmlElementSpan?)>();
        foreach (var e in spans.Elements)
        {
            var selfClosed = e.EndTag is null;
            tokens.Add(
                (e.StartTag.Start, e.StartTag.Start + e.StartTag.Length, selfClosed ? Kind.Other : Kind.Start, e));
            if (e.EndTag is { } endTag) tokens.Add((endTag.Start, endTag.Start + endTag.Length, Kind.End, e));
        }

        foreach (var c in spans.Comments) tokens.Add((c.Start, c.Start + c.Length, Kind.Other, null));
        tokens.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return tokens;
    }

    // Elements holding non-whitespace text directly, in any gap between their own tokens.
    private static HashSet<XmlElementSpan> MixedElements(string text,
        List<(int Start, int End, Kind Kind, XmlElementSpan? Element)> tokens)
    {
        var mixed = new HashSet<XmlElementSpan>();
        var open = new Stack<XmlElementSpan>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var (_, end, kind, element) = tokens[i];
            if (kind == Kind.Start) open.Push(element!);
            else if (kind == Kind.End && open.Count > 0) open.Pop();

            if (i + 1 >= tokens.Count || open.Count == 0) continue;
            for (var p = end; p < tokens[i + 1].Start; p++)
                if (!IsSpace(text[p]))
                {
                    mixed.Add(open.Peek());
                    break;
                }
        }

        return mixed;
    }

    // A whitespace-only gap with a line break: L0 br L1 br ... br Ln. Becomes "" br [ "" br ] indent,
    // as one minimal edit per line so a range can take the lines it covers.
    private static void Reindent(string text, int from, int to, string indent, List<XmlTextEdit> edits)
    {
        for (var p = from; p < to; p++)
            if (!IsSpace(text[p]))
                return;

        var breaks = new List<(int At, int Length)>();
        for (var p = from; p < to; p++)
            if (text[p] == '\r' && p + 1 < to && text[p + 1] == '\n')
                breaks.Add((p++, 2));
            else if (text[p] is '\r' or '\n')
                breaks.Add((p, 1));
        if (breaks.Count == 0) return;

        Set(edits, text, from, breaks[0].At, "");
        var lastLine = breaks[^1].At + breaks[^1].Length;
        if (breaks.Count >= 2)
        {
            var secondLine = breaks[0].At + breaks[0].Length;
            Set(edits, text, secondLine, breaks[1].At, "");
            Set(edits, text, breaks[1].At + breaks[1].Length, lastLine, "");
        }

        Set(edits, text, lastLine, to, indent);
    }

    private static void Set(List<XmlTextEdit> edits, string text, int start, int end, string replacement)
    {
        if (end - start == replacement.Length &&
            string.CompareOrdinal(text, start, replacement, 0, replacement.Length) == 0) return;
        edits.Add(new XmlTextEdit(start, end - start, replacement));
    }

    private static string Indent(string unit, int depth)
    {
        return depth <= 0 ? "" : new StringBuilder(unit.Length * depth).Insert(0, unit, depth).ToString();
    }

    private static bool IsSpace(char c)
    {
        return c is ' ' or '\t' or '\r' or '\n';
    }

    // What the document reads: every element with its attributes as written, every comment, and
    // every text node that holds more than whitespace, in order.
    private static string Signature(HtmlDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var node in doc.DocumentNode.Descendants())
            switch (node.NodeType)
            {
                case HtmlNodeType.Element:
                    sb.Append('<').Append(node.OriginalName).Append(' ').Append(node.Depth);
                    foreach (var a in node.Attributes)
                        sb.Append(' ').Append(a.OriginalName).Append('=').Append(a.Value);
                    sb.Append('\n');
                    break;
                case HtmlNodeType.Comment:
                    sb.Append('!').Append(node.InnerHtml).Append('\n');
                    break;
                case HtmlNodeType.Text when node.InnerText.Any(c => !IsSpace(c)):
                    sb.Append('"').Append(node.InnerText).Append('\n');
                    break;
            }

        return sb.ToString();
    }
}