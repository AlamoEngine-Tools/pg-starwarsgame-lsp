// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server;

public sealed class GameHoverHandler : HoverHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<GameHoverHandler> _logger;
    private readonly ILuaHoverProvider _lua;
    private readonly IXmlHoverProvider _xml;
    private readonly ILspConfigurationProvider _config;

    public GameHoverHandler(IXmlHoverProvider xml, ILuaHoverProvider lua, IFileHelper fileHelper,
        ILogger<GameHoverHandler> logger, ILspConfigurationProvider config)
    {
        _xml = xml;
        _lua = lua;
        _fileHelper = fileHelper;
        _logger = logger;
        _config = config;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        _logger.LogDebug("Hover request received: {0}", request.TextDocument.Uri);
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            if (!_config.Current.Features.Lua.Hover)
                return Task.FromResult<Hover?>(null);

            _logger.LogDebug("Routing to lua hover handlers.");
            return _lua.Handle(request, ct);
        }

        if (uri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            if (!_config.Current.Features.Xml.Hover)
                return Task.FromResult<Hover?>(null);

            _logger.LogDebug("Routing to xml hover handlers.");
            return _xml.Handle(request, ct);
        }

        _logger.LogWarning("Not supported hover handler URI: {0}", uri);
        return Task.FromResult<Hover?>(null);
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                TextDocumentFilter.ForLanguage("xml"),
                TextDocumentFilter.ForLanguage("lua"))
        };
    }
}