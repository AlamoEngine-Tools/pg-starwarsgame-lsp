// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Diagnostics;
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

/// <summary>
///     The sequencer over the indexing stages (#315). Each stage is tested on its own under
///     <c>Indexing/</c>; what is left here is what the seam adds - the baseline it hands the metafile
///     stage, and the statistics it reports from the stages that keep them.
/// </summary>
public sealed class WorkspaceIndexerTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static WorkspaceIndexer Build(MockFileSystem fs, IGameIndexService svc, IFileTypeRegistry registry,
        ISchemaProvider schema)
    {
        var fh = new FileHelper(fs);
        var ctx = new EaWXmlContext(fh);
        return new WorkspaceIndexer(svc,
            new MetafileRegistrar(fh, registry, schema, ctx, new StoryChainProblemStore(),
                new RegistryWalkStore(), NullLogger<MetafileRegistrar>.Instance),
            new DocumentIndexer(fh, svc, new NullProjectIndexCache(), new ParseableFiles(fh, [], ctx),
                new IndexFingerprints(schema, new LuaApiSchemaProvider([]), registry, fh),
                NullLogger<DocumentIndexer>.Instance),
            new AssetCatalogBuilder(fh, svc, new FakeLspConfigurationProvider(), NoMegEntries.Instance,
                new MegArchiveLoadOrder(fh), NullLogger<AssetCatalogBuilder>.Instance),
            new ModelCatalogBuilder(fh, svc, new NullModelBoneCatalogCache(), NullLogger<ModelCatalogBuilder>.Instance),
            new DynamicEnumCatalogBuilder(fh, svc, schema, NullLogger<DynamicEnumCatalogBuilder>.Instance));
    }

    /// <summary>
    ///     The metafile stage falls back to the baseline loaded when it runs, not one captured when
    ///     the indexer was built: a reload after a baseline change must type files by the new one.
    /// </summary>
    [Fact]
    public void PreScanMetafiles_FallsBackToTheBaselineLoadedNow()
    {
        var xmlDir = Path.Combine(Root("ws"), "data", "xml");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var svc = new FakeGameIndexService();
        var registry = new FileTypeRegistry();
        var indexer = Build(fs, svc, registry, new SchemaProviderBuilder().WithMetafiles(
                new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"]))
            .Build());
        svc.ApplyBaseline(BaselineIndex.Empty with
        {
            FileTypeMap = ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add("units.xml", ["GameObjectType"])
        });

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [Root("ws")]);

        Assert.Equal(["GameObjectType"],
            registry.GetTypesForFile(new FileHelper(fs).PathToFileUri(Path.Combine(xmlDir, "units.xml"))).ToArray());
    }

    /// <summary>
    ///     The metafile stage publishes what the game loads: a shipped object in a file the project's
    ///     registry leaves out stops resolving once the stage has run.
    /// </summary>
    [Fact]
    public void PreScanMetafiles_PublishesTheLoadOrder_SoAnUnlistedShippedObjectStopsResolving()
    {
        var xmlDir = Path.Combine(Root("ws"), "data", "xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(xmlDir, "gameobjectfiles.xml")] =
                new("<Game_Object_Files><File>Units.xml</File></Game_Object_Files>")
        });
        var svc = new FakeGameIndexService();
        var indexer = Build(fs, svc, new FileTypeRegistry(), new SchemaProviderBuilder().WithMetafiles(
            new MetafileDefinition("data/xml/gameobjectfiles.xml", MetafileType.FileRegistry, ["GameObjectType"])
            {
                DuplicateNames = DuplicateNameRule.FIRST_WINS
            }).Build());
        svc.ApplyBaseline(BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary<string, GameSymbol>.Empty
                .Add("Executor", new GameSymbol("Executor", GameSymbolKind.XmlObject, "GameObjectType",
                    new FileOrigin(@"DATA\XML\Units_Underworld.xml", 1, null), null))
        });

        indexer.PreScanMetafiles(new WorkspaceConfiguration([xmlDir], [], [], [], null), [Root("ws")]);

        Assert.Null(svc.Current.Resolve("Executor"));
    }

    [Fact]
    public async Task TheStatistics_AreTheStagesOwn_AndNullUntilTheyRun()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(Path.Combine(root, "art"));
        var indexer = Build(fs, new FakeGameIndexService(), new FileTypeRegistry(),
            new SchemaProviderBuilder().Build());
        var config = new WorkspaceConfiguration([], [], [], [Path.Combine(root, "art")], null)
        {
            Layers =
            [
                new ProjectLayer(0, "mod", [], [], [], [Path.Combine(root, "art")], null,
                    Path.Combine(root, "mod.pgproj"))
            ]
        };
        Assert.Null(indexer.LastIndexCache);
        Assert.Null(indexer.LastBoneCatalog);

        await indexer.IndexDocumentsAsync(config, CancellationToken.None);
        indexer.ApplyModelBoneCatalog(config);

        Assert.Equal(new IndexCacheStats(0, 1, 0, 0), indexer.LastIndexCache);
        Assert.Equal(new BoneCatalogStats(0, 1), indexer.LastBoneCatalog);
    }
}