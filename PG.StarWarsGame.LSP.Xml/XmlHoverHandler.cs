// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlHoverHandler : IXmlHoverProvider
{
    private readonly ILspConfigurationProvider _config;
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlHoverHandler> _logger;
    private readonly IXmlHoverStrategyRegistry _registry;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlHoverHandler(
        IGameWorkspaceHost workspaceHost,
        IGameIndexService indexService,
        ISchemaProvider schema,
        ILspConfigurationProvider config,
        ILogger<XmlHoverHandler> logger,
        IFileHelper fileHelper,
        IEaWXmlContext eaWXmlContext,
        IXmlHoverStrategyRegistry registry)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
        _schema = schema;
        _config = config;
        _logger = logger;
        _fileHelper = fileHelper;
        _eaWXmlContext = eaWXmlContext;
        _registry = registry;
    }

    public Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        _logger.LogDebug("Hover request at {Line}:{Character}",
            request.Position.Line, request.Position.Character);

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<Hover?>(null);
        if (!_workspaceHost.TryGetOrReadFromDisk(_fileHelper, uri, out var doc))
            return Task.FromResult<Hover?>(null);
        var hapDoc = XmlUtility.CreateHtmlDocument(doc.Text);

        if (!XmlUtility.TryGetRootNode(hapDoc, out var rootNode) ||
            XmlUtility.GetLine(rootNode) == request.Position.Line)
            return Task.FromResult<Hover?>(null);

        var lineIndex = request.Position.Line;
        var charPos = request.Position.Character;

        if (!XmlUtility.TryFindNode(hapDoc, lineIndex, out var node) &&
            !XmlUtility.TryFindNodeByClosingLine(hapDoc, lineIndex, out node))
        {
            _logger.LogWarning(
                "Hover request at {Line}:{Character} produced no result, because the tag could not be found.",
                lineIndex, charPos);
            return Task.FromResult<Hover?>(null);
        }

        var isOnTagName = XmlUtility.IsOnTagName(node!, lineIndex, charPos);
        var locale = _config.Current.Locale;
        var index = _indexService.Current;

        var ctx = new HoverContext(uri, index, _schema, hapDoc, rootNode!, node!, isOnTagName,
            lineIndex, charPos, locale);

        var hover = _registry.Dispatch(ctx);
        if (hover is null)
            _logger.LogWarning(
                "Hover request at {Line}:{Character} produced no result, because the tag could not be found.",
                lineIndex, charPos);
        else
            _logger.LogDebug("Hover resolved at {Line}:{Character}", lineIndex, charPos);

        return Task.FromResult(hover);
    }
}