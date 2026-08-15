// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>Serves the assembled scene description.</summary>
/// <remarks>
///     Gated on <c>features.tools.modelPreview</c>: when off, every request answers with a scene
///     carrying one explanatory problem rather than an error, so a client that asks anyway gets a
///     panel that explains itself instead of a failed request.
/// </remarks>
public sealed class GetPreviewSceneHandler(
    PreviewSceneBuilder builder,
    IGameAssetResolver assets,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetPreviewSceneParams, GetPreviewSceneResult>
{
    public Task<GetPreviewSceneResult> Handle(
        GetPreviewSceneParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(new GetPreviewSceneResult(PreviewScene.NotFound(
                request.ObjectId ?? request.ModelReference ?? string.Empty,
                "The model preview is turned off (aet-eaw-edit.features.tools.modelPreview).",
                assets.Tiers)));

        if (!string.IsNullOrWhiteSpace(request.ObjectId))
            return Task.FromResult(new GetPreviewSceneResult(builder.BuildForObject(request.ObjectId)));

        if (!string.IsNullOrWhiteSpace(request.AnimationReference))
            return Task.FromResult(new GetPreviewSceneResult(
                builder.BuildForAnimation(request.AnimationReference)));

        if (!string.IsNullOrWhiteSpace(request.ModelReference))
            return Task.FromResult(new GetPreviewSceneResult(
                builder.BuildForModel(PreviewModelReference.Normalise(request.ModelReference))));

        return Task.FromResult(new GetPreviewSceneResult(PreviewScene.NotFound(
            string.Empty, "No object, model or animation was named.", assets.Tiers)));
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
                var mismatch = animation.WhyNotModel(boneNames);

                if (mismatch is null)
                    clips.Add((Path.GetFileNameWithoutExtension(name), animation));
                else
                    logger.LogInformation(
                        "Animation {Animation} does not pair with {Model}: {Reason}",
                        name, reference, mismatch);
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

public sealed class GetParticleSystemHandler(
    IGameAssetResolver assets,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetParticleSystemParams, GetParticleSystemResult>
{
    public Task<GetParticleSystemResult> Handle(
        GetParticleSystemParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModelPreview)
            return Task.FromResult(new GetParticleSystemResult(null, "The model preview is turned off."));

        var name = PreviewModelReference.Normalise(request.Name);
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(new GetParticleSystemResult(null, "No particle system was named."));

        // A proxy names the system without an extension; particle systems are .alo like models.
        if (!Path.HasExtension(name))
            name += ".alo";

        var bytes = assets.Read(PreviewModelReference.ModelPath(name));
        if (bytes is null)
            return Task.FromResult(new GetParticleSystemResult(null,
                $"Particle system '{name}' was not found. {assets.Tiers.Explain()}"));

        try
        {
            return Task.FromResult(new GetParticleSystemResult(AloParticleReader.Read(bytes)));
        }
        catch (AloFormatException e)
        {
            return Task.FromResult(new GetParticleSystemResult(null,
                $"'{name}' could not be read: {e.Message}"));
        }
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
}
