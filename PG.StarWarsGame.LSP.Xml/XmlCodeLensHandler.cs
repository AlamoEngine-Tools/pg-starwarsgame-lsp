// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.CodeLens;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCodeLensHandler : CodeLensHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlCodeLensHandler> _logger;
    private readonly IXmlCodeLensRegistry _registry;
    private readonly ILspConfigurationProvider _config;

    public XmlCodeLensHandler(
        IGameIndexService indexService,
        ILogger<XmlCodeLensHandler> logger,
        IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper,
        IXmlCodeLensRegistry registry,
        ILspConfigurationProvider config)
    {
        _indexService = indexService;
        _logger = logger;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
        _registry = registry;
        _config = config;
    }

    public override Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Xml.CodeLens)
            return Task.FromResult<CodeLensContainer?>(null);

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<CodeLensContainer?>(null);

        var index = _indexService.Current;
        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return Task.FromResult<CodeLensContainer?>(new CodeLensContainer());

        var lenses = new List<LspCodeLens>();
        foreach (var symbol in docIndex.Symbols)
        {
            if (symbol.Origin is not FileOrigin fo)
                continue;

            var ctx = new CodeLensSymbolContext(symbol, fo, index);
            foreach (var lens in _registry.Dispatch(ctx))
            {
                _logger.LogDebug("CodeLens: {Id} at line {Line}", symbol.Id, fo.Line);
                lenses.Add(lens);
            }
        }

        return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(lenses));
    }

    public override Task<LspCodeLens> Handle(LspCodeLens request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(
        CodeLensCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CodeLensRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            ResolveProvider = false
        };
    }
}