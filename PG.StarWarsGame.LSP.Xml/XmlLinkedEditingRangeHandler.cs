// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlLinkedEditingRangeHandler : LinkedEditingRangeHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlLinkedEditingRangeHandler(IGameWorkspaceHost workspaceHost, IEaWXmlContext eaWXmlContext)
    {
        _workspaceHost = workspaceHost;
        _eaWXmlContext = eaWXmlContext;
    }

    public override Task<LinkedEditingRanges?> Handle(LinkedEditingRangeParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<LinkedEditingRanges?>(null);

        if (!_workspaceHost.TryGet(uri, out var doc))
            return Task.FromResult<LinkedEditingRanges?>(null);

        var line = request.Position.Line;
        var character = request.Position.Character;

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(doc.Text);

        var node = FindNodeAtPosition(htmlDoc, line, character);
        if (node is null)
            return Task.FromResult<LinkedEditingRanges?>(null);

        var openRange = GetOpeningTagNameRange(node);
        var closeRange = GetClosingTagNameRange(node);
        if (openRange is null || closeRange is null)
            return Task.FromResult<LinkedEditingRanges?>(null);

        return Task.FromResult<LinkedEditingRanges?>(new LinkedEditingRanges
        {
            Ranges = new Container<LspRange>(openRange, closeRange),
            WordPattern = @"[a-zA-Z_][a-zA-Z0-9_]*"
        });
    }

    private static HtmlNode? FindNodeAtPosition(HtmlDocument doc, int line, int character)
    {
        foreach (var node in doc.DocumentNode.Descendants().Where(n => n.NodeType == HtmlNodeType.Element))
        {
            if (!XmlUtility.IsOnTagName(node, line, character))
                continue;
            if (node.EndNode is null || ReferenceEquals(node.EndNode, node))
                return null;
            return node;
        }

        return null;
    }

    private static LspRange? GetOpeningTagNameRange(HtmlNode node)
    {
        var openLine = XmlUtility.GetLine(node);
        if (openLine < 0) return null;
        var openCol = XmlUtility.GetOpeningTagStartColumn(node);
        return new LspRange
        {
            Start = new Position(openLine, openCol),
            End = new Position(openLine, openCol + node.Name.Length)
        };
    }

    private static LspRange? GetClosingTagNameRange(HtmlNode node)
    {
        var endNode = node.EndNode;
        if (endNode is null || ReferenceEquals(endNode, node)) return null;
        var closeLine = XmlUtility.GetLine(endNode);
        if (closeLine < 0) return null;
        var closeCol = endNode.LinePosition + 2; // skip </
        return new LspRange
        {
            Start = new Position(closeLine, closeCol),
            End = new Position(closeLine, closeCol + node.Name.Length)
        };
    }

    protected override LinkedEditingRangeRegistrationOptions CreateRegistrationOptions(
        LinkedEditingRangeClientCapabilities capability, ClientCapabilities clientCapabilities)
    {
        return new LinkedEditingRangeRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml")
        };
    }
}