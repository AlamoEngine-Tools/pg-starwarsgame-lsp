// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview.Sections;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Icons;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Resolves ONE projectile by id, for the attacker panel's "fill from a projectile".
/// </summary>
/// <remarks>
///     <para>
///         The panel offers every projectile in the tree - 105 of them in the shipped data - because
///         the reader is building a weapon to fire AT the subject, so the subject's own armament is
///         the wrong list. But only what the subject fires travels with the scene, so picking any of
///         the rest filled nothing and said "not loaded yet". That is the reported fault, and it also
///         made the catalogue look incomplete: measured, it holds all 105.
///     </para>
///     <para>
///         Resolution is <see cref="ProjectileSectionBuilder.ProjectilesFor" />, the same reader the scene
///         uses, so a projectile filled on demand and one that arrived with the scene cannot differ.
///     </para>
/// </remarks>
public sealed class GetProjectileHandler(
    IMessageCatalog messages,
    IGameIndexService indexService,
    ISchemaProvider schema,
    IVariantTagSource tagSource)
    : IJsonRpcRequestHandler<GetProjectileParams, GetProjectileResult>
{
    public Task<GetProjectileResult> Handle(
        GetProjectileParams request, CancellationToken cancellationToken)
    {
        var id = request.Name.Trim();
        if (id.Length == 0)
            return Task.FromResult(new GetProjectileResult(null,
                messages.Format("PreviewHandlers_NoProjectileWasNamed")));

        // The problems the builder would have raised are collected and dropped: this is a request
        // for ONE projectile and the caller wants an answer about it, not a scene's problem list.
        var problems = new List<PreviewProblem>();
        var resolver = new EffectiveObjectResolver(indexService.Current, schema, tagSource);
        var found = ProjectileSectionBuilder.ProjectilesFor(resolver, [id], problems, messages)
            .FirstOrDefault();

        return Task.FromResult(found is null
            ? new GetProjectileResult(null,
                messages.Format("PreviewHandlers_ProjectileNotDefinedThisProject", id))
            : new GetProjectileResult(found));
    }
}

/// <summary>Lists the models the "Preview Model" picker offers.</summary>
/// <remarks>
///     Read off the merged asset catalog - the project's layers and the baseline together - so a
///     model that sits in a dependency or inside a shipped MEG is offered as readily as one in the
///     project's own folder. Particle systems share the <c>.alo</c> extension and the folder; the
///     catalog scan that already reads every file records which they are, and those are left out
///     (#270). One no scan has seen is offered.
/// </remarks>
public sealed class ListModelsHandler(IGameIndexService indexService)
    : IJsonRpcRequestHandler<ListModelsParams, ListModelsResult>
{
    private const string ModelFolder = "data/art/models/";

    public Task<ListModelsResult> Handle(ListModelsParams request, CancellationToken cancellationToken)
    {
        var assets = indexService.Current.AssetFiles;
        var particleSystems = indexService.Current.ParticleSystems;
        var models = assets.GetByExtension(".alo")
            .Where(p => p.StartsWith(ModelFolder, StringComparison.OrdinalIgnoreCase))
            .Where(p => !particleSystems.Contains(ModelBoneKey.From(p)))
            .Select(p => new PreviewModelEntry(MergedAssetFileIndex.FileNameOf(p), assets.IsPackedAsset(p)))
            // A name in two subfolders is one entry: the scene resolves by name, so a second row
            // would open the same model. The project's copy is the one kept, as it is the one the
            // scene will resolve to.
            .OrderBy(m => m.BaseGame)
            .DistinctBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(new ListModelsResult(models));
    }
}

