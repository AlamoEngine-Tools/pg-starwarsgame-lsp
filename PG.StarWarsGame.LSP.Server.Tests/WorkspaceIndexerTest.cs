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
        var indexer = new WorkspaceIndexer(fh, parsers, svc, registry, schema, ctx,
            cache ?? new NullProjectIndexCache(),
            storyProblems ?? new StoryChainProblemStore(),
            config ?? new FakeLspConfigurationProvider(),
            megReader ?? NoMegEntries.Instance,
            new MegArchiveLoadOrder(fh),
            boneCache ?? new NullModelBoneCatalogCache(),
            NullLogger<WorkspaceIndexer>.Instance, new LuaApiSchemaProvider([]));
        return (indexer, ctx);
    }

    // ── PreScanMetafiles ─────────────────────────────────────────────────────

    /// <summary>
    ///     The registry file is read by the game too - by a name compiled into it - so it is
    ///     registered under its own URI, with no types because the schema does not model its
    ///     contents. Only the files it listed used to be registered, and the registry itself was
    ///     reported as read by nothing.
    /// </summary>
    [Fact]
    public void PreScanMetafiles_RegistersTheRegistryFileItself_WithoutTypes()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var metafilePath = Path.Combine(xmlDir, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new(@"<Game_Object_Files><File>DATA\XML\HARDPOINTS.XML</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var ownKey = new FileHelper(fs).PathToFileUri(metafilePath);
        Assert.True(((IFileTypeRegistry)registry).IsRegistered(ownKey));
        Assert.Empty(registry.GetTypesForFile(ownKey));
    }

    [Fact]
    public void PreScanMetafiles_RegistersFileTypes_FromMetafileOnDisk()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var metafilePath = Path.Combine(xmlDir, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new(@"<Game_Object_Files><File>DATA\XML\HARDPOINTS.XML</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var expectedKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public void PreScanMetafiles_SubdirEntry_RegistersUnderEveryXmlRoot_PreservingSubdir()
    {
        // Real case: the mod (rev) ships GameObjectFiles.xml listing subdirectory paths that resolve
        // to object files in its dependency (core). The type must be registered at the subdir path
        // under BOTH xml roots - not stripped to a bare filename under the metafile's own directory.
        var revRoot = Root("rev");
        var revXml = Path.Combine(revRoot, "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var metafilePath = Path.Combine(revXml, "gameobjectfiles.xml");

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new(
                "<Game_Object_Files><File>Units\\Death_Clones.xml</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry, schema);

        // config.XmlDirectories spans both projects (dependency first, root last).
        indexer.PreScanMetafiles(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot]);

        var fh = new FileHelper(fs);
        var coreKey = fh.PathToFileUri(Path.Combine(coreXml, "Units", "Death_Clones.xml"));
        var revKey = fh.PathToFileUri(Path.Combine(revXml, "Units", "Death_Clones.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(coreKey).ToArray());
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(revKey).ToArray());
    }

    [Fact]
    public void PreScanMetafiles_MetafileShippedByDependency_IsFoundAndRegistersTypes()
    {
        // The metafile lives in the dependency (core), not in the open workspace (rev). It must still
        // be discovered via the declared xml roots, and its (subdir) entries registered.
        var revRoot = Root("rev");
        var revXml = Path.Combine(revRoot, "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var metafilePath = Path.Combine(coreXml, "hardpointdatafiles.xml"); // shipped by core

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new(
                "<HardPointDataFiles><File>HardPoints\\HP_Weapons.xml</File></HardPointDataFiles>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/hardpointdatafiles.xml", MetafileType.FileRegistry, ["HardPoint"]))
            .Build();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot]);

        var fh = new FileHelper(fs);
        var coreKey = fh.PathToFileUri(Path.Combine(coreXml, "HardPoints", "HP_Weapons.xml"));
        Assert.Equal(["HardPoint"], registry.GetTypesForFile(coreKey).ToArray());
    }

    [Fact]
    public void PreScanMetafiles_MetafileShippedByBothLayers_OnlyTopmostCopyIsRead()
    {
        // The engine resolves a file by name, first-found-wins - it never merges two copies of
        // GameObjectFiles.xml. When the mod (rev) ships its own, the dependency's (core) copy is
        // shadowed entirely, which is why modders copy the base list into their own registry.
        var revRoot = Root("rev");
        var revXml = Path.Combine(revRoot, "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(coreXml, "gameobjectfiles.xml")] =
                new("<Game_Object_Files><File>Core_Only.xml</File></Game_Object_Files>"),
            [Path.Combine(revXml, "gameobjectfiles.xml")] =
                new("<Game_Object_Files><File>Rev_Only.xml</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot]);

        var fh = new FileHelper(fs);
        Assert.Equal(["GameObjectType"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Rev_Only.xml"))).ToArray());
        // The shadowed registry's entry must not be typed under ANY root.
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Core_Only.xml"))));
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Core_Only.xml"))));
    }

    [Fact]
    public void PreScanMetafiles_SeedsXmlDirectories_IntoContext()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var (indexer, ctx) = Build(fs, new FakeGameIndexService(), new FileTypeRegistry(),
            new SchemaProviderBuilder().Build());

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
            .Add("data/xml/hardpoints.xml", ["GameObjectType"]);
        var baseline = BaselineIndex.Empty with { FileTypeMap = fileTypeMap };
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
        var svc = new FakeGameIndexService(GameIndex.Empty with { Baseline = baseline });
        var (indexer, _) = Build(fs, svc, registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var expectedKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    // ── PreScanMetafiles: story chain (MetafileType.Special) ─────────────────

    private static MetafileDefinition StorySpecialDef()
    {
        return new MetafileDefinition("data/xml/campaignfiles.xml", MetafileType.Special,
            ["Campaign", "StoryParser", "StoryPlotManifest"]);
    }

    private static Dictionary<string, MockFileData> StoryChainFixture(string xmlDir)
    {
        return new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "campaignfiles.xml")] =
                new("<Campaign_Files><File>Campaigns_Test.xml</File></Campaign_Files>"),
            [Path.Combine(xmlDir, "Campaigns_Test.xml")] = new(
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Rebel.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>"),
            [Path.Combine(xmlDir, "Story_Plots_Rebel.xml")] = new(
                "<Story_Mode_Plots><Active_Plot>Story_Rebel_Act_I.xml</Active_Plot></Story_Mode_Plots>"),
            [Path.Combine(xmlDir, "Story_Rebel_Act_I.xml")] = new(
                "<Story_Threads><Event Name=\"E1\"><Event_Type>STORY_TRIGGER</Event_Type></Event></Story_Threads>")
        };
    }

    /// <summary>
    ///     The campaign registry is opened by the game by its compiled-in name, like every other
    ///     registry, so it is registered under its own URI with no types - the chain it starts
    ///     types the files it reaches, not the registry.
    /// </summary>
    [Fact]
    public void PreScanMetafiles_SpecialDef_RegistersTheRegistryFileItself_WithoutTypes()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(StoryChainFixture(xmlDir));
        var registry = new FileTypeRegistry();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var ownKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "campaignfiles.xml"));
        Assert.True(((IFileTypeRegistry)registry).IsRegistered(ownKey));
        Assert.Empty(registry.GetTypesForFile(ownKey));
    }

    [Fact]
    public void PreScanMetafiles_SpecialDef_TypesManifestsAndThreadsAlongTheChain()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(StoryChainFixture(xmlDir));
        var registry = new FileTypeRegistry();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "Story_Plots_Rebel.xml"))).ToArray());
        Assert.Equal(["StoryParser"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "Story_Rebel_Act_I.xml"))).ToArray());
    }

    [Fact]
    public void PreScanMetafiles_SpecialDef_UnresolvedStoryName_LandsInProblemStore()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var files = StoryChainFixture(xmlDir);
        files.Remove(Path.Combine(xmlDir, "Story_Plots_Rebel.xml"));
        var fs = new MockFileSystem(files);
        var registry = new FileTypeRegistry();
        var store = new StoryChainProblemStore();
        var (indexer, _) = BuildWithHost(fs, new FakeGameIndexService(), registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build(), null, store, null);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var fh = new FileHelper(fs);
        var campaignUri = fh.NormalizeUri(Path.Combine(xmlDir, "Campaigns_Test.xml"));
        var problem = Assert.Single(store.GetForDocument(campaignUri));
        Assert.Equal(StoryChainProblemKind.UnresolvedStoryName, problem.Kind);
        Assert.Equal("Story_Plots_Rebel.xml", problem.Reference);
    }

    [Fact]
    public void PreScanMetafiles_SpecialDef_ChainSpansXmlRoots_AndRegistersUnderEveryRoot()
    {
        // The campaign lives in the mod (rev); the manifest and thread ship with the dependency
        // (core). Resolution must search every xml root, and registrations must cover both roots
        // (mirroring how file-registry entries are registered).
        var revXml = Path.Combine(Root("rev"), "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(revXml, "campaignfiles.xml")] =
                new("<Campaign_Files><File>Campaigns_Mod.xml</File></Campaign_Files>"),
            [Path.Combine(revXml, "Campaigns_Mod.xml")] = new(
                "<Campaigns><Campaign Name=\"T\">" +
                "<Rebel_Story_Name>Story_Plots_Core.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>"),
            [Path.Combine(coreXml, "Story_Plots_Core.xml")] = new(
                "<Story_Mode_Plots><Active_Plot>Story_Core_Act.xml</Active_Plot></Story_Mode_Plots>"),
            [Path.Combine(coreXml, "Story_Core_Act.xml")] = new("<Story_Threads/>")
        });
        var registry = new FileTypeRegistry();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        indexer.PreScanMetafiles(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [Root("rev")]);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Story_Plots_Core.xml"))).ToArray());
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Story_Plots_Core.xml"))).ToArray());
        Assert.Equal(["StoryParser"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Story_Core_Act.xml"))).ToArray());
    }

    [Fact]
    public void PreScanMetafiles_SpecialDef_RegistryShippedByBothLayers_OnlyTopmostCopyIsRead()
    {
        // Same override rule as any other metafile: the mod's campaignfiles.xml shadows the
        // dependency's, so only the campaigns it lists exist. The winning registry may still point
        // at files that live in the dependency - that is covered by ChainSpansXmlRoots above.
        var revRoot = Root("rev");
        var revXml = Path.Combine(revRoot, "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(coreXml, "campaignfiles.xml")] =
                new("<Campaign_Files><File>Campaigns_Core.xml</File></Campaign_Files>"),
            [Path.Combine(coreXml, "Campaigns_Core.xml")] = new(
                "<Campaigns><Campaign Name=\"C\">" +
                "<Rebel_Story_Name>Story_Plots_Core.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>"),
            [Path.Combine(coreXml, "Story_Plots_Core.xml")] = new("<Story_Mode_Plots/>"),

            [Path.Combine(revXml, "campaignfiles.xml")] =
                new("<Campaign_Files><File>Campaigns_Mod.xml</File></Campaign_Files>"),
            [Path.Combine(revXml, "Campaigns_Mod.xml")] = new(
                "<Campaigns><Campaign Name=\"M\">" +
                "<Rebel_Story_Name>Story_Plots_Mod.xml</Rebel_Story_Name>" +
                "</Campaign></Campaigns>"),
            [Path.Combine(revXml, "Story_Plots_Mod.xml")] = new("<Story_Mode_Plots/>")
        });
        var registry = new FileTypeRegistry();
        var (indexer, _) = Build(fs, new FakeGameIndexService(), registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        indexer.PreScanMetafiles(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot]);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Story_Plots_Mod.xml"))).ToArray());
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Story_Plots_Core.xml"))));
    }

    [Fact]
    public void PreScanMetafiles_SpecialDef_RegistryAbsentEverywhere_FallsBackToBaselineFileTypeMap()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var fileTypeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("story_plots_vanilla.xml", ["StoryPlotManifest"])
            .Add("story_vanilla_act.xml", ["StoryParser"]);
        var svc = new FakeGameIndexService(GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with { FileTypeMap = fileTypeMap }
        });
        var registry = new FileTypeRegistry();
        var (indexer, _) = Build(fs, svc, registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root]);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "story_plots_vanilla.xml"))).ToArray());
        Assert.Equal(["StoryParser"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "story_vanilla_act.xml"))).ToArray());
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
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        var count = await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, svc.Updates.Count);
        Assert.Equal(1, svc.BulkUpdates);
        Assert.All(svc.Updates, c => Assert.Equal(0, c.Version));
        Assert.All(svc.Updates, c => Assert.StartsWith("file:///", c.Uri, StringComparison.Ordinal));
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
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        var call = Assert.Single(svc.Updates);
        Assert.EndsWith("units.xml", call.Uri, StringComparison.OrdinalIgnoreCase);
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
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [scriptDir], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser(), new FakeParser(".lua"));
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Contains(svc.Updates, c => c.Uri.EndsWith("story.lua", StringComparison.OrdinalIgnoreCase));
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
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            indexer.IndexDocumentsAsync(config, cts.Token));

        Assert.Empty(svc.Updates);
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
        var svc = new FakeGameIndexService();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
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

    // ── IndexDocumentsAsync with cache ──────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_WithLayers_NoSnapshot_ParsesNormally()
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
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates); // parsed normally
        Assert.Empty(svc.Injected);
        Assert.Equal(new IndexCacheStats(0, 1, 0, 1),
            indexer.LastIndexCache);
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithLayers_CacheHit_InjectsWithoutParsing()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

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
    public async Task IndexDocumentsAsync_CacheHit_WhenTheSnapshotRecordsTheFilesRealCase()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Empty(svc.Updates); // no re-parse
        Assert.Single(svc.Injected); // injected from cache
    }

    [Fact]
    public async Task IndexDocumentsAsync_CacheHit_EvenWhenTheSnapshotSpellsThePathInAnotherCase()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Empty(svc.Updates);
        Assert.Single(svc.Injected);
    }

    [Fact]
    public async Task IndexDocumentsAsync_SnapshotRecordsTheRelativePathWithItsCaseIntact()
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
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        var saved = Assert.Single(cache.Saved).Value;
        Assert.Equal("Data/XML/Units.xml", Assert.Single(saved.Files).RelativePath);
    }

    [Fact]
    public async Task IndexDocumentsAsync_SchemaFingerprintChanged_DiscardsLayerSnapshot()
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
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates); // re-parsed
        Assert.Empty(svc.Injected);
    }

    [Fact]
    public async Task IndexDocumentsAsync_WritesSnapshotWithCurrentSchemaFingerprint()
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
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema, cache, new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

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
    public async Task IndexDocumentsAsync_LuaStubsChanged_SnapshotFingerprintChanges()
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
        var fh = new FileHelper(fs);
        var indexer = new WorkspaceIndexer(fh, [new FakeParser()], new FakeGameIndexService(), new FileTypeRegistry(),
            new SchemaProviderBuilder().Build(), new EaWXmlContext(fh), cache, new StoryChainProblemStore(),
            new FakeLspConfigurationProvider(), NoMegEntries.Instance, new MegArchiveLoadOrder(fh),
            new NullModelBoneCatalogCache(),
            NullLogger<WorkspaceIndexer>.Instance, new LuaApiSchemaProvider([luaStub]));
        var config = ConfigWithLayer(xmlDir, pgproj);
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        return cache.Saved[pgproj].SchemaFingerprint;
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithLayers_StaleHash_Reparses()
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
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates); // re-parsed
        Assert.Empty(svc.Injected);
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithLayers_WritesSnapshotAfterIndexing()
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
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

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
    public async Task IndexDocumentsAsync_InEveryShape_IndexesEveryLayersFiles(string name)
    {
        var setup = ProjectSetup.Named(name);
        var fs = setup.Disk();
        foreach (var layer in setup.Layers)
            fs.AddFile(setup.PathOf(layer.Rank, "Xml/Units.xml"), new MockFileData("<Root/>"));
        var svc = new FakeGameIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            new FakeParser());
        var configuration = setup.Configuration();

        indexer.PreScanMetafiles(configuration, [.. setup.Layers.Select(l => setup.FolderOf(l.Rank))]);
        await indexer.IndexDocumentsAsync(configuration, CancellationToken.None);

        foreach (var layer in setup.Layers)
            Assert.Single(svc.Updates, c => DocumentUris.Same(c.Uri, setup.Uri(layer.Rank, "Xml/Units.xml")));
    }

    [Fact]
    public async Task IndexDocumentsAsync_ADependencyOpenedAloneThenUnderALeaf_KeepsItsSnapshot()
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
            var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
                new FakeParser());
            indexer.PreScanMetafiles(config, roots);
            await indexer.IndexDocumentsAsync(config, CancellationToken.None);
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
    public async Task IndexDocumentsAsync_DependencyContentChanged_KeepsLayerSnapshot()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [depRoot, modRoot]);
        StampCrossLayer(modSnapshot, fs, registry, config, modXml);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

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
    public async Task IndexDocumentsAsync_DependencyRetypedThisLayersFiles_DiscardsLayerSnapshot()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [depRoot, modRoot]);
        // Stamp the snapshot as it was when the mod's units.xml carried NO registered type...
        StampCrossLayer(modSnapshot, fs, registry, config, modXml);
        // ...then let a dependency metafile register one for it, as a dependency's metafile can.
        registry.RegisterFile(fh.PathToFileUri(Path.Combine(modXml, "units.xml")), ["GameObjectType"]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        // Both re-parsed; nothing served from the now-stale mod snapshot.
        Assert.Equal(2, svc.Updates.Count);
        Assert.Empty(svc.Injected);
    }

    [Fact]
    public async Task IndexDocumentsAsync_SupersededDependencyHashKey_IsNoLongerConsulted()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [depRoot, modRoot]);
        StampCrossLayer(modSnapshot, fs, registry, config, modXml);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

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
    public async Task IndexDocumentsAsync_CacheHit_ReplaysTheParsersOutOfBandState()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache, parser);
        indexer.PreScanMetafiles(config, [root]);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Injected);
        var restored = Assert.Single(parser.Restored);
        Assert.Equal(state, restored.State);
        Assert.Contains("units.xml", restored.Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndexDocumentsAsync_SnapshotSave_CarriesTheParsersOutOfBandState()
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
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), cache,
            parser);
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.True(cache.Saved.TryGetValue(pgproj, out var saved));
        Assert.Equal([4, 2], Assert.Single(saved.Files).Document.ParserState);
    }

    [Fact]
    public async Task IndexDocumentsAsync_UnchangedSnapshot_SkipsSave()
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
        var (indexer, _) = Build(fs, svc, registry, new SchemaProviderBuilder().Build(), cache,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

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
    public async Task IndexDocumentsAsync_FlatScan_DoesNotAddFilesToWorkspaceHost()
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
        var (indexer, _) = BuildWithHost(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(), null,
            null, null, null, null, new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Equal(2, svc.Updates.Count);
    }

    [Fact]
    public async Task IndexDocumentsAsync_LayeredScan_CacheMiss_DoesNotAddFilesToWorkspaceHost()
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
        var (indexer, _) = BuildWithHost(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().Build(),
            null, null, null, null, null, new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Updates);
    }

    [Fact]
    public async Task IndexDocumentsAsync_LayeredScan_CacheHit_InjectsIntoIndexWithoutHost()
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
        var (indexer, _) = BuildWithHost(fs, svc, registry, new SchemaProviderBuilder().Build(),
            cache, null, null, null, null, new FakeParser());
        indexer.PreScanMetafiles(config, [root]);
        StampCrossLayer(snapshot, fs, registry, config, xmlDir);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Injected); // cache hit lands in the index; no text is pinned anywhere
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