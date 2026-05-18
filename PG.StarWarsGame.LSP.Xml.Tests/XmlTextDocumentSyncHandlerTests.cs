using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlTextDocumentSyncHandlerTests
{
    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlTextDocumentSyncHandler handler,
                    FakeGameWorkspaceHost host,
                    FakeGameIndexService index) Build()
    {
        var host  = new FakeGameWorkspaceHost();
        var index = new FakeGameIndexService();
        return (new XmlTextDocumentSyncHandler(host, index), host, index);
    }

    // ── DidOpen ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidOpen_Adds_Document_To_WorkspaceHost()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "<Foo/>", LanguageId = "xml", Version = 1 }
        }, CancellationToken.None);

        Assert.Single(host.AddOrUpdateCalls);
        Assert.Equal(TestUri.ToString(), host.AddOrUpdateCalls[0].Uri);
        Assert.Equal("<Foo/>", host.AddOrUpdateCalls[0].Text);
        Assert.Equal(1, host.AddOrUpdateCalls[0].Version);
    }

    [Fact]
    public async Task DidOpen_Triggers_Index_Update()
    {
        var (handler, _, index) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "<Foo/>", LanguageId = "xml", Version = 1 }
        }, CancellationToken.None);

        Assert.Single(index.UpdateCalls);
        Assert.Equal(TestUri.ToString(), index.UpdateCalls[0].Uri);
        Assert.Equal("<Foo/>", index.UpdateCalls[0].Text);
        Assert.Equal(1, index.UpdateCalls[0].Version);
    }

    // ── DidChange ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidChange_Updates_WorkspaceHost_With_New_Text()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = TestUri, Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "<New/>" })
        }, CancellationToken.None);

        Assert.Single(host.AddOrUpdateCalls);
        Assert.Equal("<New/>", host.AddOrUpdateCalls[0].Text);
        Assert.Equal(2, host.AddOrUpdateCalls[0].Version);
    }

    [Fact]
    public async Task DidChange_Triggers_Index_Update()
    {
        var (handler, _, index) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = TestUri, Version = 3 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "<Changed/>" })
        }, CancellationToken.None);

        Assert.Single(index.UpdateCalls);
        Assert.Equal("<Changed/>", index.UpdateCalls[0].Text);
        Assert.Equal(3, index.UpdateCalls[0].Version);
    }

    [Fact]
    public async Task DidChange_EmptyContentChanges_Uses_EmptyString()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = TestUri },
            ContentChanges = new Container<TextDocumentContentChangeEvent>()
        }, CancellationToken.None);

        Assert.Equal(string.Empty, host.AddOrUpdateCalls[0].Text);
    }

    // ── DidClose ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidClose_Removes_Document_From_WorkspaceHost()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Single(host.RemoveCalls);
        Assert.Equal(TestUri.ToString(), host.RemoveCalls[0]);
    }

    [Fact]
    public async Task DidClose_Removes_Document_From_Index()
    {
        var (handler, _, index) = Build();

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Single(index.RemoveCalls);
        Assert.Equal(TestUri.ToString(), index.RemoveCalls[0]);
    }

    // ── DidSave ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidSave_Does_Not_Touch_Host_Or_Index()
    {
        var (handler, host, index) = Build();

        await handler.Handle(new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Empty(host.AddOrUpdateCalls);
        Assert.Empty(index.UpdateCalls);
    }

    // ── GetTextDocumentAttributes ─────────────────────────────────────────────

    [Fact]
    public void GetTextDocumentAttributes_ReturnsXmlLanguage()
    {
        var (handler, _, _) = Build();
        var attrs = handler.GetTextDocumentAttributes(TestUri);
        Assert.Equal("xml", attrs.LanguageId);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        public record Call(string Uri, string Text, int Version);
        public List<Call> AddOrUpdateCalls { get; } = [];
        public List<string> RemoveCalls { get; } = [];

        public void AddOrUpdate(string uri, string text, int version)
            => AddOrUpdateCalls.Add(new Call(uri, text, version));

        public void Remove(string uri) => RemoveCalls.Add(uri);

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            doc = null!;
            return false;
        }

        public IEnumerable<TrackedDocument> All => [];
    }

    internal sealed class FakeGameIndexService : IGameIndexService
    {
        public record UpdateCall(string Uri, string Text, int Version);
        public List<UpdateCall> UpdateCalls { get; } = [];
        public List<string> RemoveCalls { get; } = [];

        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged { add { } remove { } }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            UpdateCalls.Add(new UpdateCall(uri, text, version));
            return Task.CompletedTask;
        }

        public void RemoveDocument(string uri) => RemoveCalls.Add(uri);
        public void ApplyBaseline(BaselineIndex baseline) { }
        public IDisposable BeginBulkUpdate() => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
