// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>Serves one model as a glTF binary.</summary>
public sealed class GetModelGlbHandler(
    IGameAssetResolver assets,
    IMessageCatalog messages,
    ILogger<GetModelGlbHandler> logger)
    : IJsonRpcRequestHandler<GetModelGlbParams, GetModelGlbResult>
{
    public Task<GetModelGlbResult> Handle(GetModelGlbParams request, CancellationToken cancellationToken)
    {
        var reference = PreviewModelReference.Normalise(request.ModelReference);
        if (string.IsNullOrEmpty(reference))
            return Task.FromResult(new GetModelGlbResult(null, [],
                messages.Format("PreviewHandlers_NoModelWasNamed")));

        var bytes = assets.Read(PreviewModelReference.ModelPath(reference));
        if (bytes is null)
            return Task.FromResult(new GetModelGlbResult(null, [],
                messages.Format("PreviewHandlers_ModelWasNotFound", reference,
                    assets.Tiers.Explain(messages))));

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
                messages.Format("PreviewHandlers_CouldNotBeRead", reference, e.Message)));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Unexpected failure exporting {Model}", reference);
            return Task.FromResult(new GetModelGlbResult(null, [],
                messages.Format("PreviewHandlers_CouldNotBeExported", reference,
                    e.Message)));
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
    IMessageCatalog messages,
    ILogger<GetModelDetailHandler> logger)
    : IJsonRpcRequestHandler<GetModelDetailParams, GetModelDetailResult>
{
    public Task<GetModelDetailResult> Handle(
        GetModelDetailParams request, CancellationToken cancellationToken)
    {
        var reference = PreviewModelReference.Normalise(request.ModelReference);
        if (string.IsNullOrEmpty(reference))
            return Task.FromResult(new GetModelDetailResult(null,
                messages.Format("PreviewHandlers_NoModelWasNamed")));

        var bytes = assets.Read(PreviewModelReference.ModelPath(reference));
        if (bytes is null)
            return Task.FromResult(new GetModelDetailResult(null,
                messages.Format("PreviewHandlers_ModelWasNotFound", reference,
                    assets.Tiers.Explain(messages))));

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
                messages.Format("PreviewHandlers_CouldNotBeRead", reference, e.Message)));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Unexpected failure reading detail for {Model}", reference);
            return Task.FromResult(new GetModelDetailResult(null,
                messages.Format("PreviewHandlers_CouldNotBeInspected", reference,
                    e.Message)));
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
    IMessageCatalog messages,
    ILogger<GetSubMeshGeometryHandler> logger)
    : IJsonRpcRequestHandler<GetSubMeshGeometryParams, GetSubMeshGeometryResult>
{
    public Task<GetSubMeshGeometryResult> Handle(
        GetSubMeshGeometryParams request, CancellationToken cancellationToken)
    {
        var reference = PreviewModelReference.Normalise(request.ModelReference);
        if (string.IsNullOrEmpty(reference))
            return Task.FromResult(new GetSubMeshGeometryResult(null,
                messages.Format("PreviewHandlers_NoModelWasNamed")));

        var bytes = assets.Read(PreviewModelReference.ModelPath(reference));
        if (bytes is null)
            return Task.FromResult(new GetSubMeshGeometryResult(null,
                messages.Format("PreviewHandlers_ModelWasNotFound", reference,
                    assets.Tiers.Explain(messages))));

        try
        {
            var model = AloModelReader.Read(bytes);

            return Task.FromResult(new GetSubMeshGeometryResult(PreviewSubMeshGeometry.From(
                reference, model, request.MeshIndex, request.SubMeshIndex, request.Table,
                request.Offset, request.Count, messages)));
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
                messages.Format("PreviewHandlers_CouldNotBeRead", reference, e.Message)));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Unexpected failure reading geometry for {Model}", reference);
            return Task.FromResult(new GetSubMeshGeometryResult(null,
                messages.Format("PreviewHandlers_CouldNotBeInspected", reference,
                    e.Message)));
        }
    }
}

/// <summary>Serves one texture's bytes, undecoded.</summary>
public sealed class GetModelTextureHandler(
    IGameAssetResolver assets,
    IMessageCatalog messages)
    : IJsonRpcRequestHandler<GetModelTextureParams, GetModelTextureResult>
{
    /// <summary>Textures live here, and a shader parameter names one without any path.</summary>
    private const string TextureDirectory = "Data/Art/Textures/";

    public Task<GetModelTextureResult> Handle(
        GetModelTextureParams request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(new GetModelTextureResult(null, null,
                messages.Format("PreviewHandlers_NoTextureWasNamed")));

        // Locate before reading so the ACTUAL extension is reported: a .tga reference routinely
        // resolves to a .dds, and the client picks its decoder from what it is given.
        var location = assets.Locate(TextureDirectory + name);
        if (location is null)
            return Task.FromResult(new GetModelTextureResult(null, null,
                messages.Format("PreviewHandlers_TextureWasNotFound", name,
                    assets.Tiers.Explain(messages))));

        var bytes = assets.Read(TextureDirectory + name);
        if (bytes is null)
            return Task.FromResult(new GetModelTextureResult(null, null,
                messages.Format("PreviewHandlers_TextureResolvedButCouldNot", name)));

        var format = Path.GetExtension(location.ResolvedPath).TrimStart('.').ToLowerInvariant();
        return Task.FromResult(new GetModelTextureResult(format, Convert.ToBase64String(bytes)));
    }
}