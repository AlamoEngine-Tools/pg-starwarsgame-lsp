// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

public sealed class InlayHintContext : XmlDocumentHandlerContextBase
{
    public InlayHintContext(
        string documentUri, GameIndex index, ISchemaProvider schema,
        HtmlDocument hapDoc, HtmlNode node, XmlTagDefinition tagDef, int line,
        int lineEndCharacter = 0)
        : base(documentUri, index, schema, hapDoc)
    {
        Node = node;
        TagDef = tagDef;
        Line = line;
        LineEndCharacter = lineEndCharacter;
    }

    public HtmlNode Node { get; }
    public XmlTagDefinition TagDef { get; }
    public int Line { get; }

    /// <summary>
    ///     Character offset just past the last character of <see cref="Line" />, for hints that want
    ///     to sit at the end of the line. <c>int.MaxValue</c> is out of range per the LSP spec: clients
    ///     clamp it for rendering, but an anchor beyond the line can break interaction with a
    ///     clickable label part.
    /// </summary>
    public int LineEndCharacter { get; }
}