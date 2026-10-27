// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.HoverStrategies;

[PublicAPI]
public sealed class HoverContext : XmlDocumentHandlerContextBase
{
    public HoverContext(
        string documentUri,
        GameIndex index,
        ISchemaProvider schema,
        HtmlDocument hapDoc,
        HtmlNode rootNode,
        HtmlNode node,
        bool isOnTagName,
        int line,
        int character,
        string locale,
        IMessageCatalog messages)
        : base(documentUri, index, schema, hapDoc)
    {
        RootNode = rootNode;
        Node = node;
        IsOnTagName = isOnTagName;
        Line = line;
        Character = character;
        Locale = locale;
        Messages = messages;
    }

    public HtmlNode RootNode { get; }
    public HtmlNode Node { get; }
    public bool IsOnTagName { get; }
    public int Line { get; }
    public int Character { get; }

    /// <summary>The locale the schema's descriptions and notes are read in.</summary>
    public string Locale { get; }

    /// <summary>The server's own texts: the one injected catalogue (#306).</summary>
    public IMessageCatalog Messages { get; }
}