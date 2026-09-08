// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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
        FakeWorkspaceHost workspaceHost) Build(string extension = ".xml", int debounceMs = 0, bool enabled = true)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeIndexService();
        var workspaceHost = new FakeWorkspaceHost();
        var publisher = new ConcretePublisher(p => published.Add(p), indexService, workspaceHost, extension,
            debounceMs, enabled);
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

        // Remove doc from workspace host - next fire should clear it
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

        // Doc remains open - fire again; no stale-clear publish expected for it
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

        Assert.Empty(published); // not yet - still within debounce window

        await Task.Delay(250);

        Assert.Single(published); // published after debounce
    }

    [Fact]
    public void OnIndexChanged_DiagnosticsDisabled_PublishesNothing()
    {
        var (_, published, indexService, workspaceHost) = Build(enabled: false);
        workspaceHost.Add("file:///a.xml", "content");

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Empty(published);
    }

    [Fact]
    public void OnIndexChanged_SkipsDocumentsWithPublishDiagnosticsFalse()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.AddOrUpdate("file:///a.xml", "content", 1, false);

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Empty(published);
    }

    // ── scoped re-publish (content-only edits touch only the edited doc) ──────

    [Fact]
    public void IndexChanged_OnlyOneDocumentEntryChanged_RepublishesOnlyThatDocument()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Add("file:///a.xml", "content");
        workspaceHost.Add("file:///b.xml", "content");

        var docA = new DocumentIndex("file:///a.xml", 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty);
        var docB = new DocumentIndex("file:///b.xml", 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty);
        var index1 = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///a.xml", docA).Add("file:///b.xml", docB)
        };
        indexService.Fire(index1);
        published.Clear();

        // Content-only edit of a.xml: only its Documents entry is replaced; the workspace symbol
        // dictionaries (and all other index fields) keep their references.
        var index2 = index1 with
        {
            Documents = index1.Documents.SetItem("file:///a.xml", docA with { Version = 2 })
        };
        indexService.Fire(index2);

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_WorkspaceDefinitionsChanged_RepublishesAllOpenDocuments()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Add("file:///a.xml", "content");
        workspaceHost.Add("file:///b.xml", "content");

        var index1 = IndexWithDoc("file:///a.xml");
        indexService.Fire(index1);
        published.Clear();

        // A symbol-shape change can invalidate cross-document diagnostics everywhere.
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///a.xml", 1, null), null);
        var index2 = index1 with
        {
            WorkspaceDefinitions = index1.WorkspaceDefinitions.Add("UNIT_A", [sym])
        };
        indexService.Fire(index2);

        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void IndexChanged_NewlyOpenedDocument_PublishedEvenWhenIndexIsIdentical()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Add("file:///a.xml", "content");

        var index = IndexWithDoc("file:///a.xml");
        indexService.Fire(index);
        published.Clear();

        // didOpen of an unedited, already-indexed file re-fires IndexChanged with the same index
        // (the unchanged-content fast path) - the newly opened doc still needs its diagnostics.
        workspaceHost.Add("file:///b.xml", "content");
        indexService.Fire(index);

        Assert.Single(published);
        Assert.Equal("file:///b.xml", published[0].Uri.ToString());
    }

    // ── error isolation ───────────────────────────────────────────────────────

    [Fact]
    public void PublishForDocument_Throws_DoesNotAbortOtherDocuments()
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeIndexService();
        var workspaceHost = new FakeWorkspaceHost();
        // a.xml throws; b.xml should still receive diagnostics
        var publisher = new ThrowingPublisher(p => published.Add(p), indexService, workspaceHost,
            "file:///a.xml");
        workspaceHost.Add("file:///a.xml", "content");
        workspaceHost.Add("file:///b.xml", "content");

        indexService.Fire(GameIndex.Empty);

        Assert.True(published.Any(p => p.Uri.ToString() == "file:///b.xml"),
            "b.xml should receive diagnostics even though a.xml threw");
    }

    [Fact]
    public void PublishForDocument_Throws_EmptyDiagnosticsPublishedForFailedDocument()
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeIndexService();
        var workspaceHost = new FakeWorkspaceHost();
        var publisher = new ThrowingPublisher(p => published.Add(p), indexService, workspaceHost,
            "file:///a.xml");
        workspaceHost.Add("file:///a.xml", "content");

        indexService.Fire(GameIndex.Empty);

        var forA = published.FirstOrDefault(p => p.Uri.ToString() == "file:///a.xml");
        Assert.NotNull(forA);
        Assert.Empty(forA!.Diagnostics);
    }

    // ── suppression (applied in the base, so every language gets it) ──────────

    private static Diagnostic Diag(string? code, int line)
    {
        return new Diagnostic
        {
            Range = new LspRange(new Position(line, 0), new Position(line, 5)),
            Message = "problem",
            Code = code is null ? (DiagnosticCode?)null : new DiagnosticCode(code)
        };
    }

    private static (List<PublishDiagnosticsParams> published, FakeIndexService indexService)
        BuildSuppressing(
            IReadOnlyList<Diagnostic> diagnostics,
            IReadOnlyList<SuppressionRange>? ranges = null,
            IReadOnlyList<SuppressionMatcher>? global = null)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeIndexService();
        var workspaceHost = new FakeWorkspaceHost();
        _ = new ConcretePublisher(p => published.Add(p), indexService, workspaceHost, ".xml",
            0, true, diagnostics, ranges,
            global is null ? null : new FakeGlobalSuppressionStore(global));
        workspaceHost.Add("file:///a.xml", "content");
        return (published, indexService);
    }

    private static IReadOnlyList<Diagnostic> PublishedDiagnostics(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<SuppressionRange>? ranges = null,
        IReadOnlyList<SuppressionMatcher>? global = null)
    {
        var (published, indexService) = BuildSuppressing(diagnostics, ranges, global);
        indexService.Fire(IndexWithDoc("file:///a.xml"));
        return published.Single().Diagnostics.ToList();
    }

    [Fact]
    public void Publish_DropsADiagnosticCoveredByADocumentRange()
    {
        var range = new SuppressionRange(
            SuppressionMatcher.ForId(new DiagnosticId(4, 1)), SuppressionScope.Node, 2, 4, 1);

        var result = PublishedDiagnostics([Diag("aetswg-004-0001", 3)], [range]);

        Assert.Empty(result);
    }

    // The range is a line window: the same id reported outside it is a different occurrence and
    // must survive.
    [Fact]
    public void Publish_KeepsTheSameIdReportedOutsideTheRange()
    {
        var range = new SuppressionRange(
            SuppressionMatcher.ForId(new DiagnosticId(4, 1)), SuppressionScope.Node, 2, 4, 1);

        var result = PublishedDiagnostics([Diag("aetswg-004-0001", 9)], [range]);

        Assert.Single(result);
    }

    [Fact]
    public void Publish_DropsADiagnosticMatchedByAGlobalSuppression()
    {
        var result = PublishedDiagnostics(
            [Diag("aetswg-004-0001", 3)],
            global: [SuppressionMatcher.ForGroup(DiagnosticGroup.Assets)]);

        Assert.Empty(result);
    }

    // A diagnostic that cannot be named by a suppression must stay visible - never silently
    // dropped because it happens to lack a parseable code.
    [Theory]
    [InlineData(null)]
    [InlineData("story-chain")]
    [InlineData("not-an-id")]
    public void Publish_KeepsDiagnosticsThatNoSuppressionCouldName(string? code)
    {
        var range = new SuppressionRange(
            SuppressionMatcher.ForGroup(DiagnosticGroup.Assets), SuppressionScope.File, 0, int.MaxValue, 0);

        var result = PublishedDiagnostics([Diag(code, 3)], [range]);

        Assert.Single(result);
    }

    [Fact]
    public void Publish_WithNoSuppressionsInForce_PassesEverythingThrough()
    {
        var result = PublishedDiagnostics([Diag("aetswg-004-0001", 3), Diag(null, 4)]);

        Assert.Equal(2, result.Count);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class ConcretePublisher : DiagnosticsPublisherBase
    {
        private readonly IReadOnlyList<Diagnostic> _diagnostics;
        private readonly IReadOnlyList<SuppressionRange> _ranges;

        public ConcretePublisher(
            Action<PublishDiagnosticsParams> publish,
            IGameIndexService indexService,
            IGameWorkspaceHost workspaceHost,
            string extension,
            int debounceMs = 0,
            bool enabled = true,
            IReadOnlyList<Diagnostic>? diagnostics = null,
            IReadOnlyList<SuppressionRange>? ranges = null,
            IGlobalSuppressionStore? globalSuppressions = null)
            : base(publish, indexService, workspaceHost, debounceMs, null, globalSuppressions)
        {
            FileExtension = extension;
            DiagnosticsEnabled = enabled;
            _diagnostics = diagnostics ?? [];
            _ranges = ranges ?? [];
        }

        protected override string FileExtension { get; }

        protected override bool DiagnosticsEnabled { get; }

        protected override void PublishForDocument(string uri, string text, GameIndex index)
        {
            // Where a real publisher applies it: with the diagnostics and the parsed document it
            // built them from both in hand.
            Publish(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri),
                Diagnostics = new Container<Diagnostic>(FilterSuppressed(_diagnostics, _ranges))
            });
        }
    }

    private sealed class FakeGlobalSuppressionStore : IGlobalSuppressionStore
    {
        private readonly IReadOnlyList<SuppressionMatcher> _matchers;

        public FakeGlobalSuppressionStore(IReadOnlyList<SuppressionMatcher> matchers)
        {
            _matchers = matchers;
        }

        public IReadOnlyList<SuppressionMatcher> GetAll()
        {
            return _matchers;
        }

        public void Add(SuppressionMatcher matcher, string? reason = null)
        {
        }

        public void Remove(SuppressionMatcher matcher)
        {
        }
    }

    private sealed class ThrowingPublisher : DiagnosticsPublisherBase
    {
        private readonly string _throwingUri;

        public ThrowingPublisher(
            Action<PublishDiagnosticsParams> publish,
            IGameIndexService indexService,
            IGameWorkspaceHost workspaceHost,
            string throwingUri)
            : base(publish, indexService, workspaceHost, 0)
        {
            _throwingUri = throwingUri;
        }

        protected override string FileExtension => ".xml";

        protected override void PublishForDocument(string uri, string text, GameIndex index)
        {
            if (uri == _throwingUri)
                throw new InvalidOperationException("Simulated document processing failure");

            Publish(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri),
                Diagnostics = new Container<Diagnostic>()
            });
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;
        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

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