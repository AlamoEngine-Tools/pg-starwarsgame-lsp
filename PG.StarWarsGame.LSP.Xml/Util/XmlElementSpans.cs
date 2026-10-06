// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>One element's source spans, as (offset, length) into the document text.</summary>
public sealed class XmlElementSpan
{
    internal XmlElementSpan(string name, (int Start, int Length) startTag, (int Start, int Length) startName,
        (int Start, int Length)? endTag, (int Start, int Length)? endName)
    {
        Name = name;
        StartTag = startTag;
        StartName = startName;
        EndTag = endTag;
        EndName = endName;
    }

    /// <summary>The name as written in the start tag.</summary>
    public string Name { get; }

    public (int Start, int Length) StartTag { get; }
    public (int Start, int Length) StartName { get; }

    /// <summary>The matching end tag; null for a self-closing element or one never closed.</summary>
    public (int Start, int Length)? EndTag { get; }

    public (int Start, int Length)? EndName { get; }

    public XmlElementSpan? Parent { get; internal set; }

    /// <summary>First offset of the element.</summary>
    public int Start => StartTag.Start;

    /// <summary>Offset just past the element: past its end tag, or past its start tag when it has none.</summary>
    public int End => EndTag is { } e ? e.Start + e.Length : StartTag.Start + StartTag.Length;
}

/// <summary>
///     Exact spans of every element and comment in a document. Which start tag pairs with which end
///     tag is the lenient tree's decision, so malformed documents still get spans; every POSITION is
///     taken from the text at the node's stream offset, because HtmlAgilityPack's per-node line and
///     column drift for nested elements. An end tag is only accepted when the text there really is
///     <c>&lt;/name</c> for the element's own name.
/// </summary>
public sealed class XmlElementSpans
{
    private XmlElementSpans(IReadOnlyList<XmlElementSpan> elements, IReadOnlyList<(int Start, int Length)> comments)
    {
        Elements = elements;
        Comments = comments;
    }

    /// <summary>Every element in document order.</summary>
    public IReadOnlyList<XmlElementSpan> Elements { get; }

    /// <summary>Every <c>&lt;!-- --&gt;</c> comment.</summary>
    public IReadOnlyList<(int Start, int Length)> Comments { get; }

    /// <summary>The innermost element whose span contains <paramref name="offset" />, or null.</summary>
    public XmlElementSpan? ElementAt(int offset)
    {
        XmlElementSpan? best = null;
        foreach (var e in Elements)
            if (e.Start <= offset && offset < e.End && (best is null || e.Start >= best.Start))
                best = e;
        return best;
    }

    public static XmlElementSpans Build(HtmlDocument doc, string text)
    {
        var elements = new List<XmlElementSpan>();
        var comments = new List<(int, int)>();
        var byNode = new Dictionary<HtmlNode, XmlElementSpan>();

        foreach (var node in doc.DocumentNode.Descendants())
        {
            var at = node.StreamPosition;
            if (at < 0 || at >= text.Length) continue;

            if (node.NodeType == HtmlNodeType.Comment)
            {
                if (string.CompareOrdinal(text, at, "<!--", 0, 4) != 0) continue;
                var close = text.IndexOf("-->", at + 4, StringComparison.Ordinal);
                comments.Add((at, (close < 0 ? text.Length : close + 3) - at));
                continue;
            }

            if (node.NodeType != HtmlNodeType.Element || text[at] != '<') continue;

            var nameLength = NameLength(text, at + 1);
            if (nameLength == 0) continue;
            var name = text.Substring(at + 1, nameLength);
            var startTagEnd = TagEnd(text, at);
            var span = new XmlElementSpan(name, (at, startTagEnd - at), (at + 1, nameLength),
                EndTagOf(node, name, text, out var endName), endName);
            elements.Add(span);
            byNode[node] = span;
        }

        foreach (var (node, span) in byNode)
            for (var p = node.ParentNode; p is not null; p = p.ParentNode)
                if (byNode.TryGetValue(p, out var parent))
                {
                    span.Parent = parent;
                    break;
                }

        return new XmlElementSpans(elements, comments);
    }

    private static (int, int)? EndTagOf(HtmlNode node, string name, string text, out (int, int)? endName)
    {
        endName = null;
        var end = node.EndNode;
        if (end is null || ReferenceEquals(end, node)) return null;
        var at = end.StreamPosition;
        if (at < 0 || at + 2 + name.Length > text.Length) return null;
        if (text[at] != '<' || text[at + 1] != '/') return null;
        // The lenient tree folds case; the span records only an end tag that names this element.
        if (string.Compare(text, at + 2, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0) return null;
        if (NameLength(text, at + 2) != name.Length) return null;
        endName = (at + 2, name.Length);
        return (at, TagEnd(text, at) - at);
    }

    private static int NameLength(string text, int from)
    {
        var i = from;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('>' or '/' or '<')) i++;
        return i - from;
    }

    // Offset just past the '>' closing the tag at 'at', honouring quoted attribute values.
    private static int TagEnd(string text, int at)
    {
        var quote = '\0';
        for (var i = at + 1; i < text.Length; i++)
        {
            var c = text[i];
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

        return text.Length;
    }
}
