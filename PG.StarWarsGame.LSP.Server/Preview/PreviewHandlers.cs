// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
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
///         Resolution is <see cref="PreviewSceneBuilder.ProjectilesFor" />, the same reader the scene
///         uses, so a projectile filled on demand and one that arrived with the scene cannot differ.
///     </para>
/// </remarks>
public sealed class GetProjectileHandler(
    ILspConfigurationProvider config,
    IGameIndexService indexService,
    ISchemaProvider schema,
    IVariantTagSource tagSource)
    : IJsonRpcRequestHandler<GetProjectileParams, GetProjectileResult>
{
    public Task<GetProjectileResult> Handle(
        GetProjectileParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(
                new GetProjectileResult(null, "The model preview is turned off."));

        var id = request.Name?.Trim() ?? string.Empty;
        if (id.Length == 0)
            return Task.FromResult(new GetProjectileResult(null, "No projectile was named."));

        // The problems the builder would have raised are collected and dropped: this is a request
        // for ONE projectile and the caller wants an answer about it, not a scene's problem list.
        var problems = new List<PreviewProblem>();
        var resolver = new EffectiveObjectResolver(indexService.Current, schema, tagSource);
        var found = PreviewSceneBuilder.ProjectilesFor(resolver, [id], problems).FirstOrDefault();

        return Task.FromResult(found is null
            ? new GetProjectileResult(null, $"Projectile '{id}' is not defined in this project.")
            : new GetProjectileResult(found));
    }
}

