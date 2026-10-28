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

    /// <summary>
    ///     The object definition named <paramref name="objectId" /> in this document - with its type,
    ///     which the id alone does not carry. Null when the document defines none.
    /// </summary>
    public GameSymbol? DefinitionHere(string objectId)
    {
        return Index.Documents.TryGetValue(DocumentUri, out var document)
            ? document.Symbols.FirstOrDefault(s => s.Kind == GameSymbolKind.XmlObject
                                                   && string.Equals(s.Id, objectId, StringComparison.OrdinalIgnoreCase))
            : null;
    }
}