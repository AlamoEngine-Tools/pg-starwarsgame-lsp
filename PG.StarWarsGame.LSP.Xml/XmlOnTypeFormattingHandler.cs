// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

/// <summary>
///     Auto-closes an XML element as the user types the <c>&gt;</c> that ends its opening tag: when
///     the cursor sits immediately after a freshly typed <c>&gt;</c> completing a start tag, a matching
///     <c>&lt;/Name&gt;</c> is inserted at the cursor, leaving the caret between the two tags.
///     <para>
///         Implemented as <c>textDocument/onTypeFormatting</c> so it works in any LSP client. Note the
///         VS Code catch: onTypeFormatting only fires when <c>editor.formatOnType</c> is enabled, which
///         is off by default - the feature is dormant until the user turns that setting on.
///     </para>
/// </summary>
public sealed class XmlOnTypeFormattingHandler : DocumentOnTypeFormattingHandlerBase
{
    private const string TriggerCharacter = ">";

    private readonly ILspConfigurationProvider _config;
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IXmlParseCache _parseCache;

    public XmlOnTypeFormattingHandler(IXmlParseCache parseCache, IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper, ILspConfigurationProvider config)
    {
        _parseCache = parseCache;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
        _config = config;
    }

    public override Task<TextEditContainer?> Handle(DocumentOnTypeFormattingParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Xml.AutoCloseTag)
            return Empty();

        // Defensive: we only ever react to the '>' that closes an opening tag.
        if (request.Character != TriggerCharacter)
            return Empty();

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Empty();

        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return Empty();

        var text = parsed.Text;
        var cursorOffset = XmlUtility.PositionToOffset(text, request.Position.Line, request.Position.Character);

        // The just-typed character must be the '>' immediately before the cursor.
        if (cursorOffset < 1 || cursorOffset > text.Length || text[cursorOffset - 1] != '>')
            return Empty();

        // Skip tags that never take a paired close: self-closing (<Foo/>), processing
        // instructions (<?xml?>) and comments (<!-- -->) all end with a distinctive char before '>'.
        if (cursorOffset >= 2 && text[cursorOffset - 2] is '/' or '?' or '-')
            return Empty();

        // The opening tag we just finished is the element whose inner content starts at the cursor.
        var node = FindElementOpeningAt(parsed.Html, cursorOffset);
        if (node is null)
            return Empty();

        // Already has a real closing tag (e.g. the user re-typed the '>' of an existing element) -
        // inserting another would duplicate it.
        if (HasExplicitCloseTag(node))
            return Empty();

        var name = XmlUtility.GetOriginalTagName(node, text);
        if (string.IsNullOrWhiteSpace(name))
            return Empty();

        var at = request.Position;
        var edit = new TextEdit
        {
            Range = new LspRange { Start = at, End = at },
            NewText = $"</{name}>"
        };
        return Task.FromResult<TextEditContainer?>(new TextEditContainer(edit));
    }

    private static HtmlNode? FindElementOpeningAt(HtmlDocument doc, int cursorOffset)
    {
        return doc.DocumentNode.Descendants()
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element && n.InnerStartIndex == cursorOffset);
    }

    private static bool HasExplicitCloseTag(HtmlNode node)
    {
        var endNode = node.EndNode;
        return endNode is not null && !ReferenceEquals(endNode, node) && endNode.Line >= node.Line;
    }

    private static Task<TextEditContainer?> Empty()
    {
        return Task.FromResult<TextEditContainer?>(null);
    }

    protected override DocumentOnTypeFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentOnTypeFormattingCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentOnTypeFormattingRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            FirstTriggerCharacter = TriggerCharacter
        };
    }
}
