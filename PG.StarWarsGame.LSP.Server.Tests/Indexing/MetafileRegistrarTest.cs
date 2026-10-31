// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Core.Xml;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Indexing;

/// <summary>
///     The metafile stage on its own (#315): which files the registries, the shipped baseline and the
///     campaign registry's story chain type, and which copy of a registry is read.
/// </summary>
public sealed class MetafileRegistrarTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static (MetafileRegistrar Registrar, EaWXmlContext Context) Build(
        MockFileSystem fs, IFileTypeRegistry registry, ISchemaProvider schema,
        IStoryChainProblemStore? storyProblems = null, IRegistryWalkStore? walks = null)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        return (new MetafileRegistrar(fh, registry, schema, ctx, storyProblems ?? new StoryChainProblemStore(),
            walks ?? new RegistryWalkStore(), NullLogger<MetafileRegistrar>.Instance), ctx);
    }

    private static ISchemaProvider ObjectRegistrySchema()
    {
        return new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
    }

    // ── The winning copy of a listed file ────────────────────────────────────

    /// <summary>
    ///     The game opens a listed file from the first place it finds it, the mod before its
    ///     dependency, and never reads the second copy. Typing both made the dependency's copy
    ///     contribute definitions the game never loads.
    /// </summary>
    [Fact]
    public void Register_ListedFileShippedByModAndDependency_TypesOnlyTheModCopy()
    {
        var revXml = Path.Combine(Root("rev"), "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(revXml, "gameobjectfiles.xml")] =
                new("<Game_Object_Files><File>Units.xml</File></Game_Object_Files>"),
            [Path.Combine(revXml, "Units.xml")] = new("<Units/>"),
            [Path.Combine(coreXml, "Units.xml")] = new("<Units/>")
        });
        var registry = new FileTypeRegistry();
        var (registrar, _) = Build(fs, registry, ObjectRegistrySchema());

        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [Root("rev")],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        Assert.Equal(["GameObjectType"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Units.xml"))).ToArray());
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Units.xml"))));
    }

    /// <summary>
    ///     A listed file no layer ships yet is typed where a new copy would win, the leaf project, so
    ///     creating it there types it; never under a dependency, whose copy would lose to it.
    /// </summary>
    [Fact]
    public void Register_ListedFileNoLayerShips_TypesOnlyTheLeafPath()
    {
        var revXml = Path.Combine(Root("rev"), "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(revXml, "gameobjectfiles.xml")] =
                new("<Game_Object_Files><File>Units.xml</File></Game_Object_Files>")
        });
        fs.AddDirectory(coreXml);
        var registry = new FileTypeRegistry();
        var (registrar, _) = Build(fs, registry, ObjectRegistrySchema());

        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [Root("rev")],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        Assert.Equal(["GameObjectType"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Units.xml"))).ToArray());
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Units.xml"))));
    }

    /// <summary>
    ///     The walk is the registry as the game loads it: every entry in list order, a repeat kept,
    ///     each with the layer copy that wins or none when the file is shipped or missing.
    /// </summary>
    [Fact]
    public void Register_RecordsTheWalk_InListOrder_WithEachEntrysWinningCopy()
    {
        var revXml = Path.Combine(Root("rev"), "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var registryPath = Path.Combine(revXml, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [registryPath] = new(@"<Game_Object_Files><File>Zeta.xml</File><File>Units\Alpha.xml</File>" +
                                 "<File>Shipped.xml</File><File>ZETA.XML</File></Game_Object_Files>"),
            [Path.Combine(revXml, "Zeta.xml")] = new("<Units/>"),
            [Path.Combine(coreXml, "Units", "Alpha.xml")] = new("<Units/>")
        });
        var walks = new RegistryWalkStore();
        var (registrar, _) = Build(fs, new FileTypeRegistry(), ObjectRegistrySchema(), walks: walks);

        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [Root("rev")],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        var walk = Assert.Single(walks.Current);
        Assert.Equal("data/xml/gameobjectfiles.xml", walk.RegistryPath);
        Assert.Equal(fh.PathToFileUri(registryPath), walk.RegistryUri);
        Assert.Equal(["zeta.xml", "units/alpha.xml", "shipped.xml", "zeta.xml"], walk.Entries.Select(e => e.Key));
        Assert.Equal(
        [
            fh.PathToFileUri(Path.Combine(revXml, "Zeta.xml")),
            fh.PathToFileUri(Path.Combine(coreXml, "Units", "Alpha.xml")),
            null,
            fh.PathToFileUri(Path.Combine(revXml, "Zeta.xml"))
        ], walk.Entries.Select(e => e.LayerUri));
    }

    /// <summary>
    ///     With no layer shipping the registry, the game reads the shipped one: the walk follows the
    ///     baseline's list in order, and a mod's copy of a shipped file still wins over the shipped copy.
    /// </summary>
    [Fact]
    public void Register_ShippedRegistry_WalksTheBaselineList_AndAModCopyShadowsTheShippedFile()
    {
        var xmlDir = Path.Combine(Root("ws"), "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "alpha.xml")] = new("<Units/>")
        });
        var baseline = BaselineIndex.Empty with
        {
            FileTypeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("alpha.xml", ["GameObjectType"]).Add("zeta.xml", ["GameObjectType"]),
            RegistryLists = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("data/xml/gameobjectfiles.xml", ["zeta.xml", "alpha.xml"])
        };
        var registry = new FileTypeRegistry();
        var walks = new RegistryWalkStore();
        var (registrar, _) = Build(fs, registry, ObjectRegistrySchema(), walks: walks);

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [Root("ws")], baseline);

        var fh = new FileHelper(fs);
        var alpha = fh.PathToFileUri(Path.Combine(xmlDir, "alpha.xml"));
        var walk = Assert.Single(walks.Current);
        Assert.Null(walk.RegistryUri);
        Assert.Equal(["zeta.xml", "alpha.xml"], walk.Entries.Select(e => e.Key));
        Assert.Equal([null, alpha], walk.Entries.Select(e => e.LayerUri));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(alpha).ToArray());
    }

    /// <summary>
    ///     A reload replaces the registration wholesale. Adding on top kept a copy typed after a
    ///     later reload had shadowed it or the registry had dropped it.
    /// </summary>
    [Fact]
    public void Register_AgainAfterTheRegistryDropsAFile_UntypesIt()
    {
        var xmlDir = Path.Combine(Root("ws"), "data", "xml");
        var registryPath = Path.Combine(xmlDir, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [registryPath] = new("<Game_Object_Files><File>Old.xml</File></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var (registrar, _) = Build(fs, registry, ObjectRegistrySchema());
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        registrar.Register(config, [Root("ws")], BaselineIndex.Empty);

        fs.File.WriteAllText(registryPath, "<Game_Object_Files><File>New.xml</File></Game_Object_Files>");
        registrar.Register(config, [Root("ws")], BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "Old.xml"))));
        Assert.Equal(["GameObjectType"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "New.xml"))).ToArray());
    }


    /// <summary>
    ///     The registry file is read by the game too - by a name compiled into it - so it is
    ///     registered under its own URI, with no types because the schema does not model its
    ///     contents. Only the files it listed used to be registered, and the registry itself was
    ///     reported as read by nothing.
    /// </summary>
    [Fact]
    public void Register_RegistersTheRegistryFileItself_WithoutTypes()
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
        var (registrar, _) = Build(fs, registry, schema);

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], BaselineIndex.Empty);

        var ownKey = new FileHelper(fs).PathToFileUri(metafilePath);
        Assert.True(((IFileTypeRegistry)registry).IsRegistered(ownKey));
        Assert.Empty(registry.GetTypesForFile(ownKey));
    }

    [Fact]
    public void Register_RegistersFileTypes_FromMetafileOnDisk()
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
        var (registrar, _) = Build(fs, registry, schema);

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], BaselineIndex.Empty);

        var expectedKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    /// <summary>
    ///     A registry may name a file in the attribute form, <c>&lt;File filename="..."/&gt;</c>; the
    ///     baseline builder read it, the indexer did not, and a file typed in one place and not the
    ///     other was validated with no rules at all. One reader for both now (#308).
    /// </summary>
    [Fact]
    public void Register_TakesTheFilenameAttributeForm()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var metafilePath = Path.Combine(xmlDir, "gameobjectfiles.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new(@"<Game_Object_Files><File filename=""DATA\XML\HARDPOINTS.XML""/></Game_Object_Files>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
        var (registrar, _) = Build(fs, registry, schema);

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], BaselineIndex.Empty);

        var expectedKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "hardpoints.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(expectedKey).ToArray());
    }

    [Fact]
    public void Register_SubdirEntry_TypesTheDependencyCopy_PreservingSubdir()
    {
        // Real case: the mod (rev) ships GameObjectFiles.xml listing subdirectory paths that resolve
        // to object files in its dependency (core). The type is registered at the subdir path of the
        // copy the game opens - not stripped to a bare filename under the metafile's own directory.
        var revRoot = Root("rev");
        var revXml = Path.Combine(revRoot, "data", "xml");
        var coreXml = Path.Combine(Root("core"), "data", "xml");
        var metafilePath = Path.Combine(revXml, "gameobjectfiles.xml");

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [metafilePath] = new(
                "<Game_Object_Files><File>Units\\Death_Clones.xml</File></Game_Object_Files>"),
            [Path.Combine(coreXml, "Units", "Death_Clones.xml")] = new("<Units/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build();
        var (registrar, _) = Build(fs, registry, schema);

        // config.XmlDirectories spans both projects (dependency first, root last).
        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        var coreKey = fh.PathToFileUri(Path.Combine(coreXml, "Units", "Death_Clones.xml"));
        var revKey = fh.PathToFileUri(Path.Combine(revXml, "Units", "Death_Clones.xml"));
        Assert.Equal(["GameObjectType"], registry.GetTypesForFile(coreKey).ToArray());
        Assert.Empty(registry.GetTypesForFile(revKey));
    }

    [Fact]
    public void Register_MetafileShippedByDependency_IsFoundAndRegistersTypes()
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
                "<HardPointDataFiles><File>HardPoints\\HP_Weapons.xml</File></HardPointDataFiles>"),
            [Path.Combine(coreXml, "HardPoints", "HP_Weapons.xml")] = new("<HardPoints/>")
        });
        var registry = new FileTypeRegistry();
        var schema = new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/hardpointdatafiles.xml", MetafileType.FileRegistry, ["HardPoint"]))
            .Build();
        var (registrar, _) = Build(fs, registry, schema);

        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        var coreKey = fh.PathToFileUri(Path.Combine(coreXml, "HardPoints", "HP_Weapons.xml"));
        Assert.Equal(["HardPoint"], registry.GetTypesForFile(coreKey).ToArray());
    }

    [Fact]
    public void Register_MetafileShippedByBothLayers_OnlyTopmostCopyIsRead()
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
        var (registrar, _) = Build(fs, registry, schema);

        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        Assert.Equal(["GameObjectType"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Rev_Only.xml"))).ToArray());
        // The shadowed registry's entry must not be typed under ANY root.
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Core_Only.xml"))));
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Core_Only.xml"))));
    }

    [Fact]
    public void Register_SeedsXmlDirectories_IntoContext()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var (registrar, ctx) = Build(fs, new FileTypeRegistry(),
            new SchemaProviderBuilder().Build());

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], BaselineIndex.Empty);

        var xmlFileUri = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "units.xml"));
        Assert.True(ctx.IsEaWXmlFile(xmlFileUri));
    }

    [Fact]
    public void Register_FallsBackToBaseline_UsingXmlDirs_WhenMetafileAbsent()
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
        var (registrar, _) = Build(fs, registry, schema);

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], svc.Current.Baseline);

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
    public void Register_SpecialDef_RegistersTheRegistryFileItself_WithoutTypes()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(StoryChainFixture(xmlDir));
        var registry = new FileTypeRegistry();
        var (registrar, _) = Build(fs, registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], BaselineIndex.Empty);

        var ownKey = new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "campaignfiles.xml"));
        Assert.True(((IFileTypeRegistry)registry).IsRegistered(ownKey));
        Assert.Empty(registry.GetTypesForFile(ownKey));
    }

    [Fact]
    public void Register_SpecialDef_TypesManifestsAndThreadsAlongTheChain()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(StoryChainFixture(xmlDir));
        var registry = new FileTypeRegistry();
        var (registrar, _) = Build(fs, registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "Story_Plots_Rebel.xml"))).ToArray());
        Assert.Equal(["StoryParser"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "Story_Rebel_Act_I.xml"))).ToArray());
    }

    [Fact]
    public void Register_SpecialDef_UnresolvedStoryName_LandsInProblemStore()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var files = StoryChainFixture(xmlDir);
        files.Remove(Path.Combine(xmlDir, "Story_Plots_Rebel.xml"));
        var fs = new MockFileSystem(files);
        var registry = new FileTypeRegistry();
        var store = new StoryChainProblemStore();
        var (registrar, _) = Build(fs, registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build(), store);

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        var campaignUri = fh.NormalizeUri(Path.Combine(xmlDir, "Campaigns_Test.xml"));
        var problem = Assert.Single(store.GetForDocument(campaignUri));
        Assert.Equal(StoryChainProblemKind.UnresolvedStoryName, problem.Kind);
        Assert.Equal("Story_Plots_Rebel.xml", problem.Reference);
    }

    [Fact]
    public void Register_SpecialDef_ChainSpansXmlRoots_AndTypesTheCopyTheGameOpens()
    {
        // The campaign lives in the mod (rev); the manifest and thread ship with the dependency
        // (core). Resolution searches every xml root, and only the copy the game opens is typed,
        // as with file-registry entries.
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
        var (registrar, _) = Build(fs, registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [Root("rev")],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Story_Plots_Core.xml"))).ToArray());
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Story_Plots_Core.xml"))));
        Assert.Equal(["StoryParser"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Story_Core_Act.xml"))).ToArray());
    }

    [Fact]
    public void Register_SpecialDef_RegistryShippedByBothLayers_OnlyTopmostCopyIsRead()
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
        var (registrar, _) = Build(fs, registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        registrar.Register(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot],
            BaselineIndex.Empty);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(revXml, "Story_Plots_Mod.xml"))).ToArray());
        Assert.Empty(registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(coreXml, "Story_Plots_Core.xml"))));
    }

    [Fact]
    public void Register_SpecialDef_RegistryAbsentEverywhere_FallsBackToBaselineFileTypeMap()
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
        var (registrar, _) = Build(fs, registry,
            new SchemaProviderBuilder().WithMetafiles(StorySpecialDef()).Build());

        registrar.Register(new WorkspaceConfiguration([xmlDir], [], [], [], null), [root], svc.Current.Baseline);

        var fh = new FileHelper(fs);
        Assert.Equal(["StoryPlotManifest"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "story_plots_vanilla.xml"))).ToArray());
        Assert.Equal(["StoryParser"],
            registry.GetTypesForFile(fh.PathToFileUri(Path.Combine(xmlDir, "story_vanilla_act.xml"))).ToArray());
    }
}