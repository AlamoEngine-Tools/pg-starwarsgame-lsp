using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlTextDocumentSyncHandlerTests
{
    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlTextDocumentSyncHandler handler, XmlDocumentBuffer buffer) Build()
    {
        var buffer = new XmlDocumentBuffer();
        return (new XmlTextDocumentSyncHandler(buffer, new NoOpDiagnosticsPublisher()), buffer);
    }

    [Fact]
    public async Task DidOpen_SetsDocumentInBuffer()
    {
        var (handler, buffer) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = TestUri, Text = "<Foo/>", LanguageId = "xml", Version = 1
            }
        }, CancellationToken.None);

        Assert.Equal("<Foo/>", buffer.Get(TestUri));
    }

    [Fact]
    public async Task DidChange_UpdatesBuffer()
    {
        var (handler, buffer) = Build();
        buffer.Set(TestUri, "<Old/>");

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = TestUri },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "<New/>" })
        }, CancellationToken.None);

        Assert.Equal("<New/>", buffer.Get(TestUri));
    }

    [Fact]
    public async Task DidChange_EmptyContentChanges_SetsEmptyString()
    {
        var (handler, buffer) = Build();
        buffer.Set(TestUri, "<Old/>");

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = TestUri },
            ContentChanges = new Container<TextDocumentContentChangeEvent>()
        }, CancellationToken.None);

        Assert.Equal(string.Empty, buffer.Get(TestUri));
    }

    [Fact]
    public async Task DidClose_RemovesFromBuffer()
    {
        var (handler, buffer) = Build();
        buffer.Set(TestUri, "<Foo/>");

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Null(buffer.Get(TestUri));
    }

    [Fact]
    public async Task DidSave_DoesNotModifyBuffer()
    {
        var (handler, buffer) = Build();
        buffer.Set(TestUri, "<Preserved/>");

        await handler.Handle(new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Equal("<Preserved/>", buffer.Get(TestUri));
    }

    [Fact]
    public void GetTextDocumentAttributes_ReturnsXmlLanguage()
    {
        var (handler, _) = Build();
        var attrs = handler.GetTextDocumentAttributes(TestUri);
        Assert.Equal("xml", attrs.LanguageId);
    }

    private sealed class NoOpDiagnosticsPublisher : IXmlDiagnosticsPublisher
    {
        public void Publish(DocumentUri uri, string text)
        {
        }

        public void ClearDiagnostics(DocumentUri uri)
        {
        }
    }
}