/// <summary>Serves the assembled scene description.</summary>
public sealed class GetPreviewSceneHandler(
    PreviewSceneBuilder builder,
    IGameAssetResolver assets,
    IMessageCatalog messages,
    IWorkspaceIconCatalog icons,
    IPreviewCacheBuilder previewCache)
    : IJsonRpcRequestHandler<GetPreviewSceneParams, GetPreviewSceneResult>
{
    public async Task<GetPreviewSceneResult> Handle(
        GetPreviewSceneParams request, CancellationToken cancellationToken)
    {
        var result = await BuildAsync(request, cancellationToken);

        // The reader has just opened this subject, or changed it. While the background build of
        // the preview cache is still under way, everything the subject loads goes to the front of
        // its queue - the preview is never held back for the build, the build is reordered for the
        // preview.
        previewCache.Prioritise(ModelsOf(result.Scene));
        return result;
    }

    /// <summary>
    ///     Every model the client will ask for on this scene's behalf, each with the clips it will
    ///     offer: a part with the scene's, a death clone or a piece of wreckage with its own when it
    ///     has any. That is the rule the client's request follows, so an entry converted ahead is
    ///     the entry the request finds. Then the model of each other map environment the unit maps,
    ///     with the clips the scene would offer on it (#294): last, since the reader may never pick
    ///     one, and only one not listed already.
    /// </summary>
    private static List<PreviewBuildItem> ModelsOf(PreviewScene scene)
    {
        var items = new List<PreviewBuildItem>();

        foreach (var part in scene.Parts)
            if (part.Resolved)
                items.Add(new PreviewBuildItem(part.ModelRef, scene.Animations));

        foreach (var clone in scene.DeathClones)
            if (!string.IsNullOrEmpty(clone.ModelFile))
                items.Add(new PreviewBuildItem(clone.ModelFile,
                    clone.Animations.Count > 0 ? clone.Animations : scene.Animations));

        foreach (var prop in scene.BreakoffProps)
            if (prop.Resolved && !string.IsNullOrEmpty(prop.ModelRef))
                items.Add(new PreviewBuildItem(prop.ModelRef,
                    prop.Animations.Count > 0 ? prop.Animations : scene.Animations));

        foreach (var terrain in scene.TerrainModels)
            if (terrain.Resolved && !items.Any(item =>
                    item.ModelReference.Equals(terrain.Model, StringComparison.OrdinalIgnoreCase)))
                items.Add(new PreviewBuildItem(terrain.Model, terrain.Animations));

        return items;
    }

    private async Task<GetPreviewSceneResult> BuildAsync(
        GetPreviewSceneParams request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ObjectId))
        {
            // Only an object has ability rows to put icons on, so only this path pays for the
            // catalog - opening a bare .alo or an .ala never touches it.
            var catalog = await icons.GetAsync(cancellationToken);
            return new GetPreviewSceneResult(
                WithReticleIcons(builder.BuildForObject(request.ObjectId, catalog, request.Environment)));
        }

        if (!string.IsNullOrWhiteSpace(request.AnimationReference))
            return new GetPreviewSceneResult(builder.BuildForAnimation(request.AnimationReference));

        if (!string.IsNullOrWhiteSpace(request.ModelReference))
            return new GetPreviewSceneResult(
                builder.BuildForModel(PreviewModelReference.Normalise(request.ModelReference)));

        return new GetPreviewSceneResult(PreviewScene.NotFound(
            string.Empty, messages.Format("PreviewHandlers_NoObjectModelAnimationWas"),
            assets.Tiers));
    }

    /// <summary>
    ///     Decodes the reticle art the scene names, as <c>data:image/png;base64,...</c> URIs.
    /// </summary>
    /// <remarks>
    ///     Done here rather than in the builder so the builder stays free of the asset resolver's
    ///     tier chain. Only the icons the subject's own hardpoint types name are resolved - five
    ///     families at most in the base game, not all fifteen files. A scene reads perfectly well
    ///     without reticles, so a name that resolves nowhere leaves the map in place with no image
    ///     rather than raising a problem.
    /// </remarks>
    private PreviewScene WithReticleIcons(PreviewScene scene)
    {
        if (scene.Reticles is not { } reticles || reticles.ByType.Count == 0)
            return scene;

        var names = reticles.ByType.Values
            .SelectMany(states => new[]
            {
                states.Enemy, states.EnemyTracked, states.Friendly, states.FriendlyTracked,
                states.FriendlyRepairing, states.FriendlyDisabled, states.FriendlyDisabledTracked
            })
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!);

        return scene with { Reticles = reticles with { Icons = PreviewReticleIcons.Resolve(assets, names) } };
    }
}