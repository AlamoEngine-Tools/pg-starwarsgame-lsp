// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Core.Xml;

/// <summary>
///     A document text with its HtmlAgilityPack parse and derived lookup structures, produced once
///     and shared by every diagnostics fact producer - a single publish run used to re-parse the
///     same text up to six times. <see cref="LineIndex" /> and <see cref="Lines" /> are built
///     lazily so consumers that never resolve positions pay nothing for them.
/// </summary>
public sealed class ParsedXmlDocument
{
    private LineOffsetIndex? _lineIndex;
    private string[]? _lines;
    private XmlElementSpans? _spans;

    private ParsedXmlDocument(string text, HtmlDocument html)
    {
        Text = text;
        Html = html;
    }

    public string Text { get; }

    public HtmlDocument Html { get; }

    public LineOffsetIndex LineIndex => _lineIndex ??= new LineOffsetIndex(Text);

    /// <summary>Exact element and comment spans, for the structural editor features. Built on first use.</summary>
    public XmlElementSpans Spans => _spans ??= XmlElementSpans.Build(Html, Text);

    /// <summary>Raw '\n'-split lines; entries keep a trailing '\r' on CRLF input.</summary>
    public string[] Lines => _lines ??= Text.Split('\n');

    /// <summary>
    ///     The innermost element whose start tag, content or end tag holds the position (0-based), or
    ///     null. By position rather than by line, so several elements on one line are told apart.
    /// </summary>
    public HtmlNode? ElementAt(int line, int character)
    {
        return Spans.ElementAt(XmlUtility.PositionToOffset(Text, line, character))?.Node;
    }

    /// <summary>
    ///     What removing <paramref name="node" /> takes out, as 0-based positions: its whole lines when
    ///     nothing else shares them, otherwise exactly the element - a file written on one line loses
    ///     the element and not the line. Null for a node with no element span.
    /// </summary>
    public ((int Line, int Col) Start, (int Line, int Col) End)? RemovalRange(HtmlNode node)
    {
        if (Spans.Of(node) is not { } span) return null;

        var lineStart = span.Start == 0 ? 0 : Text.LastIndexOf('\n', span.Start - 1) + 1;
        var newline = Text.IndexOf('\n', span.End);
        var lineEnd = newline < 0 ? Text.Length : newline;
        var alone = Text.AsSpan(lineStart, span.Start - lineStart).IsWhiteSpace()
                    && Text.AsSpan(span.End, lineEnd - span.End).IsWhiteSpace();
        var (start, end) = alone
            ? (lineStart, newline < 0 ? Text.Length : newline + 1)
            : (span.Start, span.End);
        return (LineIndex.GetPosition(start), LineIndex.GetPosition(end));
    }

    public static ParsedXmlDocument Parse(string text)
    {
        return new ParsedXmlDocument(text, XmlUtility.CreateHtmlDocument(text));
    }
}