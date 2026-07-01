// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IStartupGate _gate;
    private readonly IGameIndexService _indexService;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaTextDocumentSyncHandler(
        IGameWorkspaceHost workspaceHost,
        IGameIndexService indexService,
        IFileHelper fileHelper,
        IStartupGate gate)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
        _fileHelper = fileHelper;
        _gate = gate;
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var text = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        // Buffered during startup; replayed once the Lua schema and index are ready.
        await _gate.RunOrBufferAsync(async token =>
        {
            _workspaceHost.AddOrUpdate(uri, text, version);
            await _indexService.UpdateDocumentAsync(uri, text, version, token);
        }, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var text = request.ContentChanges.LastOrDefault()?.Text ?? string.Empty;
        var version = request.TextDocument.Version ?? 0;

        await _gate.RunOrBufferAsync(async token =>
        {
            _workspaceHost.AddOrUpdate(uri, text, version);
            await _indexService.UpdateDocumentAsync(uri, text, version, token);
        }, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());

        await _gate.RunOrBufferAsync(async token =>
        {
            _workspaceHost.Remove(uri);

            var localPath = _fileHelper.FileUriToPath(_fileHelper.NormalizeUri(uri));
            if (localPath is not null && _fileHelper.FileSystem.File.Exists(localPath))
                using (_indexService.BeginBulkUpdate())
                {
                    _indexService.RemoveDocument(uri);
                    var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(localPath, token);
                    await _indexService.UpdateDocumentAsync(uri, text, 0, token);
                    _workspaceHost.AddOrUpdate(uri, text, 0, publishDiagnostics: false);
                }
            else
                _indexService.RemoveDocument(uri);
        }, ct);

        return Unit.Value;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        return Unit.Task;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "lua");
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lua"),
            Change = TextDocumentSyncKind.Full
        };
    }
}