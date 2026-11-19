// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Serves one model as a glTF binary: resolves it, takes it from the preview cache or converts
///     it, and offers the bytes on the transfer channel. The reply names the transfer; it carries
///     no payload.
/// </summary>
public sealed class GetModelGlbHandler(
    IGameAssetResolver assets,
    IPreviewModelCache cache,
    IPreviewTransferChannel transfers,
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

        try
        {
            var converted = cache.GetOrConvert(
                PreviewModelReference.ModelPath(reference),
                [.. request.Animations.Select(name => (name, PreviewModelReference.ModelPath(name)))],
                Convert, Judge);

            if (converted is null)
                return Task.FromResult(new GetModelGlbResult(null, [],
                    messages.Format("PreviewHandlers_ModelWasNotFound", reference,
                        assets.Tiers.Explain(messages))));

            // Said on every request, a cache hit included: the notes are kept beside the entry.
            Say(reference, converted.Notes);

            // The client's copy stands: nothing is offered, so nothing is read or sent.
            if (request.Have is { Length: > 0 } have && have == converted.Key)
                return Task.FromResult(new GetModelGlbResult(null, converted.Animations, null, converted.Key));

            return Task.FromResult(new GetModelGlbResult(
                transfers.Offer("glb", converted.Length, converted.OpenPayload), converted.Animations, null,
                converted.Key));
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
    ///     One model and the clips offered with it, as a GLB. Runs on a cache miss only; any change
    ///     to what it produces from the same bytes raises
    ///     <see cref="PreviewModelCache.ConverterVersion" />. It reads nothing but its arguments -
    ///     not even the model's name - because its result answers for every model of these bytes,
    ///     whoever asked: a request, or the background build that fills the cache ahead of one.
    /// </summary>
    public static PreviewModelConversion Convert(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered)
    {
        var model = AloModelReader.Read(aloBytes);
        var notes = new List<PreviewClipNote>();
        var clips = LoadAnimations(model, aloBytes, offered, notes);

        return new PreviewModelConversion(ModelGlbExporter.Export(model, clips), [.. clips.Select(c => c.Name)],
            notes);
    }

    /// <summary>
    ///     Which of the offered clips <see cref="Convert" /> would bake into this model, and what it
    ///     would say of the rest - the same reading of each clip, without the export. The cache
    ///     asks before it looks an entry up: only a baked clip is part of what the GLB is.
    /// </summary>
    public static IReadOnlyList<PreviewClipVerdict> Judge(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered)
    {
        if (offered.Count == 0)
            return [];

        var boneNames = BoneTargets(AloModelReader.Read(aloBytes), aloBytes);
        return
        [
            .. offered.Select(clip =>
            {
                var (animation, note) = Read(clip, boneNames);
                return new PreviewClipVerdict(animation is not null, animation is null ? note : null);
            })
        ];
    }

    /// <summary>Writes what the conversion said about the clips to the log, against this model's name.</summary>
    private void Say(string reference, IReadOnlyList<PreviewClipNote> notes)
    {
        foreach (var note in notes)
            switch (note.Kind)
            {
                case PreviewClipNoteKind.DiffersButPlays:
                    // Still worth saying: the clip will play, and the bones it moves may not be the
                    // ones it was authored to move.
                    logger.LogInformation(
                        "Animation {Animation} plays on {Model} across a differing skeleton: {Reason}",
                        note.Clip, reference, note.Reason);
                    break;
                case PreviewClipNoteKind.CannotPlay:
                    logger.LogInformation("Animation {Animation} cannot be played on {Model}: {Reason}",
                        note.Clip, reference, note.Reason);
                    break;
                default:
                    logger.LogInformation("Skipping animation {Animation}: {Reason}", note.Clip, note.Reason);
                    break;
            }
    }

    /// <summary>
    ///     Parses the offered clips, dropping any that do not belong to this skeleton.
    /// </summary>
    /// <remarks>
    ///     Dropping rather than failing: 50 shipped animations sit beside a model they were not
    ///     authored against, so a mismatch is ordinary data, not an error. The result reports which
    ///     clips actually made it.
    /// </remarks>
    private static List<(string Name, AlamoAnimationContent Animation)> LoadAnimations(
        AlamoModelContent model, byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered,
        List<PreviewClipNote> notes)
    {
        var clips = new List<(string, AlamoAnimationContent)>();
        if (offered.Count == 0)
            return clips;

        var boneNames = BoneTargets(model, aloBytes);

        foreach (var clip in offered)
        {
            var (animation, note) = Read(clip, boneNames);
            if (animation is not null)
                clips.Add((Path.GetFileNameWithoutExtension(clip.Name), animation));
            if (note is not null)
                notes.Add(note);
        }

        return clips;
    }

    /// <summary>The names a clip's tracks are matched against: the model's bone space.</summary>
    private static IReadOnlyList<string> BoneTargets(AlamoModelContent model, byte[] aloBytes)
    {
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
        return ModelNameCatalog.ReadBoneReferenceTargets(
            aloBytes, _ => model.Bones.Select(b => b.Name).ToList());
    }

    /// <summary>
    ///     One offered clip against the model's bone space: the animation when it plays, and what
    ///     there is to say about it either way. The one place that decides, for the conversion and
    ///     for the cache's key alike.
    /// </summary>
    private static (AlamoAnimationContent? Animation, PreviewClipNote? Note) Read(
        PreviewClipSource clip, IReadOnlyList<string> boneNames)
    {
        var name = clip.Name;
        try
        {
            var animation = AlaAnimationReader.Read(clip.Bytes);

            // FITS, not MATCHES. Requiring an identical skeleton dropped clips the base game
            // plays: a unit that borrows another model's animation set does so across skeletons
            // that differ, and the engine binds by index regardless. What cannot be recovered is
            // a track reaching past the end of the bone list - there is no node to drive.
            if (!animation.FitsSkeleton(boneNames))
                return (null, new PreviewClipNote(name, PreviewClipNoteKind.CannotPlay,
                    animation.WhyNotModel(boneNames) ?? string.Empty));

            return (animation, animation.WhyNotModel(boneNames) is { } disagreement
                ? new PreviewClipNote(name, PreviewClipNoteKind.DiffersButPlays, disagreement)
                : null);
        }
        catch (AloFormatException e)
        {
            return (null, new PreviewClipNote(name, PreviewClipNoteKind.Unreadable, e.Message));
        }
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

/// <summary>
///     Serves one texture's bytes, undecoded: resolves the name once and offers the file on the
///     transfer channel. The reply names the transfer and the format found; it carries no payload.
/// </summary>
public sealed class GetModelTextureHandler(
    IGameAssetResolver assets,
    IPreviewAssetFingerprints fingerprints,
    IFileHelper fileHelper,
    IPreviewTransferChannel transfers,
    IMessageCatalog messages)
    : IJsonRpcRequestHandler<GetModelTextureParams, GetModelTextureResult>
{
    /// <summary>
    ///     Textures live here. A shader parameter names one without a path - or, in the shipped
    ///     particle systems, by the art machine's absolute path, of which the game keeps the file name
    ///     (<see cref="GameTextureName.FileName" />, #292).
    /// </summary>
    private const string TextureDirectory = "Data/Art/Textures/";

    public Task<GetModelTextureResult> Handle(
        GetModelTextureParams request, CancellationToken cancellationToken)
    {
        var name = GameTextureName.FileName(request.Name);
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(new GetModelTextureResult(null, null,
                messages.Format("PreviewHandlers_NoTextureWasNamed")));

        // Resolved ONCE. The location says the ACTUAL extension - a .tga reference routinely
        // resolves to a .dds, and the client picks its decoder from what it is given - and the same
        // location is what is read, where this used to resolve the name a second time to read it.
        var location = assets.Locate(TextureDirectory + name);
        if (location is null)
            return Task.FromResult(new GetModelTextureResult(null, null,
                messages.Format("PreviewHandlers_TextureWasNotFound", name,
                    assets.Tiers.Explain(messages))));

        var format = Path.GetExtension(location.ResolvedPath).TrimStart('.').ToLowerInvariant();

        // The version is the hash of the file's bytes. A loose file is read for it ONCE: after
        // that its stamp answers, until the stamp moves or the watcher reports the file - a file
        // written over with the same length and write time has the stamp of the one it replaced.
        // An archive entry has no stamp and is read every time.
        var identity = fingerprints.Identify(location);
        if (identity is null)
            return Task.FromResult(new GetModelTextureResult(null, null,
                messages.Format("PreviewHandlers_TextureResolvedButCouldNot", name)));

        // The client's copy stands: nothing is offered, so nothing is held or sent.
        if (identity.Hash == request.Have)
            return Task.FromResult(new GetModelTextureResult(format, null, null, identity.Hash));

        // A loose file is streamed from disk when the client collects it, so its bytes are not
        // held here in between.
        if (LooseFile(location) is { } file)
            return Task.FromResult(new GetModelTextureResult(format,
                transfers.Offer(format, file.Length, () => fileHelper.FileSystem.File.OpenRead(file.Path)),
                null, identity.Hash));

        // An archive entry has no file to stream, so its bytes are held until they are collected.
        var bytes = identity.Bytes();
        if (bytes is null)
            return Task.FromResult(new GetModelTextureResult(null, null,
                messages.Format("PreviewHandlers_TextureResolvedButCouldNot", name)));

        return Task.FromResult(new GetModelTextureResult(format,
            transfers.Offer(format, bytes.LongLength, () => new MemoryStream(bytes, false)), null,
            identity.Hash));
    }

    /// <summary>The file a location names and its length, or null for an archive entry or a file that is not there.</summary>
    private (string Path, long Length)? LooseFile(GameAssetLocation location)
    {
        if (location.Tier == GameAssetTier.Archive)
            return null;

        try
        {
            var info = fileHelper.FileSystem.FileInfo.New(location.ResolvedPath);
            return info.Exists ? (location.ResolvedPath, info.Length) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            return null;
        }
    }
}
