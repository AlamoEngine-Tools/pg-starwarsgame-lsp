// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceScannerTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        params IGameDocumentParser[] parsers)
    {
        return new WorkspaceScanner(fs, parsers, svc, NullLogger<WorkspaceScanner>.Instance, null,
            new FileTypeRegistry(), new FakeSchemaProvider());
    }

    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        return new WorkspaceScanner(fs, parsers, svc, NullLogger<WorkspaceScanner>.Instance, null,
            registry, schema);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_ParseableFiles_AreIndexedWithVersionZero()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        Assert.Equal(2, svc.Calls.Count);
        Assert.All(svc.Calls, c => Assert.Equal(0, c.Version));
    }

    [Fact]
    public async Task ScanAsync_UnparseableExtension_Skipped()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.lua")] = new("-- lua")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        Assert.Single(svc.Calls);
        Assert.EndsWith(".xml", svc.Calls[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_MultipleWorkspaceFolders_AllFilesIndexed()
    {
        var root1 = Root("ws1");
        var root2 = Root("ws2");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root1, "a.xml")] = new("<Root/>"),
            [Path.Combine(root2, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root1, root2], CancellationToken.None);

        Assert.Equal(2, svc.Calls.Count);
    }

    [Fact]
    public async Task ScanAsync_PreCancelledToken_ThrowsAndDoesNotIndex()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Build(fs, svc, new FakeParser()).ScanAsync([root], cts.Token));

        Assert.Empty(svc.Calls);
    }

    [Fact]
    public async Task ScanAsync_UsesBeginBulkUpdate_Exactly_Once()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        Assert.Equal(1, svc.BeginBulkUpdateCallCount);
    }

    [Fact]
    public async Task ScanAsync_EmptyFolder_IndexesNothing()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        Assert.Empty(svc.Calls);
    }
    // ── PreScanMetafiles ─────────────────────────────────────────────────────

    [Fact]
    public async Task PreScan_FileRegistry_RegistersFilesFromMetafile()
    {
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "DATA", "XML", "GameObjectFiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new MockFileData("<Files><File filename=\"DATA\\XML\\HARDPOINTS.XML\"/></Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        var expectedKey = Path.Combine(root, "data", "xml", "hardpoints.xml")
            .Replace('\\', '/').ToLowerInvariant();
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_FileRegistry_FallsBackToBaseline_WhenMetafileAbsent()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var fileTypeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("data/xml/hardpoints.xml", ImmutableArray.Create("GameObjectType"));
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            DateTimeOffset.UtcNow, "",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            fileTypeMap);
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService(new GameIndex(baseline,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty));

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        var expectedKey = Path.Combine(root, "data", "xml", "hardpoints.xml")
            .Replace('\\', '/').ToLowerInvariant();
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_DirectContent_RegistersMetafilePath()
    {
        var root = Root("ws");
        var moviesPath = Path.Combine(root, "DATA", "XML", "MOVIES.XML");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [moviesPath] = new MockFileData("<Movies/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/movies.xml", MetafileType.DirectContent, ["BinkMovie"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        var expectedKey = Path.Combine(root, "data", "xml", "movies.xml")
            .Replace('\\', '/').ToLowerInvariant();
        Assert.Equal(["BinkMovie"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_Special_IsSkipped()
    {
        var root = Root("ws");
        var campaignPath = Path.Combine(root, "DATA", "XML", "CAMPAIGNS.XML");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [campaignPath] = new MockFileData("<Campaigns/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/campaigns.xml", MetafileType.Special, ["StoryParser"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        Assert.Empty(registry.All);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeParser : IGameDocumentParser
    {
        private readonly string _ext;

        public FakeParser(string ext = ".xml")
        {
            _ext = ext;
        }

        public bool CanParse(string ext)
        {
            return ext.Equals(_ext, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly MetafileDefinition[] _metafiles;

        public FakeSchemaProvider(params MetafileDefinition[] metafiles) => _metafiles = metafiles;

        public IReadOnlyList<MetafileDefinition> AllMetafiles => _metafiles;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public XmlTagDefinition? GetTag(string tagName) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName) => [];
        public GameObjectTypeDefinition? GetObjectType(string typeName) => null;
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName) => [];
        public EnumDefinition? GetEnum(string enumName) => null;
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        private readonly GameIndex _current;
        private readonly object _lock = new();
        public readonly List<(string Uri, int Version)> Calls = [];
        public int BeginBulkUpdateCallCount;

        public FakeIndexService(GameIndex? current = null) => _current = current ?? GameIndex.Empty;

        public GameIndex Current => _current;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                Calls.Add((uri, version));
            }

            return Task.CompletedTask;
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            Interlocked.Increment(ref BeginBulkUpdateCallCount);
            return NullDisposable.Instance;
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}