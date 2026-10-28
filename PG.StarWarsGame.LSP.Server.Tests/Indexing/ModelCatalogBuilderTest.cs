// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Indexing;

/// <summary>
///     The model catalog on its own (#315): bones, textures, particle systems and proxies of every
///     workspace model, merged over the baseline's layer by layer, and each layer's snapshot.
/// </summary>
public sealed class ModelCatalogBuilderTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static ModelCatalogBuilder Build(MockFileSystem fs, IGameIndexService svc,
        IModelBoneCatalogCache? boneCache = null)
    {
        return new ModelCatalogBuilder(new FileHelper(fs), svc, boneCache ?? new NullModelBoneCatalogCache(),
            NullLogger<ModelCatalogBuilder>.Instance);
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

    [Fact]
    public void Apply_AppliesBaselineBones()
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
        var builder = Build(fs, svc);

        builder.Apply(WorkspaceConfiguration.Empty with { AssetRoots = [root] });

        Assert.Equal(["root", "muzzle_bone"], svc.Current.ModelBones["data/art/models/shipped.alo"].ToArray());
    }

    // Models and particle systems share the extension (#270): the scan that reads every .alo records
    // which are particle systems, merged over the baseline's - a mod's model replaces a shipped
    // particle system of the same name, as every other model catalog entry does.
    [Fact]
    public void Apply_PublishesTheParticleSystems_TheModsOverTheBaselines()
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
        var builder = Build(fs, svc);

        builder.Apply(WorkspaceConfiguration.Empty with
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
    public void Apply_NoLayers_StillAppliesFromTheFlattenedRoots()
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
        var builder = Build(fs, svc);

        builder.Apply(WorkspaceConfiguration.Empty with { AssetRoots = [root], Layers = [] });

        Assert.True(svc.Current.ModelBones.ContainsKey("shipped.alo"));
    }

    // A model's proxies name the particle systems it carries (#270): the scan that reads every .alo
    // records them, merged over the baseline's like the rest of the model catalog, so a particle
    // system can list the models that carry it.
    [Fact]
    public void Apply_PublishesTheModelsProxies_TheModsOverTheBaselines()
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
        var builder = Build(fs, svc, cache);

        builder.Apply(WorkspaceConfiguration.Empty with
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
    public void Apply_ReusesASnapshotPerLayer()
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
        var builder = Build(fs, svc, cache);

        builder.Apply(WorkspaceConfiguration.Empty with
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
        Assert.Equal(new BoneCatalogStats(1, 2), builder.LastBoneCatalog);
    }
}