// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

public sealed class DiagnosticsPublisherBaseTest
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static GameIndex IndexWithDoc(string uri)
    {
        var doc = new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty);
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(uri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static (ConcretePublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeIndexService indexService,
        FakeWorkspaceHost workspaceHost) Build(string extension = ".xml", int debounceMs = 0)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeIndexService();
        var workspaceHost = new FakeWorkspaceHost();
        var publisher = new ConcretePublisher(p => published.Add(p), indexService, workspaceHost, extension,
            debounceMs);
        return (publisher, published, indexService, workspaceHost);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_PublishesForMatchingOpenDocuments()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Add("file:///a.xml", "content");
        workspaceHost.Add("file:///b.lua", "content");

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void OnIndexChanged_SkipsDocumentsWithWrongExtension()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Add("file:///b.lua", "content");

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void OnIndexChanged_ClearsStaleUrisOnClose()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Add("file:///a.xml", "content");
        indexService.Fire(IndexWithDoc("file:///a.xml"));
        published.Clear();

        // Remove doc from workspace host — next fire should clear it
        workspaceHost.Remove("file:///a.xml");
        indexService.Fire(GameIndex.Empty);

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
        Assert.Empty(published[0].Diagnostics);
    }

    [Fact]
    public void OnIndexChanged_DoesNotClearUrisThatRemainsOpen()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Add("file:///a.xml", "text1");
        indexService.Fire(IndexWithDoc("file:///a.xml"));
        var countAfterFirst = published.Count;

        // Doc remains open — fire again; no stale-clear publish expected for it
        indexService.Fire(IndexWithDoc("file:///a.xml"));

        // Second fire: 1 publish for the open doc, no extra stale-clear
        Assert.Equal(countAfterFirst + 1, published.Count);
    }

    // ── debounce tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnIndexChanged_ManyRapidChanges_BatchesToSinglePublish()
    {
        var (_, published, indexService, workspaceHost) = Build(debounceMs: 50);
        workspaceHost.Add("file:///a.xml", "content");

        for (var i = 0; i < 10; i++)
            indexService.Fire(IndexWithDoc("file:///a.xml"));

        await Task.Delay(250);

        Assert.Equal(1, published.Count);
    }

    [Fact]
    public async Task OnIndexChanged_SingleChange_NotPublishedImmediately_ThenPublishedAfterDebounce()
    {
        var (_, published, indexService, workspaceHost) = Build(debounceMs: 50);
        workspaceHost.Add("file:///a.xml", "content");

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Empty(published); // not yet — still within debounce window

        await Task.Delay(250);

        Assert.Single(published); // published after debounce
    }

    [Fact]
    public void OnIndexChanged_SkipsDocumentsWithPublishDiagnosticsFalse()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.AddOrUpdate("file:///a.xml", "content", 1, publishDiagnostics: false);

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Empty(published);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class ConcretePublisher : DiagnosticsPublisherBase
    {
        public ConcretePublisher(
            Action<PublishDiagnosticsParams> publish,
            IGameIndexService indexService,
            IGameWorkspaceHost workspaceHost,
            string extension,
            int debounceMs = 0)
            : base(publish, indexService, workspaceHost, debounceMs)
        {
            FileExtension = extension;
        }

        protected override string FileExtension { get; }

        protected override void PublishForDocument(string uri, string text, GameIndex index)
        {
            Publish(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri),
                Diagnostics = new Container<Diagnostic>()
            });
        }
    }

    private sealed class FakeIndexService  : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(
            ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(
            ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }
        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        public void Fire(GameIndex index)
        {
            IndexChanged?.Invoke(index);
        }
    }

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public IEnumerable<TrackedDocument> All => _docs.Values;

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public void Add(string uri, string text)
        {
            _docs[uri] = new TrackedDocument(uri, text, 1);
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}