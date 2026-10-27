// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server;

public sealed class GameRenameHandler : RenameHandlerBase
{
    private readonly ILuaAnalyzer _analyzer;
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly IProjectLayerMap _layers;
    private readonly ILuaRenameProvider _luaProvider;
    private readonly ILuaSandboxProvider _sandbox;
    private readonly IXmlRenameProvider _xmlProvider;

    // What we do not claim (locals, fields) is the analyzer's to rename - within the script's own
    // state, and within the project only, checked against the layers.
    public GameRenameHandler(
        IGameIndexService indexService,
        IXmlRenameProvider xmlProvider,
        ILuaRenameProvider luaProvider,
        IFileHelper fileHelper,
        ILspConfigurationProvider config,
        ILuaAnalyzer analyzer,
        IProjectLayerMap layers,
        ILuaSandboxProvider sandbox)
    {
        _sandbox = sandbox;
        _indexService = indexService;
        _xmlProvider = xmlProvider;
        _luaProvider = luaProvider;
        _fileHelper = fileHelper;
        _config = config;
        _analyzer = analyzer;
        _layers = layers;
    }

    public override async Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var index = _indexService.Current;

        if (uri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return _xmlProvider.HandleRename(uri, request, index);
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return null;

        if (_luaProvider.Claims(uri, request.Position.Line, request.Position.Character, index))
            return _luaProvider.HandleRename(uri, request, index);

        var edit = await _analyzer.RequestAsync<WorkspaceEdit>("textDocument/rename", request, ct);
        if (edit is null) return null;
        // Another script's state first: its file is no part of this rename, wherever it lives.
        edit = LuaAnalyzerRename.KeepWithinState(edit, _sandbox.Current, uri);
        LuaAnalyzerRename.EnsureWithinProject(edit, _layers, new ConfiguredMessageCatalog(_config));
        return edit;
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