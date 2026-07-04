// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceIndexerTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static (WorkspaceIndexer Indexer, EaWXmlContext Context) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        IProjectIndexCache? cache = null, ILuaAnnotationRepository? repo = null,
        params IGameDocumentParser[] parsers)
    {
        return BuildWithHost(fs, svc, registry, schema, cache, repo, null, parsers);
    }

    // Overload kept for callers that pass parsers without a cache
    private static (WorkspaceIndexer Indexer, EaWXmlContext Context) Build(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        params IGameDocumentParser[] parsers)
    {
        return BuildWithHost(fs, svc, registry, schema, null, null, null, parsers);
    }

    private static (WorkspaceIndexer Indexer, EaWXmlContext Context) BuildWithHost(
        MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry, ISchemaProvider schema,
        IProjectIndexCache? cache, ILuaAnnotationRepository? repo, IGameWorkspaceHost? workspaceHost,
        params IGameDocumentParser[] parsers)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        var indexer = new WorkspaceIndexer(fh, parsers, svc, registry, schema, ctx,
            cache ?? new NullProjectIndexCache(), repo ?? new LuaAnnotationRepository(),
            workspaceHost ?? new NullWorkspaceHost(),
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
    public void PreScanMetafiles_SubdirEntry_RegistersUnderEveryXmlRoot_PreservingSubdir()
    {
        // Real case: the mod (rev) ships GameObjectFiles.xml listing subdirectory paths that resolve
        // to object files in its dependency (core). The type must be registered at the subdir path
        // under BOTH xml roots — not stripped to a bare filename under the metafile's own directory.
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
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]));
        var (indexer, _) = Build(fs, new FakeIndexService(), registry, schema);

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
        var schema = new FakeSchemaProvider(
            new MetafileDefinition("data/xml/hardpointdatafiles.xml", MetafileType.FileRegistry, ["HardPoint"]));
        var (indexer, _) = Build(fs, new FakeIndexService(), registry, schema);

        indexer.PreScanMetafiles(new WorkspaceConfiguration([coreXml, revXml], [], [], [], null), [revRoot]);

        var fh = new FileHelper(fs);
        var coreKey = fh.PathToFileUri(Path.Combine(coreXml, "HardPoints", "HP_Weapons.xml"));
        Assert.Equal(["HardPoint"], registry.GetTypesForFile(coreKey).ToArray());
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

    // ── Dynamic enum catalog ─────────────────────────────────────────────────

    [Fact]
    public void ApplyDynamicEnumCatalog_ParsesEnumFile_AndPublishesValues()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var enumFilePath = Path.Combine(xmlDir, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] = new("<EnumDefinition><GENERIC_TRACK>0</GENERIC_TRACK><MY_MOD_TRACK>99</MY_MOD_TRACK></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new FakeSchemaWithEnums(enumDef);
        var svc = new FakeIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.NotNull(svc.AppliedWorkspaceDynamicEnumValues);
        Assert.True(svc.AppliedWorkspaceDynamicEnumValues!.TryGetValue("SurfaceFXTriggerType", out var vals));
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
        var schema = new FakeSchemaWithEnums(enumDef);
        var svc = new FakeIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.NotNull(svc.AppliedWorkspaceDynamicEnumValues);
        Assert.Empty(svc.AppliedWorkspaceDynamicEnumValues!);
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
        var schema = new FakeSchemaWithEnums(enumDef);
        var svc = new FakeIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.NotNull(svc.AppliedWorkspaceDynamicEnumValues);
        Assert.True(svc.AppliedWorkspaceDynamicEnumValues!.TryGetValue("SurfaceFXTriggerType", out var vals));
        Assert.Contains("CUSTOM", vals);
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
        var schema = new FakeSchemaWithEnums(enumDef);
        var svc = new FakeIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]); // pass parent dir, not the enum subdir

        Assert.NotNull(svc.AppliedWorkspaceDynamicEnumValues);
        Assert.True(svc.AppliedWorkspaceDynamicEnumValues!.TryGetValue("SurfaceFXTriggerType", out var vals));
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
        var schema = new FakeSchemaWithEnums(enumDef);
        var svc = new FakeIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.NotNull(svc.AppliedWorkspaceEnumValueDefinitions);
        Assert.True(svc.AppliedWorkspaceEnumValueDefinitions!.TryGetValue("DamageType", out var valueMap));
        Assert.True(valueMap!.TryGetValue("EXPLOSIVE", out var explosiveOrigin));
        Assert.True(explosiveOrigin!.IsNavigable);
        Assert.True(valueMap.TryGetValue("ENERGY", out var energyOrigin));
        // ENERGY comes after EXPLOSIVE in the same line, so its column must be greater
        Assert.True(energyOrigin!.Column > explosiveOrigin.Column);
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
        var schema = new FakeSchemaWithEnums(enumDef);
        var svc = new FakeIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        indexer.ApplyDynamicEnumCatalog([xmlDir]);

        Assert.NotNull(svc.AppliedWorkspaceEnumValueDefinitions);
        Assert.True(svc.AppliedWorkspaceEnumValueDefinitions!
            .TryGetValue("SurfaceFXTriggerType", out var valueMap));
        Assert.True(valueMap!.TryGetValue("GENERIC_TRACK", out var origin));
        Assert.True(origin!.IsNavigable);
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
        var schema = new FakeSchemaWithEnums(enumDef);
        var svc = new FakeIndexService();
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), schema);

        // Pass roots in deps-first / root-last order (same as ModProjectResolver produces).
        indexer.ApplyDynamicEnumCatalog([depXml, rootXml]);

        Assert.NotNull(svc.AppliedWorkspaceDynamicEnumValues);
        var vals = svc.AppliedWorkspaceDynamicEnumValues!["SurfaceFXTriggerType"];
        Assert.Contains("ROOT_TRACK", vals);   // root project's value is present
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
        var svc = new FakeIndexService();
        var cache = new FakeProjectIndexCache(); // no snapshot stored
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), cache, null,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Calls); // parsed normally
        Assert.Empty(svc.InjectedDocuments);
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
        var svc = new FakeIndexService();

        // Build a snapshot whose hash matches the file content
        var fh = new FileHelper(fs);
        var relPath = "data/xml/units.xml";
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
            Files = [entry]
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), cache, null,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Empty(svc.Calls); // no re-parse
        Assert.Single(svc.InjectedDocuments); // injected from cache
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
        var svc = new FakeIndexService();

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
            Files = [entry]
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), cache, null,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(svc.Calls); // re-parsed
        Assert.Empty(svc.InjectedDocuments);
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
        var svc = new FakeIndexService();
        var cache = new FakeProjectIndexCache();
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), cache, null,
            new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.True(cache.SavedPaths.Contains(pgproj));
        var saved = cache.Saved[pgproj];
        Assert.Equal(ProjectIndexSnapshot.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.Single(saved.Files);
    }

    private static WorkspaceConfiguration ConfigWithLayer(string xmlDir, string pgprojPath)
    {
        var layer = new ProjectLayer(0, "TestMod", [xmlDir], [], [], [], null, pgprojPath);
        return new WorkspaceConfiguration([xmlDir], [], [], [], null) { Layers = [layer] };
    }

    // ── annotation repository ─────────────────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_Flat_RebuildIndexCalledAfterIndexing()
    {
        var root = Root("ws");
        var scriptDir = Path.Combine(root, "data", "scripts");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(scriptDir, "mission.lua")] = new("function RunMission() end")
        });
        var svc = new FakeIndexService();
        var spy = new SpyAnnotationRepository();
        var config = new WorkspaceConfiguration([], [scriptDir], [], [], null);
        var (indexer, _) = Build(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(),
            null, spy, new FakeParser());

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Equal(1, spy.RebuildCallCount);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeProjectIndexCache : IProjectIndexCache
    {
        private readonly Dictionary<string, ProjectIndexSnapshot> _snapshots = [];
        public readonly HashSet<string> HygienePaths = [];
        public readonly Dictionary<string, ProjectIndexSnapshot> Saved = [];
        public readonly HashSet<string> SavedPaths = [];

        public ProjectIndexSnapshot? this[string pgprojPath]
        {
            set => _snapshots[pgprojPath] = value!;
        }

        public ProjectIndexSnapshot? TryLoad(string pgprojPath)
        {
            return _snapshots.GetValueOrDefault(pgprojPath);
        }

        public void Save(string pgprojPath, ProjectIndexSnapshot snapshot)
        {
            SavedPaths.Add(pgprojPath);
            Saved[pgprojPath] = snapshot;
        }

        public void EnsureGitHygiene(string pgprojPath)
        {
            HygienePaths.Add(pgprojPath);
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

    private sealed class FakeSchemaWithEnums(params EnumDefinition[] enums) : ISchemaProvider
    {
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => enums;
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public event EventHandler? SchemaRefreshed { add { } remove { } }

        public XmlTagDefinition? GetTag(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public GameObjectTypeDefinition? GetObjectType(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
        public EnumDefinition? GetEnum(string name) =>
            enums.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public readonly List<(string Uri, int Version)> Calls = [];

        public readonly List<DocumentIndex> InjectedDocuments = [];
        public int BeginBulkUpdateCallCount;

        public FakeIndexService(GameIndex? current = null)
        {
            Current = current ?? GameIndex.Empty;
        }

        public IAssetFileIndex? AppliedAssetFiles { get; private set; }

        public ImmutableDictionary<string, ImmutableArray<string>>? AppliedModelBones { get; private set; }

        public ImmutableDictionary<string, ImmutableArray<string>>? AppliedWorkspaceDynamicEnumValues
        {
            get; private set;
        }

        public ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>>? AppliedWorkspaceEnumValueDefinitions
        {
            get; private set;
        }

        public GameIndex Current { get; private set; }

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

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
            ct.ThrowIfCancellationRequested();
            lock (Calls)
            {
                Calls.Add((uri, version));
                var doc = new DocumentIndex(uri, version,
                    ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
                Current = Current with { Documents = Current.Documents.SetItem(uri, doc) };
            }

            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
            lock (InjectedDocuments)
            {
                InjectedDocuments.Add(document);
                Current = Current with { Documents = Current.Documents.SetItem(document.DocumentUri, document) };
            }
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

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
            AppliedWorkspaceDynamicEnumValues = values;
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
            AppliedWorkspaceEnumValueDefinitions = definitions;
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

    // ── WorkspaceHost population ──────────────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_FlatScan_AddsFilesToWorkspaceHost_WithPublishDiagnosticsFalse()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "a.xml")] = new("<Root/>"),
            [Path.Combine(xmlDir, "b.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();
        var host = new SpyWorkspaceHost();
        var config = new WorkspaceConfiguration([xmlDir], [], [], [], null);
        var (indexer, _) = BuildWithHost(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(), null, null,
            host, new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Equal(2, host.Calls.Count);
        Assert.All(host.Calls, c => Assert.False(c.PublishDiagnostics));
        Assert.All(host.Calls, c => Assert.Equal(0, c.Version));
    }

    [Fact]
    public async Task IndexDocumentsAsync_LayeredScan_CacheMiss_AddsFilesToWorkspaceHost_WithPublishDiagnosticsFalse()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new("<Root/>")
        });
        var svc = new FakeIndexService();
        var host = new SpyWorkspaceHost();
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, _) = BuildWithHost(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(),
            null, null, host, new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(host.Calls);
        Assert.False(host.Calls[0].PublishDiagnostics);
        Assert.Equal(0, host.Calls[0].Version);
    }

    [Fact]
    public async Task IndexDocumentsAsync_LayeredScan_CacheHit_AddsFilesToWorkspaceHost_WithPublishDiagnosticsFalse()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var pgproj = Path.Combine(root, "mod.pgproj").Replace('\\', '/').ToLowerInvariant();
        var fileContent = "<Root/>"u8.ToArray();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "units.xml")] = new(fileContent)
        });
        var svc = new FakeIndexService();
        var host = new SpyWorkspaceHost();

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
            OverallHash = "anything", DependencyHashes = [], Files = [entry]
        };
        var cache = new FakeProjectIndexCache { [pgproj] = snapshot };
        var config = ConfigWithLayer(xmlDir, pgproj);
        var (indexer, _) = BuildWithHost(fs, svc, new FileTypeRegistry(), new FakeSchemaProvider(),
            cache, null, host, new FakeParser());
        indexer.PreScanMetafiles(config, [root]);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);

        Assert.Single(host.Calls); // even cache-hit adds to host
        Assert.False(host.Calls[0].PublishDiagnostics);
        Assert.Equal(0, host.Calls[0].Version);
        Assert.Equal("<Root/>", host.Calls[0].Text);
    }

    private sealed class SpyAnnotationRepository : ILuaAnnotationRepository
    {
        public int RebuildCallCount { get; private set; }

        public void Update(string uri, ImmutableArray<EmmyLuaAnnotations> annotations) { }
        public void Remove(string uri) { }
        public IReadOnlyDictionary<string, ImmutableArray<EmmyLuaAnnotations>> All =>
            new Dictionary<string, ImmutableArray<EmmyLuaAnnotations>>();
        public ILuaTypeIndex Current => LuaTypeIndex.Empty;
        public void RebuildIndex() => RebuildCallCount++;
        public void UpdateFunctionAnnotations(string uri, IReadOnlyList<(string Name, EmmyLuaAnnotations Ann)> functions) { }
        public EmmyLuaAnnotations? GetFunctionAnnotation(string name) => null;
    }

    private sealed class NullWorkspaceHost : IGameWorkspaceHost
    {
        public IEnumerable<TrackedDocument> All => [];
        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true) { }
        public void Remove(string uri) { }
        public bool TryGet(string uri, out TrackedDocument doc) { doc = null!; return false; }
    }

    private sealed class SpyWorkspaceHost : IGameWorkspaceHost
    {
        public List<(string Uri, string Text, int Version, bool PublishDiagnostics)> Calls { get; } = [];
        public IEnumerable<TrackedDocument> All => [];
        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            lock (Calls) Calls.Add((uri, text, version, publishDiagnostics));
        }
        public void Remove(string uri) { }
        public bool TryGet(string uri, out TrackedDocument doc) { doc = null!; return false; }
    }
}
