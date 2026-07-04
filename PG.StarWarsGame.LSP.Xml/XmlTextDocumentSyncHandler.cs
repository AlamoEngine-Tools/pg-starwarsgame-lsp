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

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IStartupGate _gate;
    private readonly IGameIndexService _indexService;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlTextDocumentSyncHandler(IGameWorkspaceHost workspaceHost, IGameIndexService indexService,
        IFileHelper fileHelper, IEaWXmlContext eaWXmlContext, IStartupGate gate)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
        _fileHelper = fileHelper;
        _eaWXmlContext = eaWXmlContext;
        _gate = gate;
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var text = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        // While the startup pipeline runs, the gate buffers this open and replays it after the
        // index is built and the EaW directories are known. The IsEaWXmlFile gate is evaluated
        // inside the thunk so it sees the populated context at run time.
        await _gate.RunOrBufferAsync(async token =>
        {
            if (!_eaWXmlContext.IsEaWXmlFile(uri)) return;
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
            if (!_eaWXmlContext.IsEaWXmlFile(uri)) return;
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
            if (!_eaWXmlContext.IsEaWXmlFile(uri)) return;

            _workspaceHost.Remove(uri);

            var localPath = _fileHelper.FileUriToPath(_fileHelper.NormalizeUri(uri));
            if (localPath is not null && _fileHelper.FileSystem.File.Exists(localPath))
            {
                // File still on disk — restore the saved state in the INDEX so workspace-wide
                // references (cross-file go-to-def, unresolved-ref diagnostics) keep working after
                // close; the text itself is not re-added to the host, which tracks only open
                // documents (closed-file consumers read from disk on demand).
                // UpdateDocumentAsync skips the re-parse when the buffer already matched disk (the
                // common case for a viewed-but-unedited file), avoiding an expensive re-index and the
                // symbol-removal flicker that briefly drops resolution back to the baseline. Pass the
                // current version so an unsaved-edit revert is not dropped as stale.
                var version = _indexService.Current.Documents.GetValueOrDefault(uri)?.Version ?? 0;
                var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(localPath, token);
                await _indexService.UpdateDocumentAsync(uri, text, version, token);
            }
            else
            {
                // File was deleted from disk — remove it entirely from the index.
                _indexService.RemoveDocument(uri);
            }
        }, ct);

        return Unit.Value;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        return Unit.Task;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "xml");
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            Change = TextDocumentSyncKind.Full
        };
    }
}