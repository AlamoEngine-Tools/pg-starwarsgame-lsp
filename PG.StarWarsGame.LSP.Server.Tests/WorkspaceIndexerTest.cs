// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceIndexerTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    /// <summary>One project at <paramref name="projectRoot" /> whose asset roots are <paramref name="assetRoots" />.</summary>
    private static WorkspaceConfiguration AssetProject(string projectRoot, params string[] assetRoots)
    {
        return new WorkspaceConfiguration([], [], [], assetRoots, null)
        {
            Layers = [AssetLayer(0, projectRoot, assetRoots)]
        };
    }

    private static ProjectLayer AssetLayer(int rank, string projectRoot, params string[] assetRoots)
    {
        return new ProjectLayer(rank, $"layer{rank}", [], [], [], assetRoots, null,
            Path.Combine(projectRoot, "mod.pgproj"));
    }

    private static string MegaFiles(params string[] entries)
    {
        return "<?xml version=\"1.0\" ?>\n<Mega_Files>\n" +
               string.Concat(entries.Select(e => $"\t<File> {e} </File>\n")) + "</Mega_Files>";
    }

    // ReSharper disable once UnusedTupleComponentInReturnValue - kept the same shape as the
    // parser-only overload, which one test reads the context from; 40 callers deconstruct both.
    private static (WorkspaceIndexer Indexer, EaWXmlContext Context) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        IProjectIndexCache? cache = null,
        params IGameDocumentParser[] parsers)
    {
        return BuildWithHost(fs, svc, registry, schema, cache, null, null, null, null, parsers);
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

    /// <summary>
    ///     Its own builder rather than another optional parameter on
    ///     <see
    ///         cref="Build(MockFileSystem, IGameIndexService, IFileTypeRegistry, ISchemaProvider, IProjectIndexCache, IGameDocumentParser[])" />
    ///     : that
    ///     one ends in <c>params IGameDocumentParser[]</c>, so anything inserted before it is
    ///     bound positionally by every existing caller that passes a parser.
    /// </summary>
    private static WorkspaceIndexer BuildWithBoneCache(
        MockFileSystem fs, IGameIndexService svc, IModelBoneCatalogCache boneCache)
    {
        return BuildWithHost(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            null, null, null, null, boneCache).Indexer;
    }

    // Overload kept for callers that pass parsers without a cache
    private static (WorkspaceIndexer Indexer, EaWXmlContext Context) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        return BuildWithHost(fs, svc, registry, schema, null, null, null, null, null, parsers);
    }

    private static WorkspaceIndexer BuildWithMegReader(
        MockFileSystem fs, IGameIndexService svc, IMegEntryReader megReader)
    {
        return BuildWithHost(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            null, null, null, megReader).Indexer;
    }

    private static (WorkspaceIndexer Indexer, EaWXmlContext Context) BuildWithHost(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        IProjectIndexCache? cache,
        IStoryChainProblemStore? storyProblems, ILspConfigurationProvider? config,
        IMegEntryReader? megReader = null,
        IModelBoneCatalogCache? boneCache = null,
        params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        var indexer = new WorkspaceIndexer(fh, svc, schema,
            new MetafileRegistrar(fh, registry, schema, ctx, storyProblems ?? new StoryChainProblemStore(),
                NullLogger<MetafileRegistrar>.Instance),
            new DocumentIndexer(fh, svc, cache ?? new NullProjectIndexCache(), new ParseableFiles(fh, parsers, ctx),
                new IndexFingerprints(schema, new LuaApiSchemaProvider([]), registry, fh),
                NullLogger<DocumentIndexer>.Instance),
            config ?? new FakeLspConfigurationProvider(),
            megReader ?? NoMegEntries.Instance,
            new MegArchiveLoadOrder(fh),
            boneCache ?? new NullModelBoneCatalogCache(),
            NullLogger<WorkspaceIndexer>.Instance);
        return (indexer, ctx);
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
        var svc = new FakeGameIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build());

        indexer.ApplyAssetCatalog(AssetProject(root, root));

        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/local.tga"));
        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/shipped.tga"));
    }

    /// <summary>
    ///     A workspace asset is catalogued by its GAME-relative path, not by its path relative to
    ///     whichever asset root happened to find it.
    /// </summary>
    /// <remarks>
    ///     A .pgproj declares its asset roots as <c>data/audio</c>, <c>data/art</c> and so on, so
    ///     normalising against the root that matched threw the <c>data/audio/</c> prefix away and
    ///     stored <c>announcer/foo.wav</c>. The baseline stores <c>data/...</c>, and references are
    ///     written game-relative too - EaWX writes <c>Data\Audio\Announcer\foo.wav</c> - so the two
    ///     halves of the merged catalog never met and every such reference read as a missing file.
    /// </remarks>
    [Fact]
    public void ApplyAssetCatalog_StoresWorkspaceAssetsGameRelative()
    {
        var root = Root("ws");
        var audioRoot = Path.Combine(root, "Data", "Audio");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(audioRoot, "Announcer", "Destroyed_ENG.wav")] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build());

        // The asset ROOT is the audio directory itself, which is how a .pgproj declares it.
        indexer.ApplyAssetCatalog(AssetProject(root, audioRoot));

        Assert.True(svc.Current.AssetFiles.Contains("data/audio/announcer/destroyed_eng.wav"),
            "expected the game-relative path, got: " + string.Join(", ", svc.Current.AssetFiles));
    }

    /// <summary>
    ///     A loose script is catalogued from the project's SCRIPT roots, which a .pgproj declares
    ///     apart from its asset roots, game-relative like every other entry.
    /// </summary>
    [Fact]
    public void ApplyAssetCatalog_CataloguesLooseScriptsFromTheScriptRoots()
    {
        var root = Root("ws");
        var scripts = Path.Combine(root, "Data", "Scripts");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(scripts, "GameObject", "BobaFettPlan.lua")] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build());

        indexer.ApplyAssetCatalog(new WorkspaceConfiguration([], [scripts], [], [], null));

        Assert.True(svc.Current.AssetFiles.Contains("data/scripts/gameobject/bobafettplan.lua"),
            "expected the script, got: " + string.Join(", ", svc.Current.AssetFiles));
    }

    /// <summary>
    ///     A root with no <c>data</c> segment above the file still yields something usable: the
    ///     path relative to the root, as before. Nothing outside a game tree is guessed at.
    /// </summary>
    [Fact]
    public void ApplyAssetCatalog_RootOutsideAGameTree_KeepsTheRootRelativePath()
    {
        var root = Root("loose");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "extra", "stray.tga")] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build());

        indexer.ApplyAssetCatalog(AssetProject(root, root));

        Assert.True(svc.Current.AssetFiles.Contains("extra/stray.tga"));
    }

    /// <summary>
    ///     What a project's archive holds counts as present, with no loose copy anywhere (#220).
    /// </summary>
    /// <remarks>
    ///     A mod ships its assets packed in <c>Data\</c>, beside its art and audio folders rather
    ///     than under them, and names the archives in its own <c>Data\MegaFiles.xml</c>. Only the
    ///     art and audio roots were searched for archives, so every reference into a mod's own
    ///     archives read as a missing file.
    /// </remarks>
    [Fact]
    public void ApplyAssetCatalog_ReadsTheArchivesTheProjectsListNames()
    {
        var root = Root("ws");
        var art = Path.Combine(root, "Data", "Art");
        var meg = Path.Combine(root, "Data", "ModTextures.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "MegaFiles.xml")] = new(MegaFiles(@"Data\ModTextures.meg")),
            [meg] = new("not a real archive - the reader is the seam")
        });
        fs.AddDirectory(art);
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var indexer = BuildWithMegReader(fs, svc,
            new FakeMegEntryReader((meg, [@"DATA\ART\TEXTURES\PACKED.TGA"])));

        indexer.ApplyAssetCatalog(AssetProject(root, art));

        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/packed.tga"));
    }

    /// <summary>The game loads no archive its list does not name, so neither does the catalog.</summary>
    [Fact]
    public void ApplyAssetCatalog_AnArchiveNoListNames_IsNotRead()
    {
        var root = Root("ws");
        var meg = Path.Combine(root, "Data", "Art", "Stray.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Data", "MegaFiles.xml")] = new(MegaFiles(@"Data\ModTextures.meg")),
            [meg] = new("not a real archive - the reader is the seam")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var indexer = BuildWithMegReader(fs, svc,
            new FakeMegEntryReader((meg, [@"DATA\ART\TEXTURES\STRAY.TGA"])));

        indexer.ApplyAssetCatalog(AssetProject(root, Path.Combine(root, "Data", "Art")));

        Assert.False(svc.Current.AssetFiles.Contains("data/art/textures/stray.tga"));
    }

    /// <summary>
    ///     A dependency's archives count too, its list read when the leaf has none, and a leaf
    ///     archive of a listed name replaces the dependency's.
    /// </summary>
    [Fact]
    public void ApplyAssetCatalog_ReadsTheDependencysArchives_InLayerOrder()
    {
        var leaf = Root("leaf");
        var dependency = Root("dependency");
        var shared = Path.Combine(dependency, "Data", "Shared.meg");
        var dependencyModels = Path.Combine(dependency, "Data", "Models.meg");
        var leafModels = Path.Combine(leaf, "Data", "Models.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(dependency, "Data", "MegaFiles.xml")] =
                new(MegaFiles(@"Data\Shared.meg", @"Data\Models.meg")),
            [shared] = new(""),
            [dependencyModels] = new(""),
            [leafModels] = new("")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var indexer = BuildWithMegReader(fs, svc, new FakeMegEntryReader(
            (shared, [@"DATA\ART\TEXTURES\SHARED.TGA"]),
            (dependencyModels, [@"DATA\ART\MODELS\REPLACED.ALO"]),
            (leafModels, [@"DATA\ART\MODELS\LEAF.ALO"])));
        var config = new WorkspaceConfiguration([], [], [], [], null)
        {
            Layers = [AssetLayer(0, dependency), AssetLayer(1, leaf)]
        };

        indexer.ApplyAssetCatalog(config);

        Assert.True(svc.Current.AssetFiles.Contains("data/art/textures/shared.tga"));
        Assert.True(svc.Current.AssetFiles.Contains("data/art/models/leaf.alo"));
        Assert.False(svc.Current.AssetFiles.Contains("data/art/models/replaced.alo"));
    }

    /// <summary>
    ///     An SFX archive IS the <c>data/audio/sfx</c> directory, so its flat entries are stored
    ///     under that prefix - the same rule the baseline builder applies, from the same code.
    /// </summary>
    [Fact]
    public void ApplyAssetCatalog_AppliesSfxConventionsToWorkspaceArchives()
    {
        var root = Root("ws");
        var meg = Path.Combine(root, "Data", "Audio", "SFX", "sfx2d_english.meg");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [meg] = new("not a real archive - the reader is the seam")
        });
        var svc = new FakeGameIndexService(GameIndex.Empty);
        var indexer = BuildWithMegReader(fs, svc,
            new FakeMegEntryReader((meg, ["U000_SPD0101_ENG.WAV"])));

        indexer.ApplyAssetCatalog(AssetProject(root, Path.Combine(root, "Data", "Audio")));

        Assert.True(svc.Current.AssetFiles.Contains("data/audio/sfx/u000_spd0101_eng.wav"));
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
        var svc = new FakeGameIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build());

        indexer.ApplyModelBoneCatalog(WorkspaceConfiguration.Empty with { AssetRoots = [root] });

        Assert.Equal(["root", "muzzle_bone"], svc.Current.ModelBones["data/art/models/shipped.alo"].ToArray());
    }

    // Models and particle systems share the extension (#270): the scan that reads every .alo records
    // which are particle systems, merged over the baseline's - a mod's model replaces a shipped
    // particle system of the same name, as every other model catalog entry does.
    [Fact]
    public void ApplyModelBoneCatalog_PublishesTheParticleSystems_TheModsOverTheBaselines()
    {
        var root = Root("ws");
        var models = Path.Combine(root, "Data", "Art", "Models");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(models, "P_Smoke.alo")] = new([0x00, 0x09, 0x00, 0x00]),
            [Path.Combine(models, "My_Frigate.alo")] = new([0x00, 0x02, 0x00, 0x00])
        });
        var baseline = BaselineIndex.Empty with
        {
            ParticleSystems = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "p_shipped.alo",
                "my_frigate.alo")
        };
        var svc = new FakeGameIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build());

        indexer.ApplyModelBoneCatalog(WorkspaceConfiguration.Empty with
        {
            AssetRoots = [Path.Combine(root, "Data", "Art")]
        });

        Assert.Equal(["p_shipped.alo", "p_smoke.alo"], svc.Current.ParticleSystems.Order());
    }

    /// <summary>
    ///     A workspace that resolved no projects - a heuristic scan - still has to produce a
    ///     catalog. It cannot be cached (there is no .pgproj to keep a snapshot beside), so the
    ///     flattened asset roots stand in for a layer, and the behaviour must be what it was
    ///     before snapshots existed.
    /// </summary>
    [Fact]
    public void ApplyModelBoneCatalog_NoLayers_StillAppliesFromTheFlattenedRoots()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var baseline = BaselineIndex.Empty with
        {
            ModelBones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("shipped.alo", ["root"])
        };
        var svc = new FakeGameIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build());

        indexer.ApplyModelBoneCatalog(WorkspaceConfiguration.Empty with { AssetRoots = [root], Layers = [] });

        Assert.True(svc.Current.ModelBones.ContainsKey("shipped.alo"));
    }

    // A model's proxies name the particle systems it carries (#270): the scan that reads every .alo
    // records them, merged over the baseline's like the rest of the model catalog, so a particle
    // system can list the models that carry it.
    [Fact]
    public void ApplyModelBoneCatalog_PublishesTheModelsProxies_TheModsOverTheBaselines()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Root("mod/art"));
        var baseline = BaselineIndex.Empty with
        {
            ModelProxies = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("shipped.alo", ["P_Shipped"]).Add("my_frigate.alo", ["P_Old_Engine"])
        };
        var svc = new FakeGameIndexService(GameIndex.Empty with { Baseline = baseline });
        var cache = new StubBoneCache
        {
            Stored =
            {
                [Root("mod/mod.pgproj")] = new Dictionary<string, ModelCatalogEntry>
                {
                    ["my_frigate.alo"] = new(["root"], []) { Proxies = ["P_Engine_ALT2"] }
                }
            }
        };
        var indexer = BuildWithBoneCache(fs, svc, cache);

        indexer.ApplyModelBoneCatalog(WorkspaceConfiguration.Empty with
        {
            AssetRoots = [Root("mod/art")],
            Layers = [new ProjectLayer(0, "mod", [], [], [], [Root("mod/art")], null, Root("mod/mod.pgproj"))]
        });

        Assert.Equal(["P_Engine_ALT2"], svc.Current.ModelProxies["my_frigate.alo"].ToArray());
        Assert.Equal(["P_Shipped"], svc.Current.ModelProxies["shipped.alo"].ToArray());
    }

    /// <summary>
    ///     The reason the snapshot is per LAYER: a layer whose models are unchanged must be served
    ///     from its own snapshot, and must NOT be re-extracted because a sibling layer changed.
    /// </summary>
    [Fact]
    public void ApplyModelBoneCatalog_ReusesASnapshotPerLayer()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Root("dep/art"));
        fs.AddDirectory(Root("leaf/art"));
        var svc = new FakeGameIndexService(GameIndex.Empty);
        // Only the dependency has a snapshot; the leaf must still be extracted and then saved.
        var cache = new StubBoneCache
        {
            Stored =
            {
                [Root("dep/dep.pgproj")] = new Dictionary<string, ModelCatalogEntry>
                {
                    ["dep_model.alo"] = new(["dep_bone"], ["dep_texture.tga"])
                }
            }
        };
        var indexer = BuildWithBoneCache(fs, svc, cache);

        indexer.ApplyModelBoneCatalog(WorkspaceConfiguration.Empty with
        {
            AssetRoots = [Root("dep/art"), Root("leaf/art")],
            Layers =
            [
                new ProjectLayer(0, "dep", [], [], [], [Root("dep/art")], null, Root("dep/dep.pgproj")),
                new ProjectLayer(1, "leaf", [], [], [], [Root("leaf/art")], null, Root("leaf/leaf.pgproj"))
            ]
        });

        Assert.Equal(["dep_bone"], svc.Current.ModelBones["dep_model.alo"].ToArray());
        // The dependency was reused, so it was never re-saved; the leaf missed and was.
        Assert.DoesNotContain(Root("dep/dep.pgproj"), cache.Saved);
        Assert.Contains(Root("leaf/leaf.pgproj"), cache.Saved);
        Assert.Equal(new BoneCatalogStats(1, 2), indexer.LastBoneCatalog);
    }

    // ── Dynamic enum catalog ─────────────────────────────────────────────────

    [Fact]
    public void ApplyDynamicEnumCatalog_ParsesEnumFile_AndPublishesValues()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var enumFilePath = Path.Combine(xmlDir, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] =
                new("<EnumDefinition><GENERIC_TRACK>0</GENERIC_TRACK><MY_MOD_TRACK>99</MY_MOD_TRACK></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.True(svc.Current.WorkspaceDynamicEnumValues.TryGetValue("SurfaceFXTriggerType", out var vals));
        Assert.Contains("GENERIC_TRACK", vals);
        Assert.Contains("MY_MOD_TRACK", vals);
    }

    [Fact]
    public void ApplyDynamicEnumCatalog_MissingEnumFile_PublishesEmptyDict()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.Empty(svc.Current.WorkspaceDynamicEnumValues);
    }

    [Fact]
    public void ApplyDynamicEnumCatalog_SearchesByFilenameOnly_NotFullPath()
    {
        var root = Root("ws");
        // The enum file is directly in the xml root, not in an enum subdirectory.
        var xmlDir = Path.Combine(root, "data", "xml");
        var enumFilePath = Path.Combine(xmlDir, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] = new("<EnumDefinition><CUSTOM>1</CUSTOM></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.True(svc.Current.WorkspaceDynamicEnumValues.TryGetValue("SurfaceFXTriggerType", out var vals));
        Assert.Contains("CUSTOM", vals);
    }

    [Fact]
    public void ApplyDynamicEnumCatalog_RescanAfterEdit_PicksUpTheNewValue()
    {
        // The live path: the file gains a value on disk and the catalog is re-scanned. Adding a
        // value to a dynamic enum and having it stay "unknown" is the bug this guards.
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var enumFilePath = Path.Combine(xmlDir, "gameobjectpropertiestype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] = new("<EnumDefinition><Turret>0x1</Turret></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "GameObjectPropertiesType", Kind = EnumKind.DynamicXml,
            SourceFile = "gameobjectpropertiestype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);
        Assert.DoesNotContain("IsANewFlag",
            svc.Current.WorkspaceDynamicEnumValues["GameObjectPropertiesType"]);

        fs.File.WriteAllText(enumFilePath,
            "<EnumDefinition><Turret>0x1</Turret><IsANewFlag>0x2</IsANewFlag></EnumDefinition>");

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.Contains("IsANewFlag",
            svc.Current.WorkspaceDynamicEnumValues["GameObjectPropertiesType"]);
    }

    [Fact]
    public void ApplyDynamicEnumCatalog_EnumFileInSubdirectory_IsFound()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var enumSubDir = Path.Combine(xmlDir, "enum");
        var enumFilePath = Path.Combine(enumSubDir, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] = new("<EnumDefinition><GENERIC_TRACK>0</GENERIC_TRACK></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]); // pass parent dir, not the enum subdir

        Assert.True(svc.Current.WorkspaceDynamicEnumValues.TryGetValue("SurfaceFXTriggerType", out var vals));
        Assert.Contains("GENERIC_TRACK", vals);
    }

    [Fact]
    public void ApplyDynamicEnumCatalog_AnchorFormatEnum_PublishesEnumValueDefinitionsWithExactPosition()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var filePath = Path.Combine(xmlDir, "gameconstants.xml");
        const string xml = "<GameConstants><Damage_Types>EXPLOSIVE ENERGY</Damage_Types></GameConstants>";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [filePath] = new(xml) });
        var enumDef = new EnumDefinition
        {
            Name = "DamageType", Kind = EnumKind.DynamicXml,
            SourceFile = "data/xml/gameconstants.xml$Damage_Types", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.True(svc.Current.WorkspaceEnumValueDefinitions.TryGetValue("DamageType", out var valueMap));
        Assert.True(valueMap.TryGetValue("EXPLOSIVE", out var explosiveOrigin));
        Assert.True(explosiveOrigin.IsNavigable);
        Assert.True(valueMap.TryGetValue("ENERGY", out var energyOrigin));
        // ENERGY comes after EXPLOSIVE in the same line, so its column must be greater
        Assert.True(energyOrigin.Column > explosiveOrigin.Column);
    }

    [Fact]
    public void ApplyDynamicEnumCatalog_FileFormatEnum_PublishesEnumValueDefinitions()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var enumFilePath = Path.Combine(xmlDir, "surfacefxtriggertype.xml");
        const string xml = """
                           <EnumDefinition>
                               <GENERIC_TRACK>0</GENERIC_TRACK>
                               <MY_MOD_TRACK>99</MY_MOD_TRACK>
                           </EnumDefinition>
                           """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [enumFilePath] = new(xml) });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.True(svc.Current.WorkspaceEnumValueDefinitions
            .TryGetValue("SurfaceFXTriggerType", out var valueMap));
        Assert.True(valueMap.TryGetValue("GENERIC_TRACK", out var origin));
        Assert.True(origin.IsNavigable);
        Assert.True(origin.Line > 0); // must be inside the file, past the root element
    }

    [Fact]
    public void ApplyDynamicEnumCatalog_RootProjectOverridesDependency_UsesRootValues()
    {
        // dep has surfacefxtriggertype.xml with DEP_TRACK; root has it with ROOT_TRACK.
        // config.XmlDirectories is [depXml, rootXml] (deps-first, root-last).
        // Root's file must win because it has the higher layer rank.
        var depXml = Path.Combine(Root("dep"), "data", "xml");
        var rootXml = Path.Combine(Root("root"), "data", "xml");
        var depFile = Path.Combine(depXml, "surfacefxtriggertype.xml");
        var rootFile = Path.Combine(rootXml, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [depFile] = new("<EnumDefinition><DEP_TRACK>0</DEP_TRACK></EnumDefinition>"),
            [rootFile] = new("<EnumDefinition><ROOT_TRACK>0</ROOT_TRACK></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        // Pass roots in deps-first / root-last order (same as ModProjectResolver produces).
        indexer.ApplyDynamicEnumCatalog([depXml, rootXml]);

        var vals = svc.Current.WorkspaceDynamicEnumValues["SurfaceFXTriggerType"];
        Assert.Contains("ROOT_TRACK", vals); // root project's value is present
        Assert.DoesNotContain("DEP_TRACK", vals); // dependency value is suppressed by override
    }

    private sealed class StubBoneCache : IModelBoneCatalogCache
    {
        public Dictionary<string, IReadOnlyDictionary<string, ModelCatalogEntry>> Stored { get; } = new();
        public List<string> Saved { get; } = [];

        public IReadOnlyDictionary<string, ModelCatalogEntry>? TryLoad(string pgprojPath, string fingerprint)
        {
            return Stored.GetValueOrDefault(pgprojPath);
        }

        public void Save(
            string pgprojPath, string fingerprint, IReadOnlyDictionary<string, ModelCatalogEntry> models)
        {
            Saved.Add(pgprojPath);
        }
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     The disk, as the indexer sees it. A snapshot SEEDED by a test (the indexer) answers for
    ///     any context key - the test stamps its fingerprint to say whether it is current. A
    ///     snapshot SAVED by the indexer is filed under its context key, like the real cache, so a
    ///     test can tell "served from the snapshot of this context" from "re-parsed".
    /// </summary>
    private sealed class FakeMegEntryReader(params (string Path, IReadOnlyList<string> Entries)[] archives)
        : IMegEntryReader
    {
        public IEnumerable<string> EnumerateEntryPaths(string path)
        {
            var normalized = path.Replace('/', '\\');
            return archives.FirstOrDefault(a => a.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                .Entries ?? [];
        }
    }


}