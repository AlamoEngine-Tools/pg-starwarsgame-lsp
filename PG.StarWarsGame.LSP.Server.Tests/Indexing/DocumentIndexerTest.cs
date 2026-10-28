// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Indexing;

/// <summary>
///     The document stage on its own (#315): which files are parsed, how a layer's index snapshot is
///     replayed, discarded and rewritten, and what a parser's out-of-band state does across it. The
///     metafile stage runs first in each test, as it does in the server, because the EaW XML context
///     and the file-type registry it fills decide what is parsed and what a snapshot is keyed by.
/// </summary>
public sealed class DocumentIndexerTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static (DocumentIndexer Indexer, MetafileRegistrar Registrar) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        IProjectIndexCache? cache = null,
        params IGameDocumentParser[] parsers)
    {
        return Build(fs, svc, registry, schema, cache, new LuaApiSchemaProvider([]), parsers);
    }

    private static (DocumentIndexer Indexer, MetafileRegistrar Registrar) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        return Build(fs, svc, registry, schema, null, new LuaApiSchemaProvider([]), parsers);
    }

    private static (DocumentIndexer Indexer, MetafileRegistrar Registrar) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        IProjectIndexCache? cache, ILuaApiSchemaProvider luaSchema, params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        var registrar = new MetafileRegistrar(fh, registry, schema, ctx, new StoryChainProblemStore(),
            NullLogger<MetafileRegistrar>.Instance);
        var indexer = new DocumentIndexer(fh, svc, cache ?? new NullProjectIndexCache(),
            new ParseableFiles(fh, parsers, ctx), new IndexFingerprints(schema, luaSchema, registry, fh),
            NullLogger<DocumentIndexer>.Instance);
        return (indexer, registrar);
    }

    /// <summary>
    ///     Stamps a hand-built snapshot with the cross-layer key the indexer will compute for the
    ///     layer rooted at <paramref name="layerXmlDirs" />, so the snapshot reads as current.
    /// </summary>
    /// <remarks>
    ///     Call AFTER <c>PreScanMetafiles</c> - that is what fills the registry the key reads.
    ///     Goes through the production function on purpose: a test that recomputed the key by hand
    ///     could agree with itself while disagreeing with the indexer, and the failure it would
    ///     hide is a silently stale index.
    /// </remarks>
    private static void StampCrossLayer(
        ProjectIndexSnapshot snapshot, MockFileSystem fs, IFileTypeRegistry registry,
        WorkspaceConfiguration config, params string[] layerXmlDirs)
    {
        var fh = new FileHelper(fs);
        var files = layerXmlDirs
            .Where(d => fs.Directory.Exists(d))
            .SelectMany(d => fs.Directory.EnumerateFiles(d, "*.xml", SearchOption.AllDirectories))
            .Select(fh.PathToFileUri)
            .ToArray();

        snapshot.CrossLayerFingerprint =
            CrossLayerInputFingerprint.Compute(registry, files, layerXmlDirs, config.XmlDirectories);
    }

    // ── IndexDocumentsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task IndexAsync_IndexesXmlFiles_InXmlDirectories()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>"),
            [Path.Combine(xmlDir, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        var count = await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, svc.Updates.Count);
        Assert.Equal(1, svc.BulkUpdates);
        Assert.All(svc.Updates, c => Assert.Equal(0, c.Version));
        Assert.All(svc.Updates, c => Assert.StartsWith("file:///", c.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexAsync_SkipsFilesOutsideXmlDirectories()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new("<Root/>"),
            [Path.Combine(root, "scripts", "build.xml")] = new("<project/>")
        });
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        var call = Assert.Single(svc.Updates);
        Assert.EndsWith("units.xml", call.Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndexAsync_IndexesLuaFiles_FromScriptRoots()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var scriptDir = Path.Combine(root, "data", "scripts");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(scriptDir, "story.lua")] = new("function Definitions() end")
        });
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [scriptDir], [], [], null);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser(), new FakeParser(".lua"));
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Contains(svc.Updates, c => c.Uri.EndsWith("story.lua", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexAsync_PreCancelledToken_Throws_NoIndex()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            indexer.IndexAsync(config, cts.Token));

        Assert.Empty(svc.Updates);
    }

    [Fact]
    public async Task IndexAsync_ReportsProgress()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>"),
            [Path.Combine(xmlDir, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        var maxTotal = 0;
        var maxDone = 0;
        await indexer.IndexAsync(config, CancellationToken.None, (done, total) =>
        {
            maxTotal = total;
            if (done > maxDone) maxDone = done;
        });

        Assert.Equal(2, maxTotal);
        Assert.Equal(2, maxDone);
    }


    // ── IndexDocumentsAsync with cache ──────────────────────────────────────

    [Fact]
    public async Task IndexAsync_WithLayers_NoSnapshot_ParsesNormally()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var cache = new FakeProjectIndexCache(); // no snapshot stored
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates); // parsed normally
        Assert.Empty(svc.Injected);
        Assert.Equal(new IndexCacheStats(0, 1, 0, 1),
            indexer.LastIndexCache);
    }

    [Fact]
    public async Task IndexAsync_WithLayers_CacheHit_InjectsWithoutParsing()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fileContent = "<Root/>"u8.ToArray();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "data", "xml", "units.xml")] = new(fileContent)
        });
        var svc = new FakeGameIndexService();

        // Build a snapshot whose hash matches the file content
        var fh = new FileHelper(fs);
        const string relPath = "data/xml/units.xml";
        var hash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(xmlDir, "units.xml"), fh.FileSystem);
        var entry = new ProjectFileEntry
        {
            RelativePath = relPath, ContentHash = hash,
            Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
        };
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [],
            Files = [entry],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Empty(svc.Updates); // no re-parse
        Assert.Single(svc.Injected); // injected from cache
        // Kept for the bug report, which otherwise could not tell a warm start from a cold one.
        Assert.Equal(new IndexCacheStats(1, 0, 1, 0),
            indexer.LastIndexCache);
    }

    // The index's own key used to be lowercased on the way in, which made every lookup work by
    // accident: both sides had been flattened, so nothing could disagree. That is the same mistake
    // the URI fold used to make - it destroys the real name, and a project-relative path is what we
    // hand back to the filesystem. These two pin the rule the URIs now follow: the VALUE keeps the
    // name the file actually has, and the COMPARISON supplies the case-insensitivity.
    [Fact]
    public async Task IndexAsync_CacheHit_WhenTheSnapshotRecordsTheFilesRealCase()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "Data", "XML");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/');
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "XML", "Units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        var hash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(xmlDir, "Units.xml"), fh.FileSystem);
        var entry = new ProjectFileEntry
        {
            // Spelled the way the file really is on disk - which is what the scan now records.
            RelativePath = "Data/XML/Units.xml", ContentHash = hash,
            Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
        };
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [],
            Files = [entry],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Empty(svc.Updates); // no re-parse
        Assert.Single(svc.Injected); // injected from cache
    }

    [Fact]
    public async Task IndexAsync_CacheHit_EvenWhenTheSnapshotSpellsThePathInAnotherCase()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "Data", "XML");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/');
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "XML", "Units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        var hash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(xmlDir, "Units.xml"), fh.FileSystem);
        var entry = new ProjectFileEntry
        {
            // A spelling neither side would produce today - an older snapshot, or a file renamed
            // to differ only in case. The engine calls it the same asset, so the lookup must too.
            RelativePath = "DATA/xml/UNITS.XML", ContentHash = hash,
            Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
        };
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [],
            Files = [entry],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Empty(svc.Updates);
        Assert.Single(svc.Injected);
    }

    [Fact]
    public async Task IndexAsync_SnapshotRecordsTheRelativePathWithItsCaseIntact()
    {
        // The saved key is what a case-sensitive host has to open next time, so the flattening has
        // to be gone from the WRITE side too, not just tolerated on the read side.
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "Data", "XML");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/');
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "XML", "Units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var cache = new FakeProjectIndexCache();
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        var saved = Assert.Single(cache.Saved).Value;
        Assert.Equal("Data/XML/Units.xml", Assert.Single(saved.Files).RelativePath);
    }

    [Fact]
    public async Task IndexAsync_SchemaFingerprintChanged_DiscardsLayerSnapshot()
    {
        // The schema drives what the parser emits (which tags become references, which files
        // carry types), so a snapshot built under a different schema must be discarded even
        // when every file's content hash matches - e.g. after flipping a tag from
        // referenceKind: unknown to a typed reference.
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "data", "xml", "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        var hash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(xmlDir, "units.xml"), fh.FileSystem);
        var entry = new ProjectFileEntry
        {
            RelativePath = "data/xml/units.xml", ContentHash = hash,
            Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
        };
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [],
            Files = [entry],
            SchemaFingerprint = "fingerprint-of-an-older-schema"
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates); // re-parsed
        Assert.Empty(svc.Injected);
    }

    [Fact]
    public async Task IndexAsync_WritesSnapshotWithCurrentSchemaFingerprint()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "data", "xml", "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var cache = new FakeProjectIndexCache();
        var config = ConfigWithLayer(xmlDir, pgproj);
        var schema = new SchemaProviderBuilder().Build();
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), schema, cache, new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        var saved = cache.Saved[pgproj];
        Assert.Equal(SchemaFingerprint.Compute(schema), saved.SchemaFingerprint);
        Assert.NotEqual(string.Empty, saved.SchemaFingerprint);
    }

    /// <summary>
    ///     The Lua stubs decide which string arguments a script's index records as references, so a
    ///     snapshot written under one set must not be replayed under another. Measured: a session on
    ///     the released schema (stubs still spelled <c>---@xmlref</c>, no longer read) cached Lua
    ///     indices without a single reference, and a server on the current stubs replayed them.
    /// </summary>
    [Fact]
    public async Task IndexAsync_LuaStubsChanged_SnapshotFingerprintChanges()
    {
        const string current = "---@param name string\n---@aetref XmlObject\nfunction Find_Object_Type(name) end";
        const string released = "---@param name string\n---@xmlref XmlObject\nfunction Find_Object_Type(name) end";

        Assert.NotEqual(await SavedFingerprintAsync(current), await SavedFingerprintAsync(released));
    }

    private static async Task<string> SavedFingerprintAsync(string luaStub)
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "data", "xml", "units.xml")] = new("<Root/>")
        });
        var cache = new FakeProjectIndexCache();
        var (indexer, registrar) = Build(fs, new FakeGameIndexService(), new FileTypeRegistry(),
            new SchemaProviderBuilder().Build(), cache, new LuaApiSchemaProvider([luaStub]), new FakeParser());
        var config = ConfigWithLayer(xmlDir, pgproj);
        registrar.Register(config, [root], BaselineIndex.Empty);

        await indexer.IndexAsync(config, CancellationToken.None);

        return cache.Saved[pgproj].SchemaFingerprint;
    }

    [Fact]
    public async Task IndexAsync_WithLayers_StaleHash_Reparses()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "data", "xml", "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        // Snapshot with a wrong hash
        var entry = new ProjectFileEntry
        {
            RelativePath = "data/xml/units.xml", ContentHash = "stale_hash",
            Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
        };
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "old",
            DependencyHashes = [],
            Files = [entry],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates); // re-parsed
        Assert.Empty(svc.Injected);
    }

    [Fact]
    public async Task IndexAsync_WithLayers_WritesSnapshotAfterIndexing()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "data", "xml", "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var cache = new FakeProjectIndexCache();
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Contains(pgproj, cache.SavedPaths);
        var saved = cache.Saved[pgproj];
        Assert.Equal(ProjectIndexSnapshot.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.Single(saved.Files);
    }

    /// <summary>
    ///     MEASURED 2026-10-04 on EaWX: the core layer's snapshot was discarded on four of seven
    ///     starts ("Cross-layer inputs changed since layer 'Core Library' was cached") because the
    ///     key folded in the workspace-wide xml-root union, which differs between "core opened
    ///     alone" and "core under Rev", and because both contexts wrote the SAME file. Three
    ///     starts, one shared disk: alone, under a leaf, alone again. The third start parses
    ///     nothing, and the leaf start did not replace the dependency's snapshot.
    /// </summary>
    public static TheoryData<string> Setups => new(ProjectSetup.Names);

    // Every shape (#280): the layers' roots are lower-cased, the disk keeps its case, and every layer's
    // file is indexed once.
    [Theory]
    [MemberData(nameof(Setups))]
    public async Task IndexAsync_InEveryShape_IndexesEveryLayersFiles(string name)
    {
        var setup = ProjectSetup.Named(name);
        var fs = setup.Disk();
        foreach (var layer in setup.Layers)
            fs.AddFile(setup.PathOf(layer.Rank, "Xml/Units.xml"), new MockFileData("<Root/>"));
        var svc = new FakeGameIndexService();
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        var configuration = setup.Configuration();

        registrar.Register(configuration, [.. setup.Layers.Select(l => setup.FolderOf(l.Rank))], svc.Current.Baseline);
        await indexer.IndexAsync(configuration, CancellationToken.None);

        foreach (var layer in setup.Layers)
            Assert.Single(svc.Updates, c => DocumentUris.Same(c.Uri, setup.Uri(layer.Rank, "Xml/Units.xml")));
    }

    [Fact]
    public async Task IndexAsync_ADependencyOpenedAloneThenUnderALeaf_KeepsItsSnapshot()
    {
        var coreRoot = Root("core");
        var revRoot = Root("rev");
        var coreXml = Path.Combine(coreRoot, "data", "xml");
        var revXml = Path.Combine(revRoot, "data", "xml");
        var corePgproj = Path.Combine(coreRoot, "core.pgproj").Replace('\\', '/').ToLowerInvariant();
        var revPgproj = Path.Combine(revRoot, "rev.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(coreXml, "units.xml")] = new("<Root/>"),
            [Path.Combine(revXml, "mod.xml")] = new("<Root/>")
        });
        // One disk, three server starts.
        var cache = new FakeProjectIndexCache();
        var coreLayer = new ProjectLayer(0, "Core", [coreXml], [], [], [], null, corePgproj);
        var coreAlone = new WorkspaceConfiguration([coreXml], [], [], [], null) { Layers = [coreLayer] };
        var revOverCore = new WorkspaceConfiguration([coreXml, revXml], [], [], [], null)
        {
            Layers = [coreLayer, new ProjectLayer(1, "Rev", [revXml], [], [], [], null, revPgproj)]
        };

        await StartAsync(coreAlone, coreRoot);
        var underALeaf = await StartAsync(revOverCore, coreRoot, revRoot);
        var aloneAgain = await StartAsync(coreAlone, coreRoot);

        // The leaf start served core from the snapshot the first start wrote...
        Assert.DoesNotContain(underALeaf.Updates, c => c.Uri.Contains("/core/", StringComparison.OrdinalIgnoreCase));
        // ...and the third start found that snapshot untouched.
        Assert.Empty(aloneAgain.Updates);
        // Same registry slice, same relevant roots: one context, one file for the dependency.
        Assert.Single(cache.SavedByContext.Keys, k => k.Pgproj == corePgproj);
        return;

        async Task<FakeGameIndexService> StartAsync(WorkspaceConfiguration config, params string[] roots)
        {
            var svc = new FakeGameIndexService();
            var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
                new FakeParser());
            registrar.Register(config, roots, svc.Current.Baseline);
            await indexer.IndexAsync(config, CancellationToken.None);
            return svc;
        }
    }

    /// <summary>
    ///     A dependency's OWN files changing must NOT discard a dependent layer's snapshot.
    /// </summary>
    /// <remarks>
    ///     This is the whole point of the cross-layer key. The old key recorded the dependency's
    ///     OverallHash, so one changed line in a shared core re-parsed every leaf that referenced
    ///     it - on a four-leaf mod, four full re-parses for an edit that could not have changed
    ///     how any of their files parse. What CAN change that is a dependency RETYPING this
    ///     layer's files, which the next test covers.
    /// </remarks>
    [Fact]
    public async Task IndexAsync_DependencyContentChanged_KeepsLayerSnapshot()
    {
        var depRoot = Root("dep");
        var modRoot = Root("mod");
        var depXml = Path.Combine(depRoot, "data", "xml");
        var modXml = Path.Combine(modRoot, "data", "xml");
        var depPgproj = Path.Combine(depRoot, "dep.pgproj").Replace('\\', '/').ToLowerInvariant();
        var modPgproj = Path.Combine(modRoot, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            // The dependency's own file differs from whatever it was when the mod was cached.
            [Path.Combine(depXml, "dep.xml")] = new("<Root><Changed/></Root>"),
            [Path.Combine(modXml, "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        var unitsHash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(modXml, "units.xml"), fh.FileSystem);
        var modSnapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [],
            Files =
            [
                new ProjectFileEntry
                {
                    RelativePath = "data/xml/units.xml", ContentHash = unitsHash,
                    Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
                }
            ],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [modPgproj] = modSnapshot };
        var config = new WorkspaceConfiguration([depXml, modXml], [], [], [], null)
        {
            Layers =
            [
                new ProjectLayer(0, "Dep", [depXml], [], [], [], null, depPgproj),
                new ProjectLayer(1, "Mod", [modXml], [], [], [], null, modPgproj)
            ]
        };
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [depRoot, modRoot], svc.Current.Baseline);
        StampCrossLayer(modSnapshot, fs, registry, config, modXml);

        await indexer.IndexAsync(config, CancellationToken.None);

        // The dependency's own file is parsed; the mod's is served from its snapshot.
        Assert.Single(svc.Updates);
        Assert.Single(svc.Injected);
    }

    /// <summary>
    ///     A dependency that changes the TYPES registered for this layer's files must still
    ///     discard the snapshot - the registered types decide which symbol passes run, so the
    ///     cached parse is genuinely wrong. The narrower key must not lose this.
    /// </summary>
    [Fact]
    public async Task IndexAsync_DependencyRetypedThisLayersFiles_DiscardsLayerSnapshot()
    {
        var depRoot = Root("dep");
        var modRoot = Root("mod");
        var depXml = Path.Combine(depRoot, "data", "xml");
        var modXml = Path.Combine(modRoot, "data", "xml");
        var depPgproj = Path.Combine(depRoot, "dep.pgproj").Replace('\\', '/').ToLowerInvariant();
        var modPgproj = Path.Combine(modRoot, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(depXml, "dep.xml")] = new("<Root/>"),
            [Path.Combine(modXml, "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        var unitsHash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(modXml, "units.xml"), fh.FileSystem);
        var modSnapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [],
            Files =
            [
                new ProjectFileEntry
                {
                    RelativePath = "data/xml/units.xml", ContentHash = unitsHash,
                    Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
                }
            ],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [modPgproj] = modSnapshot };
        var config = new WorkspaceConfiguration([depXml, modXml], [], [], [], null)
        {
            Layers =
            [
                new ProjectLayer(0, "Dep", [depXml], [], [], [], null, depPgproj),
                new ProjectLayer(1, "Mod", [modXml], [], [], [], null, modPgproj)
            ]
        };
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [depRoot, modRoot], svc.Current.Baseline);
        // Stamp the snapshot as it was when the mod's units.xml carried NO registered type...
        StampCrossLayer(modSnapshot, fs, registry, config, modXml);
        // ...then let a dependency metafile register one for it, as a dependency's metafile can.
        registry.RegisterFile(fh.PathToFileUri(Path.Combine(modXml, "units.xml")), ["GameObjectType"]);

        await indexer.IndexAsync(config, CancellationToken.None);

        // Both re-parsed; nothing served from the now-stale mod snapshot.
        Assert.Equal(2, svc.Updates.Count);
        Assert.Empty(svc.Injected);
    }

    [Fact]
    public async Task IndexAsync_SupersededDependencyHashKey_IsNoLongerConsulted()
    {
        // The old key is retained on the DTO only to keep the MessagePack numbering append-only.
        // A snapshot carrying an outdated dependency hash must now be KEPT, or the narrower key
        // buys nothing.
        var depRoot = Root("dep");
        var modRoot = Root("mod");
        var depXml = Path.Combine(depRoot, "data", "xml");
        var modXml = Path.Combine(modRoot, "data", "xml");
        var depPgproj = Path.Combine(depRoot, "dep.pgproj").Replace('\\', '/').ToLowerInvariant();
        var modPgproj = Path.Combine(modRoot, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(depXml, "dep.xml")] = new("<Root/>"),
            [Path.Combine(modXml, "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        // Mod snapshot: own file hash MATCHES, but the recorded dependency hash is outdated.
        var fh = new FileHelper(fs);
        var unitsHash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(modXml, "units.xml"), fh.FileSystem);
        var modSnapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [new SerializedDependencyHash { ProjectPath = depPgproj, OverallHash = "outdated" }],
            Files =
            [
                new ProjectFileEntry
                {
                    RelativePath = "data/xml/units.xml", ContentHash = unitsHash,
                    Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
                }
            ],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [modPgproj] = modSnapshot };
        var config = new WorkspaceConfiguration([depXml, modXml], [], [], [], null)
        {
            Layers =
            [
                new ProjectLayer(0, "Dep", [depXml], [], [], [], null, depPgproj),
                new ProjectLayer(1, "Mod", [modXml], [], [], [], null, modPgproj)
            ]
        };
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [depRoot, modRoot], svc.Current.Baseline);
        StampCrossLayer(modSnapshot, fs, registry, config, modXml);

        await indexer.IndexAsync(config, CancellationToken.None);

        // The outdated dependency hash is ignored: only the dependency's own file is parsed, and
        // the mod's is still served from its snapshot.
        Assert.Single(svc.Updates);
        Assert.Single(svc.Injected);
    }

    /// <summary>
    ///     A cache hit skips the parser, so anything the parse published elsewhere has to be
    ///     replayed from the snapshot - otherwise a warm start silently loses it.
    /// </summary>
    /// <remarks>
    ///     The Lua parser was the instance until #155 retired its annotation repository: on a
    ///     layered mod most indexed files are Lua, so before this nearly every annotation in the
    ///     workspace was missing after a warm start.
    /// </remarks>
    [Fact]
    public async Task IndexAsync_CacheHit_ReplaysTheParsersOutOfBandState()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new([.. "<Root/>"u8])
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        var hash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(xmlDir, "units.xml"), fh.FileSystem);
        byte[] state = [7, 8, 9];
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything",
            DependencyHashes = [],
            Files =
            [
                new ProjectFileEntry
                {
                    RelativePath = "data/xml/units.xml", ContentHash = hash,
                    Document = new SerializedDocument
                    {
                        Symbols = [], References = [], RequireArgs = [], ParserState = state
                    }
                }
            ],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var registry = new FileTypeRegistry();
        var parser = new StatefulFakeParser();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache, parser);
        registrar.Register(config, [root], svc.Current.Baseline);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Single(svc.Injected);
        var restored = Assert.Single(parser.Restored);
        Assert.Equal(state, restored.State);
        Assert.Contains("units.xml", restored.Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndexAsync_SnapshotSave_CarriesTheParsersOutOfBandState()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new([.. "<Root/>"u8])
        });
        var svc = new FakeGameIndexService();
        var cache = new FakeProjectIndexCache();
        var config = ConfigWithLayer(xmlDir, pgproj);
        var parser = new StatefulFakeParser();
        var uri = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "units.xml"));
        parser.Captured[uri] = [4, 2];
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            parser);
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.True(cache.Saved.TryGetValue(pgproj, out var saved));
        Assert.Equal([4, 2], Assert.Single(saved.Files).Document.ParserState);
    }

    [Fact]
    public async Task IndexAsync_UnchangedSnapshot_SkipsSave()
    {
        // Nothing changed since the snapshot was written - re-serializing it every startup is
        // wasted work for large dependency layers.
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        const string relPath = "data/xml/units.xml";
        var hash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(xmlDir, "units.xml"), fh.FileSystem);
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = ProjectFileHasher.ComputeProjectHash([(relPath, hash)]),
            DependencyHashes = [],
            Files =
            [
                new ProjectFileEntry
                {
                    RelativePath = relPath, ContentHash = hash,
                    Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
                }
            ],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Single(svc.Injected); // cache hit as usual …
        Assert.Empty(cache.SavedPaths); // … and no redundant re-save
    }

    private static WorkspaceConfiguration ConfigWithLayer(string xmlDir, string pgprojPath)
    {
        var layer = new ProjectLayer(0, "TestMod", [xmlDir], [], [], [], null, pgprojPath);
        return new WorkspaceConfiguration([xmlDir], [], [], [], null) { Layers = [layer] };
    }

    // ── WorkspaceHost stays open-documents-only ───────────────────────────────

    [Fact]
    public async Task IndexAsync_FlatScan_DoesNotAddFilesToWorkspaceHost()
    {
        // The host tracks only open editor documents; indexing must not pin the whole workspace
        // corpus text in memory. Closed-file consumers read from disk on demand.
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>"),
            [Path.Combine(xmlDir, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Equal(2, svc.Updates.Count);
    }

    [Fact]
    public async Task IndexAsync_LayeredScan_CacheMiss_DoesNotAddFilesToWorkspaceHost()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new("<Root/>")
        });
        var svc = new FakeGameIndexService();
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, registrar) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates);
    }

    [Fact]
    public async Task IndexAsync_LayeredScan_CacheHit_InjectsIntoIndexWithoutHost()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fileContent = "<Root/>"u8.ToArray();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new(fileContent)
        });
        var svc = new FakeGameIndexService();

        var fh = new FileHelper(fs);
        var hash = ProjectFileHasher.ComputeFileHash(
            fh.FileSystem.Path.Combine(xmlDir, "units.xml"), fh.FileSystem);
        var entry = new ProjectFileEntry
        {
            RelativePath = "data/xml/units.xml", ContentHash = hash,
            Document = new SerializedDocument { Symbols = [], References = [], RequireArgs = [] }
        };
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "anything", DependencyHashes = [], Files = [entry],
            SchemaFingerprint = SchemaFingerprint.Compute(new SchemaProviderBuilder().Build())
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var registry = new FileTypeRegistry();
        var (indexer, registrar) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache, new FakeParser());
        registrar.Register(config, [root], svc.Current.Baseline);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexAsync(config, CancellationToken.None);

        Assert.Single(svc.Injected); // cache hit lands in the index; no text is pinned anywhere
    }


    private sealed class FakeProjectIndexCache : IProjectIndexCache
    {
        public readonly Dictionary<string, ProjectIndexSnapshot> Saved = [];
        public readonly Dictionary<(string Pgproj, string ContextKey), ProjectIndexSnapshot> SavedByContext = [];
        public readonly HashSet<string> SavedPaths = [];
        private readonly Dictionary<string, ProjectIndexSnapshot> _snapshots = [];

        public ProjectIndexSnapshot? this[string pgprojPath]
        {
            set => _snapshots[pgprojPath] = value!;
        }

        public ProjectIndexSnapshot? TryLoad(string pgprojPath, string contextKey)
        {
            return SavedByContext.TryGetValue((pgprojPath, contextKey), out var saved)
                ? saved
                : _snapshots.GetValueOrDefault(pgprojPath);
        }

        public void Save(string pgprojPath, string contextKey, ProjectIndexSnapshot snapshot)
        {
            SavedPaths.Add(pgprojPath);
            Saved[pgprojPath] = snapshot;
            SavedByContext[(pgprojPath, contextKey)] = snapshot;
        }

        public void EnsureGitHygiene(string pgprojPath)
        {
        }
    }

    /// <summary>An archive whose entry table is whatever the test says it is.</summary>

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

    /// <summary>
    ///     A parser with state published outside the document index, like the Lua one's annotation
    ///     repository wrote until #155.
    /// </summary>
    private sealed class StatefulFakeParser : IGameDocumentParser
    {
        public Dictionary<string, byte[]> Captured { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Uri, byte[] State)> Restored { get; } = [];

        public bool CanParse(string ext)
        {
            return ext.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public byte[]? CaptureParserState(string documentUri)
        {
            return Captured.GetValueOrDefault(documentUri);
        }

        public void RestoreParserState(string documentUri, byte[] state)
        {
            Restored.Add((documentUri, state));
        }
    }
}
