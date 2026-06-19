// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml;

public abstract class XmlDocumentHandlerContextBase : XmlHandlerContextBase
{
    public XmlDocumentHandlerContextBase(
        string documentUri, GameIndex index, ISchemaProvider schema,
        HtmlDocument hapDoc)
        : base(documentUri, index, schema)
    {
        HapDoc = hapDoc;
    }

    public HtmlDocument HapDoc { get; }
}