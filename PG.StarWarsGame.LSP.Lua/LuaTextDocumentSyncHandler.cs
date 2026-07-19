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
        if (!IsLuaDocument(uri)) return Unit.Value;
        var text = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        // Buffered during startup; replayed once the Lua schema and index are ready.
        await _gate.RunOrBufferAsync(async token =>
        {
            _workspaceHost.AddOrUpdate(uri, text, version);
            // Open (not Update): client versions restart at 1 per open session, while the didClose
            // re-index below preserves the committed version - the open starts a new version epoch.
            await _indexService.OpenDocumentAsync(uri, text, version, token);
        }, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!IsLuaDocument(uri)) return Unit.Value;
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
        if (!IsLuaDocument(uri)) return Unit.Value;

        await _gate.RunOrBufferAsync(async token =>
        {
            _workspaceHost.Remove(uri);

            var localPath = _fileHelper.FileUriToPath(_fileHelper.NormalizeUri(uri));
            if (localPath is not null && _fileHelper.FileSystem.File.Exists(localPath))
            {
                // File still on disk - restore the saved state in the INDEX so workspace-wide
                // references keep working after close; the host tracks only open documents.
                // Never remove-then-re-add here: the removal is applied by the bulk merge while
                // the re-add's parse runs asynchronously, and whichever lands last wins - a
                // removal landing last silently deleted the document's symbols from the index.
                // UpdateDocumentAsync alone skips the re-parse when the buffer already matched
                // disk. Pass the current version so an unsaved-edit revert is not dropped as stale.
                var version = _indexService.Current.Documents.GetValueOrDefault(uri)?.Version ?? 0;
                var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(localPath, token);
                await _indexService.UpdateDocumentAsync(uri, text, version, token);
            }
            else
            {
                // File was deleted from disk - remove it entirely from the index.
                _indexService.RemoveDocument(uri);
            }
        }, ct);

        return Unit.Value;
    }

    // OmniSharp routes didChange/didClose by tracked document attributes, not by the language id
    // of the original didOpen, so this handler also receives notifications for XML documents.
    // Processing them here raced the XML handler's own close flow and could strip the document
    // from the index (a Lua-side removal landing after the XML-side re-add).
    private static bool IsLuaDocument(string uri)
    {
        return uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
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