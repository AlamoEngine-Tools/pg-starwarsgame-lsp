// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

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
        return new WorkspaceScanner(new FileHelper(fs), parsers, svc,
            NullLogger<WorkspaceScanner>.Instance, null,
            new FileTypeRegistry(), new FakeSchemaProvider());
    }

    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        return new WorkspaceScanner(new FileHelper(fs), parsers, svc,
            NullLogger<WorkspaceScanner>.Instance, null,
            registry, schema);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_IndexedUri_HasFileScheme()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        Assert.StartsWith("file://", svc.Calls[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_IndexedUri_IsCanonicalLowercase()
    {
        // Scanner must produce canonical file:/// URIs (lowercase, forward-slash) so that
        // index lookups and file-type registry lookups always use the same key format as
        // the LSP client URIs after normalization.
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Units.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        var uri = svc.Calls[0].Uri;
        Assert.StartsWith("file:///", uri, StringComparison.Ordinal);
        Assert.Equal(uri, uri.ToLowerInvariant());
        Assert.DoesNotContain('\\', uri);
    }

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
        // Lowercase path — MockFileSystem uses case-sensitive keys; FindInWorkspace
        // searches with the normalised (lowercase) relative path from MetafileDefinition.
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Game_Object_Files><File>DATA\\XML\\HARDPOINTS.XML</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "hardpoints.xml"));
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

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_DirectContent_RegistersMetafilePath()
    {
        var root = Root("ws");
        var moviesPath = Path.Combine(root, "DATA", "XML", "MOVIES.XML");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [moviesPath] = new("<Movies/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/movies.xml", MetafileType.DirectContent, ["BinkMovie"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "movies.xml"));
        Assert.Equal(["BinkMovie"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task PreScan_Special_IsSkipped()
    {
        var root = Root("ws");
        var campaignPath = Path.Combine(root, "DATA", "XML", "CAMPAIGNS.XML");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [campaignPath] = new("<Campaigns/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/campaigns.xml", MetafileType.Special, ["StoryParser"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        Assert.Empty(registry.All);
    }

    [Fact]
    public async Task PreScan_FileRegistry_WorkspaceIsXmlDirectory_RegistersFilesFromMetafile()
    {
        // Workspace root IS the XML directory: metafile lives at root/<name>.xml
        // (no data/xml/ prefix in the key). Files inside the metafile are listed
        // as DATA\XML\FOO.XML — they resolve to root/<foo>.xml.
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Game_Object_Files><File>DATA\\XML\\HARDPOINTS.XML</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService();

        await Build(fs, svc, registry, schema).ScanAsync([root], CancellationToken.None);

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public async Task ScanAsync_WaitsForSchema_WhenAllMetafilesEmptyAtStart()
    {
        // Simulate HttpSchemaProvider: schema is empty at scan start and fires
        // SchemaRefreshed after the background HTTP download completes.
        var root = Root("ws");
        var metafilePath = Path.Combine(root, "data", "xml", "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Files><File>DATA\\XML\\HARDPOINTS.XML</File></Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new DelayedSchemaProvider();
        var svc = new FakeIndexService();
        var scanner = Build(fs, svc, registry, schema);

        // ScanAsync will yield at WaitForSchemaAsync since AllMetafiles is empty.
        var scanTask = scanner.ScanAsync([root], CancellationToken.None);

        // Simulate schema load completing and firing SchemaRefreshed.
        schema.LoadMetafiles(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry,
                ["GameObjectType"]));

        await scanTask;

        var expectedKey = new FileHelper(fs).PathToFileUri(
            Path.Combine(root, "data", "xml", "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
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

    // Schema provider that starts empty and completes ReadyAsync when LoadMetafiles is called,
    // mimicking HttpSchemaProvider's background-load behaviour.
    private sealed class DelayedSchemaProvider : ISchemaProvider
    {
        private readonly TaskCompletionSource _readyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private MetafileDefinition[] _metafiles = [];
        public event EventHandler? SchemaRefreshed;

        public Task ReadyAsync => _readyTcs.Task;
        public IReadOnlyList<MetafileDefinition> AllMetafiles => _metafiles;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public void LoadMetafiles(params MetafileDefinition[] metafiles)
        {
            _metafiles = metafiles;
            SchemaRefreshed?.Invoke(this, EventArgs.Empty);
            _readyTcs.TrySetResult();
        }
    }

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly MetafileDefinition[] _metafiles;

        public FakeSchemaProvider(params MetafileDefinition[] metafiles)
        {
            _metafiles = metafiles;
        }

        public IReadOnlyList<MetafileDefinition> AllMetafiles => _metafiles;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        // Fire immediately on subscription: simulates a schema that is already loaded
        // so that WaitForSchemaAsync in WorkspaceScanner does not time out in tests.
        public event EventHandler? SchemaRefreshed
        {
            add { value?.Invoke(this, EventArgs.Empty); }
            remove { }
        }

        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        private readonly object _lock = new();
        public readonly List<(string Uri, int Version)> Calls = [];
        public int BeginBulkUpdateCallCount;

        public FakeIndexService(GameIndex? current = null)
        {
            Current = current ?? GameIndex.Empty;
        }

        public GameIndex Current { get; }

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