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
        HtmlDocument hapDoc, HtmlNode node, XmlTagDefinition tagDef, int line)
        : base(documentUri, index, schema, hapDoc)
    {
        Node = node;
        TagDef = tagDef;
        Line = line;
    }

    public HtmlNode Node { get; }
    public XmlTagDefinition TagDef { get; }
    public int Line { get; }
}