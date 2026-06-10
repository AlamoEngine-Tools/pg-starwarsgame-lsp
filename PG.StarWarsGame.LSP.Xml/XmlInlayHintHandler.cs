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
using PG.StarWarsGame.LSP.Xml.InlayHints;
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
    private readonly IXmlInlayHintRegistry _registry;

    public XmlInlayHintHandler(
        IGameWorkspaceHost workspaceHost,
        IGameIndexService indexService,
        ISchemaProvider schema,
        IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper,
        ILogger<XmlInlayHintHandler> logger,
        IXmlInlayHintRegistry registry)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
        _schema = schema;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
        _logger = logger;
        _registry = registry;
    }

    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<InlayHintContainer?>(null);

        if (!_workspaceHost.TryGetOrReadFromDisk(_fileHelper, uri, out var doc))
            return Task.FromResult<InlayHintContainer?>(null);

        var index = _indexService.Current;
        var range = request.Range;
        var hapDoc = XmlUtility.CreateHtmlDocument(doc.Text);
        var hints = new List<InlayHint>();

        foreach (var node in hapDoc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var line = XmlUtility.GetLine(node);
            if (line < range.Start.Line || line > range.End.Line)
                continue;

            var tagDef = _schema.GetTag(node.Name);
            if (tagDef is null)
                continue;

            var ctx = new InlayHintContext(uri, index, _schema, hapDoc, node, tagDef, line);
            foreach (var hint in _registry.Dispatch(ctx))
            {
                _logger.LogDebug("InlayHint at line {Line}", line);
                hints.Add(hint);
            }
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
