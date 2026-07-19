// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     Document sync for story-dialog .txt scripts. Registered for every .txt file but gates all
///     work on the <see cref="IStoryDialogScope" /> registry scope - a .txt outside the pgproj
///     storyDialog directories is never tracked and never produces diagnostics. Dialog documents
///     do not enter the GameIndex (they define no symbols yet), so open/change revalidation is
///     driven directly instead of via index changes.
/// </summary>
public sealed class DialogTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IStartupGate _gate;
    private readonly IDialogDiagnosticsRevalidator _revalidator;
    private readonly IStoryDialogScope _scope;
    private readonly IGameWorkspaceHost _workspaceHost;

    public DialogTextDocumentSyncHandler(
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        IStartupGate gate,
        IStoryDialogScope scope,
        IDialogDiagnosticsRevalidator revalidator)
    {
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _gate = gate;
        _scope = scope;
        _revalidator = revalidator;
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!IsDialogCandidate(uri)) return Unit.Value;
        var text = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        // Buffered during startup; the scope is only known once the pgproj has loaded.
        await _gate.RunOrBufferAsync(async token =>
        {
            if (!_scope.Enabled || !_scope.IsInScope(uri)) return;
            _workspaceHost.AddOrUpdate(uri, text, version);
            await _revalidator.RevalidateDocumentAsync(uri, token);
        }, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!IsDialogCandidate(uri)) return Unit.Value;
        var text = request.ContentChanges.LastOrDefault()?.Text ?? string.Empty;
        var version = request.TextDocument.Version ?? 0;

        await _gate.RunOrBufferAsync(async token =>
        {
            if (!_scope.Enabled || !_scope.IsInScope(uri)) return;
            _workspaceHost.AddOrUpdate(uri, text, version);
            await _revalidator.RevalidateDocumentAsync(uri, token);
        }, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!IsDialogCandidate(uri)) return Unit.Value;

        await _gate.RunOrBufferAsync(token =>
        {
            // Only documents this handler tracked get cleared - scope membership may have
            // changed since open, so the tracked state decides, not the current scope.
            if (_workspaceHost.TryGet(uri, out _))
            {
                _workspaceHost.Remove(uri);
                _revalidator.ClearDocument(uri);
            }

            return Task.CompletedTask;
        }, ct);
        return Unit.Value;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        return Unit.Task;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "plaintext");
    }

    // Like the Lua handler: OmniSharp routes didChange/didClose by tracked attributes, so this
    // handler also sees XML/Lua notifications - the extension guard keeps it out of their flows.
    private static bool IsDialogCandidate(string uri)
    {
        return uri.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.txt"),
            Change = TextDocumentSyncKind.Full
        };
    }
}