using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly IGameWorkspaceHost _workspaceHost;
    private readonly IGameIndexService _indexService;

    public XmlTextDocumentSyncHandler(IGameWorkspaceHost workspaceHost, IGameIndexService indexService)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri     = request.TextDocument.Uri.ToString();
        var text    = request.TextDocument.Text;
        var version = request.TextDocument.Version ?? 0;

        _workspaceHost.AddOrUpdate(uri, text, version);
        await _indexService.UpdateDocumentAsync(uri, text, version, ct);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri     = request.TextDocument.Uri.ToString();
        var text    = request.ContentChanges.LastOrDefault()?.Text ?? string.Empty;
        var version = request.TextDocument.Version ?? 0;

        _workspaceHost.AddOrUpdate(uri, text, version);
        await _indexService.UpdateDocumentAsync(uri, text, version, ct);
        return Unit.Value;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        _workspaceHost.Remove(uri);
        _indexService.RemoveDocument(uri);
        return Unit.Task;
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