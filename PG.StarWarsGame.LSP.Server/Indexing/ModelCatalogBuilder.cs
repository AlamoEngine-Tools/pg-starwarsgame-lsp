// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Indexing;

/// <summary>
///     The model catalog: the bones, textures, particle-system flag and proxies of every workspace
///     model, merged over the baseline's layer by layer and published on the game index.
/// </summary>
public sealed class ModelCatalogBuilder
{
    private readonly IModelBoneCatalogCache _boneCache;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<ModelCatalogBuilder> _logger;

    public ModelCatalogBuilder(IFileHelper fileHelper, IGameIndexService indexService, IModelBoneCatalogCache boneCache,
        ILogger<ModelCatalogBuilder> logger)
    {
        _fileHelper = fileHelper;
        _indexService = indexService;
        _boneCache = boneCache;
        _logger = logger;
    }

    /// <summary>How the last build used its snapshots. Null before the first build.</summary>
    public BoneCatalogStats? LastBoneCatalog { get; private set; }

    /// <summary>
    ///     Extracts bone names from loose workspace .alo models, unions them with the baseline bone
    ///     catalog (workspace models override shipped ones of the same filename), and publishes the
    ///     merged map on the GameIndex for boneName completion.
    /// </summary>
    public void Apply(WorkspaceConfiguration config)
    {
        var baselineBones = _indexService.Current.Baseline.ModelBones;
        var baselineTextures = _indexService.Current.Baseline.ModelTextures;

        var mergedBones = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bones) in baselineBones)
            mergedBones[path] = bones;

        var mergedTextures = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, textures) in baselineTextures)
            mergedTextures[path] = textures;

        // Which .alo files are particle systems (#270), the workspace's entry deciding for its name.
        var particleSystems = _indexService.Current.Baseline.ParticleSystems.ToBuilder();
        // Each model's proxy names (#270), the workspace's model replacing the shipped one's.
        var proxies = _indexService.Current.Baseline.ModelProxies.ToBuilder();

        var workspaceCount = 0;
        var reusedLayers = 0;
        var layerCount = 0;

        // Rank ASCENDING - dependencies first, root project last - so a leaf's own model of the
        // same filename overwrites the dependency's, matching every other layered lookup. The old
        // flat config.AssetRoots union was already deps-first; this preserves that order while
        // making each layer separately cacheable.
        foreach (var layer in LayersForBones(config))
        {
            var (models, reused) = ModelsForLayer(layer);
            layerCount++;
            if (reused) reusedLayers++;

            foreach (var (path, entry) in models)
            {
                mergedBones[path] = [.. entry.Bones];
                // An empty texture list is recorded on purpose: it is the answer "this model names
                // none", and it is what keeps the validator from reopening the file to find out.
                mergedTextures[path] = [.. entry.Textures];
                if (entry.ParticleSystem) particleSystems.Add(path);
                else particleSystems.Remove(path);
                proxies[path] = [.. entry.Proxies];
                workspaceCount++;
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Model catalog: {Workspace} workspace model(s) merged with {Baseline} baseline "
                + "model(s); {Reused} layer(s) reused a snapshot; {Textures} model(s) with texture data",
                workspaceCount, baselineBones.Count, reusedLayers, mergedTextures.Count);
        LastBoneCatalog = new BoneCatalogStats(reusedLayers, layerCount);

        _indexService.ApplyModelBones(mergedBones.ToImmutable());
        _indexService.ApplyModelTextures(mergedTextures.ToImmutable());
        _indexService.ApplyParticleSystems(particleSystems.ToImmutable());
        _indexService.ApplyModelProxies(proxies.ToImmutable());
    }

    /// <summary>
    ///     The layers to extract bones from, dependencies first. Falls back to one synthetic layer
    ///     over the flattened asset roots when the workspace resolved no projects (a heuristic
    ///     scan), which cannot be cached because there is no <c>.pgproj</c> to store it beside.
    /// </summary>
    private static ProjectLayer[] LayersForBones(WorkspaceConfiguration config)
    {
        return config.Layers.Count == 0
            ? [new ProjectLayer(0, "(workspace)", [], [], [], config.AssetRoots, null)]
            : [.. config.Layers.OrderBy(l => l.Rank)];
    }

    /// <summary>
    ///     One layer's models, from its persisted snapshot when the files on disk are unchanged,
    ///     otherwise by extracting and writing a fresh snapshot.
    /// </summary>
    private (IReadOnlyDictionary<string, ModelCatalogEntry> Models, bool Reused) ModelsForLayer(
        ProjectLayer layer)
    {
        var roots = layer.AssetRoots;
        if (roots.Count == 0)
            return (new Dictionary<string, ModelCatalogEntry>(), false);

        // No pgproj means no place to keep a snapshot beside - extract every time, as before.
        if (layer.ProjectPath is not { } pgprojPath)
            return (ExtractModels(roots), false);

        var fingerprint = ModelBoneFingerprint.Compute(_fileHelper, roots);
        if (_boneCache.TryLoad(pgprojPath, fingerprint) is { } cached)
            return (cached, true);

        var extracted = ExtractModels(roots);
        _boneCache.Save(pgprojPath, fingerprint, extracted);
        return (extracted, false);
    }

    /// <summary>
    ///     Bones and textures for every model under <paramref name="roots" />. A model that yields
    ///     no bones is still recorded when it yields textures, and vice versa - the two reads fail
    ///     independently, and a model whose skeleton will not load still has readable textures.
    /// </summary>
    private Dictionary<string, ModelCatalogEntry> ExtractModels(IReadOnlyList<string> roots)
    {
        var models = new Dictionary<string, ModelCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!_fileHelper.FileSystem.Directory.Exists(root)) continue;

            foreach (var (path, names) in BoneNameExtractor.Extract(_fileHelper.FileSystem, root))
                models[path] = (models.GetValueOrDefault(path) ?? ModelCatalogEntry.Empty) with { Bones = names };

            foreach (var (path, scan) in BoneNameExtractor.ExtractTextures(_fileHelper.FileSystem, root))
                models[path] = (models.GetValueOrDefault(path) ?? ModelCatalogEntry.Empty) with
                {
                    Textures = [.. scan.Textures],
                    ParticleSystem = scan.Kind == AloFileKind.Particle,
                    Proxies = [.. scan.Proxies]
                };
        }

        return models;
    }
}