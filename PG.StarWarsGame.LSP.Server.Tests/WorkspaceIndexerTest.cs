// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceIndexerTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static (WorkspaceIndexer Indexer, EaWXmlContext Context) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        var indexer = new WorkspaceIndexer(fh, parsers, svc, registry, schema, ctx,
            NullLogger<WorkspaceIndexer>.Instance);
        return (indexer, ctx);
    }

    // ── PreScanMetafiles ─────────────────────────────────────────────────────

    [Fact]
    public void PreScanMetafiles_RegistersFileTypes_FromMetafileOnDisk()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var metafilePath = Path.Combine(xmlDir, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new("<Game_Object_Files><File>DATA\\XML\\HARDPOINTS.XML</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var (indexer, _) = Build(fs, new FakeIndexService(), registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var expectedKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public void PreScanMetafiles_SeedsXmlDirectories_IntoContext()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var (indexer, ctx) = Build(fs, new FakeIndexService(), new FileTypeRegistry(), new FakeSchemaProvider());

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var xmlFileUri = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "units.xml"));
        Assert.True(ctx.IsEaWXmlFile(xmlFileUri));
    }

    [Fact]
    public void PreScanMetafiles_FallsBackToBaseline_UsingXmlDirs_WhenMetafileAbsent()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "xml"); // non-standard layout
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var fileTypeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("data/xml/hardpoints.xml", ImmutableArray.Create("GameObjectType"));
        var baseline = BaselineIndex.Empty with { FileTypeMap = fileTypeMap };
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var svc = new FakeIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var expectedKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public void PreScanMetafiles_SkipsSpecialMetafiles()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var campaignPath = Path.Combine(xmlDir, "CAMPAIGNS.XML");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [campaignPath] = new("<Campaigns/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/campaigns.xml", MetafileType.Special, ["StoryParser"]));
        var (indexer, _) = Build(fs, new FakeIndexService(), registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        Assert.Empty(registry.All);
    }

    // ── IndexDocumentsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_IndexesXmlFiles_InXmlDirectories()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>"),
            [Path.Combine(xmlDir, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        var count = await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, svc.Calls.Count);
        Assert.Equal(1, svc.BeginBulkUpdateCallCount);
        Assert.All(svc.Calls, c => Assert.Equal(0, c.Version));
        Assert.All(svc.Calls, c => Assert.StartsWith("file:///", c.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexDocumentsAsync_SkipsFilesOutsideXmlDirectories()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new("<Root/>"),
            [Path.Combine(root, "scripts", "build.xml")] = new("<project/>")
        });
        var svc = new FakeIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Calls);
        Assert.EndsWith("units.xml", svc.Calls[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndexDocumentsAsync_IndexesLuaFiles_FromScriptRoots()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var scriptDir = Path.Combine(root, "data", "scripts");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(scriptDir, "story.lua")] = new("function Definitions() end")
        });
        var svc = new FakeIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [scriptDir], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(),
            new FakeParser(), new FakeParser(".lua"));
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Contains(svc.Calls, c => c.Uri.EndsWith("story.lua", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexDocumentsAsync_PreCancelledToken_Throws_NoIndex()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), new FakeParser());
        indexer.PreScanMetafiles(config, [root]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            indexer.IndexDocumentsAsync(config, cts.Token));

        Assert.Empty(svc.Calls);
    }

    [Fact]
    public async Task IndexDocumentsAsync_ReportsProgress()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>"),
            [Path.Combine(xmlDir, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        var maxTotal = 0;
        var maxDone = 0;
        await indexer.IndexDocumentsAsync(config, CancellationToken.None, (done, total) =>
        {
            maxTotal = total;
            if (done > maxDone) maxDone = done;
        });

        Assert.Equal(2, maxTotal);
        Assert.Equal(2, maxDone);
    }

    // ── Asset / bone catalogs ────────────────────────────────────────────────

    [Fact]
    public void ApplyAssetCatalog_MergesBaselineAndWorkspace()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "Art", "Textures", "Local.tga")] = new("")
        });
        var baseline = BaselineIndex.Empty with
        {
            AssetFiles = ImmutableHashSet.Create("data/art/textures/shipped.tga")
        };
        var svc = new FakeIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider());

        indexer.ApplyAssetCatalog([root]);

        Assert.NotNull(svc.AppliedAssetFiles);
        Assert.True(svc.AppliedAssetFiles!.Contains("data/art/textures/local.tga"));
        Assert.True(svc.AppliedAssetFiles.Contains("data/art/textures/shipped.tga"));
    }

    [Fact]
    public void ApplyModelBoneCatalog_AppliesBaselineBones()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var baseline = BaselineIndex.Empty with
        {
            ModelBones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("data/art/models/shipped.alo", ["root", "muzzle_bone"])
        };
        var svc = new FakeIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider());

        indexer.ApplyModelBoneCatalog([root]);

        Assert.NotNull(svc.AppliedModelBones);
        Assert.Equal(["root", "muzzle_bone"], svc.AppliedModelBones!["data/art/models/shipped.alo"].ToArray());
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

        public FakeSchemaProvider(params MetafileDefinition[] metafiles)
        {
            _metafiles = metafiles;
        }

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
        public readonly List<(string Uri, int Version)> Calls = [];
        public int BeginBulkUpdateCallCount;

        public FakeIndexService(GameIndex? current = null)
        {
            Current = current ?? GameIndex.Empty;
        }

        public IAssetFileIndex? AppliedAssetFiles { get; private set; }

        public ImmutableDictionary<string, ImmutableArray<string>>? AppliedModelBones { get; private set; }

        public GameIndex Current { get; }

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (Calls)
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

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
            AppliedAssetFiles = index;
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
            AppliedModelBones = bones;
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