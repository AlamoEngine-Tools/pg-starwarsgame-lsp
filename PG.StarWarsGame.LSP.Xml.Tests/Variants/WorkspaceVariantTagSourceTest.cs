// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Variants;

namespace PG.StarWarsGame.LSP.Xml.Tests.Variants;

public sealed class WorkspaceVariantTagSourceTest
{
    private const string Uri = "file:///c:/u.xml";

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GameSymbol Symbol(string id, string uri)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(uri, 1, null), null);
    }

    private static DocumentIndex Doc(string uri, int version = 1, int layerRank = 0)
    {
        return new DocumentIndex(uri, version, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty) with { LayerRank = layerRank };
    }

    // Index with one definition per (id, uri) and a Documents entry per distinct uri.
    private static GameIndex IndexWith(params (string Id, string Uri, int Version, int Rank)[] defs)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        var definitions =
            ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, uri, version, rank) in defs)
        {
            docs = docs.SetItem(uri, Doc(uri, version, rank));
            definitions = definitions.TryGetValue(id, out var arr)
                ? definitions.SetItem(id, arr.Add(Symbol(id, uri)))
                : definitions.Add(id, [Symbol(id, uri)]);
        }

        return GameIndex.Empty with { Documents = docs, WorkspaceDefinitions = definitions };
    }

    private static (WorkspaceVariantTagSource Source, FakeIndexService Index, FakeHost Host, MockFileSystem Fs)
        Build()
    {
        var fs = new MockFileSystem();
        var host = new FakeHost();
        var indexService = new FakeIndexService();
        var textSource = new DocumentTextSource(host, new FileHelper(fs),
            NullLogger<DocumentTextSource>.Instance);
        var source = new WorkspaceVariantTagSource(new XmlParseCache(textSource, 16), indexService);
        return (source, indexService, host, fs);
    }

    // ── open-document resolution ─────────────────────────────────────────────

    [Fact]
    public void TryGetTags_ReturnsDirectChildTagsOfObject()
    {
        var (source, index, host, _) = Build();
        host.AddOrUpdate(Uri,
            """<GameObjectFiles><SpaceUnit Name="V"><Max_Health>100</Max_Health><Mass>5</Mass></SpaceUnit></GameObjectFiles>""",
            1);
        index.Current = IndexWith(("V", Uri, 1, 0));

        var tags = source.TryGetTags("V");

        Assert.NotNull(tags);
        Assert.Equal(2, tags!.Count);
        Assert.Contains(tags, t => t.TagName == "Max_Health" && t.Value == "100");
        Assert.Contains(tags, t => t.TagName == "Mass" && t.Value == "5");
    }

    [Fact]
    public void TryGetTags_FragmentPreservesOriginalCaseAndText()
    {
        var (source, index, host, _) = Build();
        host.AddOrUpdate(Uri,
            """<GameObjectFiles><SpaceUnit Name="V"><Max_Health>100</Max_Health></SpaceUnit></GameObjectFiles>""", 1);
        index.Current = IndexWith(("V", Uri, 1, 0));

        var tag = Assert.Single(source.TryGetTags("V")!);

        Assert.Equal("<Max_Health>100</Max_Health>", tag.Fragment);
    }

    [Fact]
    public void TryGetTags_OriginPointsToTagLineInDocument()
    {
        var (source, index, host, _) = Build();
        host.AddOrUpdate(Uri,
            "<GameObjectFiles>\n<SpaceUnit Name=\"V\">\n<Max_Health>100</Max_Health>\n</SpaceUnit>\n</GameObjectFiles>",
            1);
        index.Current = IndexWith(("V", Uri, 1, 0));

        var tag = Assert.Single(source.TryGetTags("V")!);

        var origin = Assert.IsType<FileOrigin>(tag.Origin);
        Assert.Equal(Uri, origin.Uri);
        Assert.Equal(2, origin.Line); // 0-based line of <Max_Health>
    }

    [Fact]
    public void TryGetTags_CaseInsensitiveId()
    {
        var (source, index, host, _) = Build();
        host.AddOrUpdate(Uri, """<X><SpaceUnit Name="MyUnit"><Hp>1</Hp></SpaceUnit></X>""", 1);
        index.Current = IndexWith(("MyUnit", Uri, 1, 0));

        Assert.NotNull(source.TryGetTags("MYUNIT"));
    }

    // ── unknown / baseline-only objects ──────────────────────────────────────

    [Fact]
    public void TryGetTags_UnknownObject_ReturnsNull()
    {
        var (source, index, host, _) = Build();
        host.AddOrUpdate(Uri, """<GameObjectFiles><SpaceUnit Name="V"/></GameObjectFiles>""", 1);
        index.Current = IndexWith(("V", Uri, 1, 0));

        Assert.Null(source.TryGetTags("MISSING"));
    }

    [Fact]
    public void TryGetTags_BaselineOnlyObject_ReturnsNull()
    {
        // No workspace definition — the resolver must fall back to the shipped baseline tags,
        // so the workspace source reports "not mine".
        var (source, index, _, _) = Build();
        index.Current = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                Symbols = ImmutableDictionary<string, GameSymbol>.Empty.Add("B", Symbol("B", Uri))
            }
        };

        Assert.Null(source.TryGetTags("B"));
    }

    // ── closed files come from disk ──────────────────────────────────────────

    [Fact]
    public void TryGetTags_ClosedFile_ReadsFromDisk()
    {
        var (source, index, _, fs) = Build();
        fs.AddFile(@"c:\u.xml", new MockFileData(
            """<GameObjectFiles><SpaceUnit Name="V"><Mass>7</Mass></SpaceUnit></GameObjectFiles>"""));
        index.Current = IndexWith(("V", Uri, 0, 0));

        var tags = source.TryGetTags("V");

        Assert.NotNull(tags);
        Assert.Contains(tags!, t => t.TagName == "Mass" && t.Value == "7");
    }

    [Fact]
    public void TryGetTags_OpenDocumentTextWinsOverDisk()
    {
        var (source, index, host, fs) = Build();
        fs.AddFile(@"c:\u.xml", new MockFileData(
            """<X><SpaceUnit Name="V"><Mass>1</Mass></SpaceUnit></X>"""));
        host.AddOrUpdate(Uri, """<X><SpaceUnit Name="V"><Mass>2</Mass></SpaceUnit></X>""", 2);
        index.Current = IndexWith(("V", Uri, 2, 0));

        var tag = Assert.Single(source.TryGetTags("V")!);

        Assert.Equal("2", tag.Value);
    }

    // ── layer precedence and cache invalidation ──────────────────────────────

    [Fact]
    public void TryGetTags_HighestRankDefinitionWins()
    {
        const string depUri = "file:///c:/dep/u.xml";
        const string modUri = "file:///c:/mod/u.xml";
        var (source, index, host, _) = Build();
        host.AddOrUpdate(depUri, """<X><SpaceUnit Name="V"><Mass>1</Mass></SpaceUnit></X>""", 1);
        host.AddOrUpdate(modUri, """<X><SpaceUnit Name="V"><Mass>9</Mass></SpaceUnit></X>""", 1);
        index.Current = IndexWith(("V", depUri, 1, 0), ("V", modUri, 1, 1));

        var tag = Assert.Single(source.TryGetTags("V")!);

        Assert.Equal("9", tag.Value);
        Assert.Equal(modUri, Assert.IsType<FileOrigin>(tag.Origin!).Uri);
    }

    [Fact]
    public void TryGetTags_CacheInvalidatedWhenDocumentVersionChanges()
    {
        var (source, index, host, _) = Build();
        host.AddOrUpdate(Uri, """<X><SpaceUnit Name="V"><Mass>1</Mass></SpaceUnit></X>""", 1);
        index.Current = IndexWith(("V", Uri, 1, 0));
        Assert.Equal("1", Assert.Single(source.TryGetTags("V")!).Value);

        // Edit: host text and the indexed document's version advance together.
        host.AddOrUpdate(Uri, """<X><SpaceUnit Name="V"><Mass>2</Mass></SpaceUnit></X>""", 2);
        index.Current = IndexWith(("V", Uri, 2, 0));

        Assert.Equal("2", Assert.Single(source.TryGetTags("V")!).Value);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
#pragma warning disable CS0067
        public event Action<GameIndex>? IndexChanged;
        public event Action<ILocalisationIndex>? LocalisationChanged;
        public event Action<GameIndex>? DynamicEnumChanged;
#pragma warning restore CS0067

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

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new NullDisposable();
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public IEnumerable<TrackedDocument> All => _docs.Values;

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }
    }
}
