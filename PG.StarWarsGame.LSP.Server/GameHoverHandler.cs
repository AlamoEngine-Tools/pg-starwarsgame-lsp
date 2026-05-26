// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server;

public sealed class GameHoverHandler : HoverHandlerBase
{
    private readonly ILuaHoverProvider _lua;
    private readonly IXmlHoverProvider _xml;

    public GameHoverHandler(IXmlHoverProvider xml, ILuaHoverProvider lua)
    {
        _xml = xml;
        _lua = lua;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        return uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? _lua.Handle(request, ct)
            : _xml.Handle(request, ct);
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