/// <summary>Serves the assembled scene description.</summary>
/// <remarks>
///     Gated on <c>features.tools.modelPreview</c>: when off, every request answers with a scene
///     carrying one explanatory problem rather than an error, so a client that asks anyway gets a
///     panel that explains itself instead of a failed request.
/// </remarks>
public sealed class GetPreviewSceneHandler(
    PreviewSceneBuilder builder,
    IGameAssetResolver assets,
    ILspConfigurationProvider config,
    IWorkspaceIconCatalog? icons = null)
    : IJsonRpcRequestHandler<GetPreviewSceneParams, GetPreviewSceneResult>
{
    public async Task<GetPreviewSceneResult> Handle(
        GetPreviewSceneParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return new GetPreviewSceneResult(PreviewScene.NotFound(
                request.ObjectId ?? request.ModelReference ?? string.Empty,
                "The model preview is turned off (aet-eaw-edit.features.tools.modelPreview).",
                assets.Tiers));

        if (!string.IsNullOrWhiteSpace(request.ObjectId))
        {
            // Only an object has ability rows to put icons on, so only this path pays for the
            // catalog - opening a bare .alo or an .ala never touches it.
            var catalog = icons is null ? null : await icons.GetAsync(cancellationToken);
            return new GetPreviewSceneResult(
                WithReticleIcons(builder.BuildForObject(request.ObjectId, catalog)));
        }

        if (!string.IsNullOrWhiteSpace(request.AnimationReference))
            return new GetPreviewSceneResult(builder.BuildForAnimation(request.AnimationReference));

        if (!string.IsNullOrWhiteSpace(request.ModelReference))
            return new GetPreviewSceneResult(
                builder.BuildForModel(PreviewModelReference.Normalise(request.ModelReference)));

        return new GetPreviewSceneResult(PreviewScene.NotFound(
            string.Empty, "No object, model or animation was named.", assets.Tiers));
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

/// <summary>Serves one model as a glTF binary.</summary>
public sealed class GetModelGlbHandler(
    IGameAssetResolver assets,
    ILspConfigurationProvider config,
    ILogger<GetModelGlbHandler> logger)
    : IJsonRpcRequestHandler<GetModelGlbParams, GetModelGlbResult>
{
    public Task<GetModelGlbResult> Handle(GetModelGlbParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(new GetModelGlbResult(null, [], "The model preview is turned off."));

        var reference = PreviewModelReference.Normalise(request.ModelReference);
        if (string.IsNullOrEmpty(reference))
            return Task.FromResult(new GetModelGlbResult(null, [], "No model was named."));

        var bytes = assets.Read(PreviewModelReference.ModelPath(reference));
        if (bytes is null)
            return Task.FromResult(new GetModelGlbResult(null, [],
                $"Model '{reference}' was not found. {assets.Tiers.Explain()}"));

        try
        {
            var model = AloModelReader.Read(bytes);
            var clips = LoadAnimations(model, bytes, reference, request.Animations);

            var glb = ModelGlbExporter.Export(model, clips);
            return Task.FromResult(new GetModelGlbResult(
                Convert.ToBase64String(glb), [.. clips.Select(c => c.Name)]));
        }
        catch (AloFormatException e)
        {
            // The reader is strict on purpose; a refusal names the chunk that did not add up, which is
            // far more use to the author than a half-drawn model would be.
            return Task.FromResult(new GetModelGlbResult(null, [],
                $"'{reference}' could not be read: {e.Message}"));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Unexpected failure exporting {Model}", reference);
            return Task.FromResult(new GetModelGlbResult(null, [],
                $"'{reference}' could not be exported: {e.Message}"));
        }
    }

    /// <summary>
    ///     Reads the requested clips, dropping any that do not belong to this skeleton.
    /// </summary>
    /// <remarks>
    ///     Dropping rather than failing: 50 shipped animations sit beside a model they were not
    ///     authored against, so a mismatch is ordinary data, not an error. The result reports which
    ///     clips actually made it.
    /// </remarks>
    private List<(string Name, AlamoAnimationContent Animation)> LoadAnimations(
        AlamoModelContent model, byte[] aloBytes, string reference,
        IReadOnlyList<string> requested)
    {
        var clips = new List<(string, AlamoAnimationContent)>();
        if (requested.Count == 0)
            return clips;

        // Bones UNIONED WITH MESH NAMES, which is the engine's own bone space: it assumes a bone at
        // the origin of every mesh, so a clip may legitimately drive one of those at an index past
        // the end of the skeleton, and matching against the skeleton alone rejects it.
        //
        // Measured over the whole FOC tree: of 2826 model/clip pairs whose names pair them, two are
        // recovered by the union and none are lost. Both are Underworld turrets -
        // `Ub_turretmst50_idle00` and `Ub_turretptl05_idle00` - and both drive a last bone called
        // `collision` that the model carries only as a MESH. Two is a small number and the rule is
        // still right: the alternative is a clip silently dropped for being what the engine calls
        // valid.
        //
        // Through `ModelNameCatalog` rather than rebuilt here, so the union stays defined once -
        // it is the same call `BoneNameExtractor` makes to populate `index.ModelBones`.
        var boneNames = ModelNameCatalog.ReadBoneReferenceTargets(
            aloBytes, _ => model.Bones.Select(b => b.Name).ToList());

        foreach (var name in requested)
        {
            var bytes = assets.Read(PreviewModelReference.ModelPath(name));
            if (bytes is null)
                continue;

            try
            {
                var animation = AlaAnimationReader.Read(bytes);

                // FITS, not MATCHES. Requiring an identical skeleton dropped clips the base game
                // plays: a unit that borrows another model's animation set does so across skeletons
                // that differ, and the engine binds by index regardless. What cannot be recovered is
                // a track reaching past the end of the bone list - there is no node to drive.
                if (animation.FitsSkeleton(boneNames))
                {
                    clips.Add((Path.GetFileNameWithoutExtension(name), animation));

                    // Still worth saying: the clip will play, and the bones it moves may not be the
                    // ones it was authored to move.
                    if (animation.WhyNotModel(boneNames) is { } disagreement)
                        logger.LogInformation(
                            "Animation {Animation} plays on {Model} across a differing skeleton: "
                            + "{Reason}", name, reference, disagreement);
                }
                else
                {
                    logger.LogInformation(
                        "Animation {Animation} cannot be played on {Model}: {Reason}",
                        name, reference, animation.WhyNotModel(boneNames));
                }
            }
            catch (AloFormatException e)
            {
                logger.LogInformation("Skipping animation {Animation}: {Reason}", name, e.Message);
            }
        }

        return clips;
    }
}

/// <summary>Serves what is inside one model: bones, mesh bounds and proxies.</summary>
/// <remarks>
///     Everything here is read off the FILE rather than derived from what was exported. The glTF the
///     client holds has been rotated Z-up to Y-up and had its doubled-up bone/mesh nodes split apart,
///     so a matrix taken from it is not a matrix the author has ever seen.
/// </remarks>
public sealed class GetModelDetailHandler(
    IGameAssetResolver assets,
    ILspConfigurationProvider config,
    ILogger<GetModelDetailHandler> logger)
    : IJsonRpcRequestHandler<GetModelDetailParams, GetModelDetailResult>
{
    public Task<GetModelDetailResult> Handle(
        GetModelDetailParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(new GetModelDetailResult(null, "The model preview is turned off."));

        var reference = PreviewModelReference.Normalise(request.ModelReference);
        if (string.IsNullOrEmpty(reference))
            return Task.FromResult(new GetModelDetailResult(null, "No model was named."));

        var bytes = assets.Read(PreviewModelReference.ModelPath(reference));
        if (bytes is null)
            return Task.FromResult(new GetModelDetailResult(null,
                $"Model '{reference}' was not found. {assets.Tiers.Explain()}"));

        try
        {
            // Geometry included: the mesh panel reports vertex and triangle counts per mesh, and
            // SkipGeometry would leave every one of them at zero.
            var model = AloModelReader.Read(bytes);

            return Task.FromResult(new GetModelDetailResult(PreviewModelDetail.From(reference, model)));
        }
        catch (AloFormatException e)
        {
            return Task.FromResult(new GetModelDetailResult(null,
                $"'{reference}' could not be read: {e.Message}"));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Unexpected failure reading detail for {Model}", reference);
            return Task.FromResult(new GetModelDetailResult(null,
                $"'{reference}' could not be inspected: {e.Message}"));
        }
    }
}

/// <summary>Serves one page of one sub-mesh's vertices, faces or bone mapping.</summary>
/// <remarks>
///     Read fresh from the file on every request rather than cached. The alternative is holding a
///     parsed model per open preview for a table almost nobody opens, and the read is a few
///     milliseconds against a memory cost that would last as long as the panel does.
/// </remarks>
public sealed class GetSubMeshGeometryHandler(
    IGameAssetResolver assets,
    ILspConfigurationProvider config,
    ILogger<GetSubMeshGeometryHandler> logger)
    : IJsonRpcRequestHandler<GetSubMeshGeometryParams, GetSubMeshGeometryResult>
{
    public Task<GetSubMeshGeometryResult> Handle(
        GetSubMeshGeometryParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(new GetSubMeshGeometryResult(null,
                "The model preview is turned off."));

        var reference = PreviewModelReference.Normalise(request.ModelReference);
        if (string.IsNullOrEmpty(reference))
            return Task.FromResult(new GetSubMeshGeometryResult(null, "No model was named."));

        var bytes = assets.Read(PreviewModelReference.ModelPath(reference));
        if (bytes is null)
            return Task.FromResult(new GetSubMeshGeometryResult(null,
                $"Model '{reference}' was not found. {assets.Tiers.Explain()}"));

        try
        {
            var model = AloModelReader.Read(bytes);

            return Task.FromResult(new GetSubMeshGeometryResult(PreviewSubMeshGeometry.From(
                reference, model, request.MeshIndex, request.SubMeshIndex, request.Table,
                request.Offset, request.Count)));
        }
        catch (ArgumentOutOfRangeException e)
        {
            // The panel asked about something this model does not have - a stale request after a
            // reload, most likely. Its own words say which part did not add up.
            return Task.FromResult(new GetSubMeshGeometryResult(null, e.Message));
        }
        catch (AloFormatException e)
        {
            return Task.FromResult(new GetSubMeshGeometryResult(null,
                $"'{reference}' could not be read: {e.Message}"));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Unexpected failure reading geometry for {Model}", reference);
            return Task.FromResult(new GetSubMeshGeometryResult(null,
                $"'{reference}' could not be inspected: {e.Message}"));
        }
    }
}

/// <summary>Serves one texture's bytes, undecoded.</summary>
public sealed class GetModelTextureHandler(
    IGameAssetResolver assets,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetModelTextureParams, GetModelTextureResult>
{
    /// <summary>Textures live here, and a shader parameter names one without any path.</summary>
    private const string TextureDirectory = "Data/Art/Textures/";

    public Task<GetModelTextureResult> Handle(
        GetModelTextureParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(new GetModelTextureResult(null, null, "The model preview is turned off."));

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(new GetModelTextureResult(null, null, "No texture was named."));

        // Locate before reading so the ACTUAL extension is reported: a .tga reference routinely
        // resolves to a .dds, and the client picks its decoder from what it is given.
        var location = assets.Locate(TextureDirectory + name);
        if (location is null)
            return Task.FromResult(new GetModelTextureResult(null, null,
                $"Texture '{name}' was not found. {assets.Tiers.Explain()}"));

        var bytes = assets.Read(TextureDirectory + name);
        if (bytes is null)
            return Task.FromResult(new GetModelTextureResult(null, null,
                $"Texture '{name}' resolved but could not be read."));

        var format = Path.GetExtension(location.ResolvedPath).TrimStart('.').ToLowerInvariant();
        return Task.FromResult(new GetModelTextureResult(format, Convert.ToBase64String(bytes)));
    }
}

/// <summary>Serves one particle system's emitter description.</summary>
/// <summary>
///     Serves a shader's source text.
/// </summary>
/// <remarks>
///     The sources are never shipped with the extension - see <see cref="ShaderSourceResolver" />.
///     This only reads whatever copy the user has, and answering null is normal.
/// </remarks>
public sealed class GetShaderSourceHandler(
    ShaderSourceResolver shaders,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetShaderSourceParams, GetShaderSourceResult>
{
    public Task<GetShaderSourceResult> Handle(
        GetShaderSourceParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(
                new GetShaderSourceResult(null, false, "The model preview is turned off."));

        return Task.FromResult(
            new GetShaderSourceResult(shaders.Read(request.Name), shaders.HasManagedShaders));
    }
}

/// <summary>
///     One particle system's geometry, by asset name or by the name of the object that owns it.
/// </summary>
/// <remarks>
///     Both shapes arrive, and only one of them used to work. A model's particle PROXY names the
///     asset - <c>pe_stardestroyerengines</c> - and that is most of the traffic. But
///     <c>Death_Explosions</c> and <c>Debris_Attached_Particle</c> name a <c>&lt;Particle&gt;</c>
///     GAME OBJECT, which then names the asset in its own <c>Space_Model_Name</c>. Reading those as
///     file names asked for <c>Large_Explosion_Space_Empire.alo</c>, which exists nowhere, so no
///     death explosion has ever played - on a hardpoint or on the wreckage it sheds.
/// </remarks>
public sealed class GetParticleSystemHandler(
    IGameAssetResolver assets,
    ILspConfigurationProvider config,
    IGameIndexService indexService,
    ISchemaProvider schema,
    IVariantTagSource tagSource)
    : IJsonRpcRequestHandler<GetParticleSystemParams, GetParticleSystemResult>
{
    public Task<GetParticleSystemResult> Handle(
        GetParticleSystemParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(
                new GetParticleSystemResult(null, Error: "The model preview is turned off."));

        // Resolved ONCE: the object carries both the asset name and the render scale, and looking it
        // up twice would resolve the whole variant chain twice for one request.
        var owner = ObjectBehind(request.Name);

        // A proxy names its system with its own damage stage still attached - `p_smoke_small_thin_ALT2`
        // - and the asset is filed under the bare name. Stripped AFTER the object lookup, so a
        // <Particle> object is still found by the name it was actually asked for.
        var name = PreviewModelReference.StripLevelSuffix(PreviewModelReference.Normalise(
            (owner is null ? null : Tag(owner, "Space_Model_Name") ?? Tag(owner, "Land_Model_Name"))
            ?? request.Name));

        if (string.IsNullOrEmpty(name))
            return Task.FromResult(
                new GetParticleSystemResult(null, Error: "No particle system was named."));

        var scale = ScaleFactorOf(owner);

        // A proxy names the system without an extension; particle systems are .alo like models.
        if (!Path.HasExtension(name))
            name += ".alo";

        var bytes = assets.Read(PreviewModelReference.ModelPath(name));
        if (bytes is null)
            return Task.FromResult(new GetParticleSystemResult(null, scale,
                $"Particle system '{name}' was not found. {assets.Tiers.Explain()}"));

        try
        {
            return Task.FromResult(
                new GetParticleSystemResult(AloParticleReader.Read(bytes), scale));
        }
        catch (AloFormatException e)
        {
            return Task.FromResult(new GetParticleSystemResult(null, scale,
                $"'{name}' could not be read: {e.Message}"));
        }
    }

    /// <summary>
    ///     The game object a name stands for, or null when the name is not one.
    /// </summary>
    /// <remarks>
    ///     An unknown name resolves to nothing and is left exactly as it came in - a bare asset name
    ///     is the other legitimate caller, and must cost nothing. The caller reads the model as
    ///     space first and land as the fallback: a particle is not theatre-specific, and a dozen of
    ///     foc's particle objects declare only the land model.
    /// </remarks>
    private EffectiveObject? ObjectBehind(string? objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId) || Path.HasExtension(objectId.Trim()))
            return null;

        return new EffectiveObjectResolver(indexService.Current, schema, tagSource)
            .Resolve(objectId.Trim());
    }

    /// <summary>
    ///     The object's uniform render scale, defaulting to 1.
    /// </summary>
    /// <remarks>
    ///     Guarded exactly as the reference guards it (<c>GameObjectCatalog.cpp</c>): anything
    ///     non-finite or non-positive falls back to 1, because a zero or negative scale collapses
    ///     the object rather than sizing it. The variant chain is already walked by the resolver.
    /// </remarks>
    private static float ScaleFactorOf(EffectiveObject? effective)
    {
        if (effective is null || Tag(effective, "Scale_Factor") is not { } raw)
            return 1f;

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && float.IsFinite(value) && value > 0f
                ? value
                : 1f;
    }

    private static string? Tag(EffectiveObject effective, string tagName)
    {
        var value = effective.Tags
            .FirstOrDefault(t => string.Equals(t.TagName, tagName, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }
}

/// <summary>
///     Turns whatever the client sends into the bare model name the resolver expects.
/// </summary>
/// <remarks>
///     Two callers send two shapes: the XML writes <c>EV_StarDestroyer.ALO</c>, while a custom editor
///     opening a file sends a <c>file:///</c> URI. Both mean the same model, and normalising here
///     keeps that knowledge out of every handler.
/// </remarks>
public static class PreviewModelReference
{
    private const string ModelDirectory = "Data/Art/Models/";

    public static string Normalise(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var value = reference.Trim();

        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            value = Uri.UnescapeDataString(value);

        // Take the file name whichever separator was used - a URI brings forward slashes, the XML
        // sometimes brings backslashes, and neither carries a path we should trust anyway.
        var lastSlash = value.LastIndexOfAny(['/', '\\']);
        return lastSlash >= 0 ? value[(lastSlash + 1)..] : value;
    }

    /// <summary>The game-relative path a model or animation reference resolves against.</summary>
    public static string ModelPath(string reference)
    {
        return ModelDirectory + Normalise(reference);
    }

    /// <summary>
    ///     Removes the <c>_ALT&lt;n&gt;</c> and <c>_LOD&lt;n&gt;</c> tags a PROXY name carries, leaving
    ///     the asset name the effect is actually filed under.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A proxy carries its damage stage and detail level in its own name, and the particle
    ///         system it names is filed without them: <c>p_smoke_small_thin_ALT2</c> is a proxy,
    ///         <c>p_smoke_small_thin.alo</c> is the file. The engine strips the tags at the load site -
    ///         <c>ObjectTemplate.cpp</c> walks <c>_ALT</c> and <c>_LOD</c> out of the name before
    ///         <c>Assets::LoadParticleSystem</c> - so without this every level-tagged effect asks for a
    ///         file that exists nowhere. 152 shipped models carry 206 such names between them.
    ///     </para>
    ///     <para>
    ///         Every occurrence, either marker, in either order: <c>p_fire_small01_ALT3_LOD0</c> is a
    ///         real shape. A marker with no digits after it is part of the name and is left alone,
    ///         which is the rule the reader uses to decide there is no level there at all.
    ///     </para>
    ///     <para>
    ///         Only ever applied to a proxy name. No shipped asset in either tree has a level tag in
    ///         its real file name, so this cannot take a name away from a file that has one - but a
    ///         model reference out of the XML is an authored file name and has no business here.
    ///     </para>
    /// </remarks>
    public static string StripLevelSuffix(string? reference)
    {
        return ModelLevelTag.Strip(reference);
    }
}
