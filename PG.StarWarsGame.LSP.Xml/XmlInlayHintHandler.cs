// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using IFileHelper = PG.StarWarsGame.LSP.Core.Util.IFileHelper;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlInlayHintHandler : InlayHintsHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlInlayHintHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlInlayHintHandler(
        IGameWorkspaceHost workspaceHost,
        IGameIndexService indexService,
        ISchemaProvider schema,
        IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper,
        ILogger<XmlInlayHintHandler> logger)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
        _schema = schema;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<InlayHintContainer?>(null);

        if (!_workspaceHost.TryGet(uri, out var doc))
            return Task.FromResult<InlayHintContainer?>(null);

        var localisation = _indexService.Current.Localisation;
        var range = request.Range;
        var hints = new List<InlayHint>();

        var hapDoc = XmlUtility.CreateHtmlDocument(doc.Text);
        foreach (var node in hapDoc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var line = XmlUtility.GetLine(node);
            if (line < range.Start.Line || line > range.End.Line)
                continue;

            var tagDef = _schema.GetTag(node.Name);
            if (tagDef?.ReferenceKind != ReferenceKind.LocalisationKey)
                continue;

            var key = node.InnerText.Trim();
            if (string.IsNullOrEmpty(key))
                continue;

            var translated = localisation.GetValue(key) ?? key + ": MISSING";

            hints.Add(new InlayHint
            {
                Position = new Position(line, int.MaxValue),
                Label = $"= \"{translated}\""!,
                Kind = InlayHintKind.Type,
                PaddingLeft = true
            });

            _logger.LogDebug("InlayHint: {Key} → \"{Value}\" at line {Line}", key, translated, line);
        }

        return Task.FromResult<InlayHintContainer?>(new InlayHintContainer(hints));
    }

    public override Task<InlayHint> Handle(InlayHint request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(
        InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
    {
        return new InlayHintRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            ResolveProvider = false
        };
    }
}