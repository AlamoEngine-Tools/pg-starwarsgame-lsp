using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly XmlDocumentBuffer _buffer;
    private readonly IXmlDiagnosticsPublisher _diagnosticsPublisher;

    public XmlTextDocumentSyncHandler(XmlDocumentBuffer buffer, IXmlDiagnosticsPublisher diagnosticsPublisher)
    {
        _buffer = buffer;
        _diagnosticsPublisher = diagnosticsPublisher;
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var text = request.TextDocument.Text;
        _buffer.Set(request.TextDocument.Uri, text);
        _diagnosticsPublisher.Publish(request.TextDocument.Uri, text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var text = request.ContentChanges.LastOrDefault()?.Text ?? string.Empty;
        _buffer.Set(request.TextDocument.Uri, text);
        _diagnosticsPublisher.Publish(request.TextDocument.Uri, text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        _buffer.Remove(request.TextDocument.Uri);
        _diagnosticsPublisher.ClearDiagnostics(request.TextDocument.Uri);
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