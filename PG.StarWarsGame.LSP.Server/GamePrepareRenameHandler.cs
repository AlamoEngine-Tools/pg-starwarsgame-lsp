// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server;

public sealed class GamePrepareRenameHandler : PrepareRenameHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILuaRenameProvider _luaProvider;
    private readonly IXmlRenameProvider _xmlProvider;

    public GamePrepareRenameHandler(
        IGameIndexService indexService,
        IXmlRenameProvider xmlProvider,
        ILuaRenameProvider luaProvider,
        IFileHelper fileHelper)
    {
        _indexService = indexService;
        _xmlProvider = xmlProvider;
        _luaProvider = luaProvider;
        _fileHelper = fileHelper;
    }

    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var index = _indexService.Current;

        if (uri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(_xmlProvider.HandlePrepare(uri, request.Position.Line, request.Position.Character, index));
        if (uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(_luaProvider.HandlePrepare(uri, request.Position.Line, request.Position.Character, index));

        return Task.FromResult<RangeOrPlaceholderRange?>(null);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Language = "xml" },
                new TextDocumentFilter { Language = "lua" }),
            PrepareProvider = true
        };
    }
}
