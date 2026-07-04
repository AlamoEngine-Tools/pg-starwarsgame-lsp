// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
///     A document text with its HtmlAgilityPack parse and derived lookup structures, produced once
///     and shared by every diagnostics fact producer — a single publish run used to re-parse the
///     same text up to six times. <see cref="LineIndex" /> and <see cref="Lines" /> are built
///     lazily so consumers that never resolve positions pay nothing for them.
/// </summary>
public sealed class ParsedXmlDocument
{
    private LineOffsetIndex? _lineIndex;
    private string[]? _lines;

    private ParsedXmlDocument(string text, HtmlDocument html)
    {
        Text = text;
        Html = html;
    }

    public string Text { get; }

    public HtmlDocument Html { get; }

    public LineOffsetIndex LineIndex => _lineIndex ??= new LineOffsetIndex(Text);

    /// <summary>Raw '\n'-split lines; entries keep a trailing '\r' on CRLF input.</summary>
    public string[] Lines => _lines ??= Text.Split('\n');

    public static ParsedXmlDocument Parse(string text)
    {
        return new ParsedXmlDocument(text, XmlUtility.CreateHtmlDocument(text));
    }
}
