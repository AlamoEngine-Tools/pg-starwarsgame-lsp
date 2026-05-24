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
    private readonly IGameIndexService _indexService;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlTextDocumentSyncHandler(IGameWorkspaceHost workspaceHost, IGameIndexService indexService,
        IFileHelper fileHelper, IEaWXmlContext eaWXmlContext)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
        _fileHelper = fileHelper;
        _eaWXmlContext = eaWXmlContext;
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        if (!_eaWXmlContext.IsEaWXmlFile(uri)) return Unit.Value;

        var text = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        _workspaceHost.AddOrUpdate(uri, text, version);
        await _indexService.UpdateDocumentAsync(uri, text, version, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        if (!_eaWXmlContext.IsEaWXmlFile(uri)) return Unit.Value;

        var text = request.ContentChanges.LastOrDefault()?.Text ?? string.Empty;
        var version = request.TextDocument.Version ?? 0;

        _workspaceHost.AddOrUpdate(uri, text, version);
        await _indexService.UpdateDocumentAsync(uri, text, version, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        if (!_eaWXmlContext.IsEaWXmlFile(uri)) return Unit.Value;

        _workspaceHost.Remove(uri);

        var localPath = _fileHelper.FileUriToPath(_fileHelper.NormalizeUri(uri));
        if (localPath is not null && _fileHelper.FileSystem.File.Exists(localPath))
            // File still on disk — restore the saved state so workspace-wide references
            // (cross-file go-to-def, unresolved-ref diagnostics) keep working after close.
            using (_indexService.BeginBulkUpdate())
            {
                _indexService.RemoveDocument(uri);
                var text = await _fileHelper.FileSystem.File.ReadAllTextAsync(localPath, ct);
                await _indexService.UpdateDocumentAsync(uri, text, 0, ct);
            }
        else
            // File was deleted from disk — remove it entirely from the index.
            _indexService.RemoveDocument(uri);

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