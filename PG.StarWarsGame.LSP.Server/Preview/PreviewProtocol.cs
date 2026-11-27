// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Preview;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Request for a preview scene, by GameObject or by model file.
/// </summary>
/// <remarks>
///     Exactly one of the two is expected. Both routes produce the same shape - a bare model is a
///     scene with one part - so the client renders one thing either way.
/// </remarks>
[Method("aet/getPreviewScene", Direction.ClientToServer)]
[PublicAPI]
public sealed record GetPreviewSceneParams : IRequest<GetPreviewSceneResult>
{
    /// <summary>Id of the GameObject to assemble, with its hardpoints mounted.</summary>
    public string? ObjectId { get; init; }

    /// <summary>
    ///     A model reference as the XML writes it (<c>EV_StarDestroyer.ALO</c>), or the URI of an
    ///     <c>.alo</c> the user opened. Ignored when <see cref="ObjectId" /> is set.
    /// </summary>
    public string? ModelReference { get; init; }

    /// <summary>
    ///     An <c>.ala</c> the user opened. The server finds the model it drives and returns that
    ///     scene; an animation names no model, so the pairing has to be recovered and verified.
    /// </summary>
    public string? AnimationReference { get; init; }

    /// <summary>
    ///     The map environment to assemble a land unit for - one its <c>Land_Terrain_Model_Mapping</c>
    ///     names - or null for its default model. Read only with <see cref="ObjectId" />.
    /// </summary>
    public string? Environment { get; init; }
}

/// <summary>Result of <c>aet/getPreviewScene</c>.</summary>
/// <param name="Scene">
///     The scene, or a scene carrying only an error problem when the subject did not resolve. Never
///     null: the panel always has something to show, even if that is only why it cannot show a model.
/// </param>
public sealed record GetPreviewSceneResult(PreviewScene Scene);

/// <summary>Request for every model the project can preview, for the "Preview Model" picker.</summary>
[Method("aet/listModels", Direction.ClientToServer)]
public sealed record ListModelsParams : IRequest<ListModelsResult>;

/// <summary>One model the picker offers.</summary>
/// <param name="Name">The file name, which is what <c>aet/getPreviewScene</c> resolves.</param>
/// <param name="BaseGame">True when it comes from the shipped game rather than the project's layers.</param>
public sealed record PreviewModelEntry(string Name, bool BaseGame);

/// <summary>Result of <c>aet/listModels</c>, sorted by name. Empty when the preview is turned off.</summary>
public sealed record ListModelsResult(IReadOnlyList<PreviewModelEntry> Models);

/// <summary>
///     Asks the server to stop the background build of the preview cache. Sent when the reader
///     cancels the progress the client shows for it.
/// </summary>
[Method("aet/cancelPreviewCacheBuild", Direction.ClientToServer)]
public sealed record CancelPreviewCacheBuildParams : IRequest<CancelPreviewCacheBuildResult>;

/// <summary>Result of <c>aet/cancelPreviewCacheBuild</c>. Empty: the build's own notification says it stopped.</summary>
public sealed record CancelPreviewCacheBuildResult;

/// <summary>
///     Asks the server to empty the project's preview cache: every converted model under
///     <c>.aetswg/preview/</c>. Sent by the client's Clear Preview Cache command.
/// </summary>
[Method("aet/clearPreviewCache", Direction.ClientToServer)]
public sealed record ClearPreviewCacheParams : IRequest<ClearPreviewCacheResult>;

/// <summary>Result of <c>aet/clearPreviewCache</c>.</summary>
/// <param name="Removed">How many converted models were removed.</param>
public sealed record ClearPreviewCacheResult(int Removed);

/// <summary>
///     What the server tells the client about the background build of the preview cache, as the
///     <c>aet/previewCacheBuild</c> notification: the client shows it as a progress the reader can
///     cancel.
/// </summary>
/// <param name="State">
///     <c>running</c> while models are being converted, <c>ended</c> when the build has stopped -
///     finished or cancelled.
/// </param>
/// <param name="Done">Models converted so far.</param>
/// <param name="Total">Models the build set out to convert, which grows when a subject is opened.</param>
public sealed record PreviewCacheBuildProgress(string State, int Done, int Total);

/// <summary>Request for one model's geometry as a glTF binary.</summary>
[Method("aet/getModelGlb", Direction.ClientToServer)]
public sealed record GetModelGlbParams : IRequest<GetModelGlbResult>
{
    /// <summary>The model reference as it appears in <see cref="PreviewPart.ModelRef" />.</summary>
    public string ModelReference { get; init; } = string.Empty;

    /// <summary>
    ///     Animation clips to bake into the GLB, by file name. Empty means geometry only.
    /// </summary>
    /// <remarks>
    ///     Baked in rather than fetched separately because glTF ties a clip to the skeleton it drives:
    ///     shipping them apart would mean rebuilding that association in the client, which is the work
    ///     the format exists to avoid.
    /// </remarks>
    public IReadOnlyList<string> Animations { get; init; } = [];

    /// <summary>
    ///     The <see cref="GetModelGlbResult.Version" /> of the GLB the client already holds for this
    ///     model and these clips, if it holds one. When it is still the version, the reply offers
    ///     no transfer.
    /// </summary>
    public string? Have { get; init; }
}

/// <summary>
///     Result of <c>aet/getModelGlb</c>.
/// </summary>
/// <param name="Transfer">
///     Where the client collects the GLB, or null when the model did not resolve or would not
///     parse. The bytes do not cross JSON-RPC: MEASURED, an 81 MB model took 1.1 s as base64 in the
///     reply and 0.26 s over the transfer pipe.
/// </param>
/// <param name="Animations">
///     Clips actually baked in. Shorter than the request when one did not match the skeleton, which
///     is common enough to report rather than treat as an error.
/// </param>
/// <param name="Error">Why nothing came back, in words the panel can show verbatim.</param>
/// <param name="Version">
///     What the GLB was converted from, as one token: the same for the same model bytes, clips and
///     converter. A reply with a version, no transfer and no error says the client's copy stands.
/// </param>
public sealed record GetModelGlbResult(
    PreviewTransfer? Transfer,
    IReadOnlyList<string> Animations,
    string? Error = null,
    string? Version = null);

/// <summary>
///     A payload waiting on the server's transfer pipe (<see cref="PreviewTransferPipe" />).
/// </summary>
/// <param name="Pipe">What the client connects to.</param>
/// <param name="Id">The transfer's id, 32 hex digits; collected once.</param>
/// <param name="Length">The payload's length in bytes.</param>
/// <param name="Format">What the bytes are: <c>glb</c>.</param>
public sealed record PreviewTransfer(string Pipe, string Id, long Length, string Format);

/// <summary>Request for what is inside one model, for the inspector.</summary>
[Method("aet/getModelDetail", Direction.ClientToServer)]
public sealed record GetModelDetailParams : IRequest<GetModelDetailResult>
{
    /// <summary>The model reference as it appears in <see cref="PreviewPart.ModelRef" />.</summary>
    public string ModelReference { get; init; } = string.Empty;
}

/// <summary>
///     Result of <c>aet/getModelDetail</c>.
/// </summary>
/// <remarks>
///     Separate from the GLB rather than folded into it, because the two are wanted at different
///     times: geometry is needed to draw anything at all, while this is only read when someone opens
///     a panel. Bundling it would put a Star Destroyer's 58 bone matrices into the load path of every
///     preview that never inspects one.
/// </remarks>
[PublicAPI]
public sealed record GetModelDetailResult(
    PreviewModelDetail? Detail,
    string? Error = null);

/// <summary>Request for one page of one sub-mesh's bulk geometry.</summary>
[Method("aet/getSubMeshGeometry", Direction.ClientToServer)]
[PublicAPI]
public sealed record GetSubMeshGeometryParams : IRequest<GetSubMeshGeometryResult>
{
    public string ModelReference { get; init; } = string.Empty;

    /// <summary>The mesh's position in the file - what <c>alamoMeshIndex</c> in the glTF names.</summary>
    public int MeshIndex { get; init; }

    public int SubMeshIndex { get; init; }

    /// <summary><c>vertices</c>, <c>faces</c> or <c>boneMapping</c>.</summary>
    public string Table { get; init; } = "vertices";

    public int Offset { get; init; }

    /// <summary>Rows wanted, capped server-side by <see cref="PreviewSubMeshGeometry.MaxPage" />.</summary>
    public int Count { get; init; } = 100;
}

/// <summary>Result of <c>aet/getSubMeshGeometry</c>.</summary>
[PublicAPI]
public sealed record GetSubMeshGeometryResult(
    PreviewSubMeshGeometry? Page,
    string? Error = null);

/// <summary>Request for one projectile's values, by object id.</summary>
/// <remarks>
///     The scene carries only the projectiles the SUBJECT fires, because those are what its weapons
///     name. The attacker panel offers the whole tree - you are building a weapon to shoot AT the
///     subject, so its own armament is the wrong list - and everything outside that handful had no
///     way to be resolved. This is it.
/// </remarks>
[Method("aet/getProjectile", Direction.ClientToServer)]
public sealed record GetProjectileParams : IRequest<GetProjectileResult>
{
    /// <summary>The projectile's object id, as the catalogue lists it.</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>Result of <c>aet/getProjectile</c>.</summary>
/// <remarks>
///     <see cref="Error" /> rather than an empty projectile when the id resolves to nothing: filling
///     the panel with zeroes would read as a projectile that does no damage, which is a different
///     and worse answer than "this project does not define it".
/// </remarks>
public sealed record GetProjectileResult(
    PreviewProjectile? Projectile,
    string? Error = null);

/// <summary>Request for one particle system's emitter description.</summary>
[Method("aet/getParticleSystem", Direction.ClientToServer)]
public sealed record GetParticleSystemParams : IRequest<GetParticleSystemResult>
{
    /// <summary>
    ///     The system name as a model proxy writes it, with no extension or path.
    /// </summary>
    /// <remarks>
    ///     Particle systems live beside models in <c>Data\Art\Models</c> and share their extension,
    ///     which is why a proxy can name one exactly as it would a model.
    /// </remarks>
    public string Name { get; init; } = string.Empty;
}

/// <summary>
///     Result of <c>aet/getParticleSystem</c>.
/// </summary>
/// <remarks>
///     The emitter description travels whole and is simulated in the client. Sending per-frame
///     particle state instead would mean thousands of positions at sixty frames a second over a
///     JSON-RPC channel, which is not a trade worth making for a preview.
/// </remarks>
/// <param name="ScaleFactor">
///     The owning object's <c>Scale_Factor</c>, or 1 when the request named a bare asset or the
///     object declares none.
/// </param>
/// <remarks>
///     <para>
///         A uniform RENDER SCALE on the object, not anything particle-specific:
///         <c>DatabaseMapExport.xml</c> lists <c>Scale_Factor</c> on the base GameObjectType beside
///         <c>Mass</c> and <c>LOD_Bias</c>, and the reference applies it as a uniform scale on the
///         object's world matrix. So it scales where a particle spawns as well as how big it draws.
///     </para>
///     <para>
///         Six shipped particle objects declare one and NOTHING read it: 20.0 on the four hero
///         powerup effects (Veers, Piet, Replenish Wingmen, Weaken Enemy) and 2.0 on the two
///         bombing-run explosions, so all six drew at a twentieth and a half of their size.
///     </para>
/// </remarks>
[PublicAPI]
public sealed record GetParticleSystemResult(
    AlamoParticleContent? System,
    float ScaleFactor = 1f,
    string? Error = null)
{
    /// <summary>
    ///     What the file carries that the game does not apply, with ids: the system's notices as
    ///     problems, a skipped property once per file. 38 of the 987 shipped files carry one. See
    ///     <see cref="ParticleNoticeProblems" />.
    /// </summary>
    public IReadOnlyList<PreviewProblem> Problems { get; init; } = [];

    /// <summary>
    ///     The file read, as its asset name - <c>p_rb_station_damage.alo</c> for a proxy that asked
    ///     for <c>p_rb_station_damage_ALT2</c>. Absent when nothing was read.
    /// </summary>
    /// <remarks>
    ///     Two damage stages of one model name one file, and each asks for it (#394). The panel says
    ///     a file's <see cref="Problems" /> once, under this name, however many proxies asked.
    /// </remarks>
    public string? File { get; init; }
}

/// <summary>Request for one shader's source text.</summary>
[Method("aet/getShaderSource", Direction.ClientToServer)]
public sealed record GetShaderSourceParams : IRequest<GetShaderSourceResult>
{
    /// <summary>The shader's bare file name, e.g. <c>MeshBump.fx</c> or an included <c>.fxh</c>.</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>
///     Result of <c>aet/getShaderSource</c>.
/// </summary>
/// <param name="Source">
///     The shader text, or null when no tier has it. Null is a normal answer, not a failure: the
///     renderer keeps archetype materials for exactly this case.
/// </param>
/// <param name="HasManagedShaders">
///     Whether a managed copy of the base game's shaders is reachable at all, so the UI can explain a
///     plain-looking preview rather than leaving the author guessing.
/// </param>
[PublicAPI]
public sealed record GetShaderSourceResult(
    string? Source,
    bool HasManagedShaders,
    string? Error = null);

/// <summary>Request for one texture's bytes.</summary>
[Method("aet/getModelTexture", Direction.ClientToServer)]
public sealed record GetModelTextureParams : IRequest<GetModelTextureResult>
{
    /// <summary>The texture name as a sub-mesh's shader parameter writes it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    ///     The <see cref="GetModelTextureResult.Version" /> of the copy the client already holds, if
    ///     it holds one. When it is still the version, the reply offers no transfer.
    /// </summary>
    public string? Have { get; init; }
}

/// <summary>
///     Result of <c>aet/getModelTexture</c>.
/// </summary>
/// <remarks>
///     The bytes are offered <strong>undecoded</strong>, in whatever format the file is. The browser can
///     hand DXT data straight to the GPU, so decoding here and re-encoding as PNG would cost a
///     round trip through an image codec and inflate a compressed 2048x2048 texture several times
///     over, for nothing.
/// </remarks>
/// <param name="Format">
///     The extension of the file found, without the dot - <c>dds</c>, <c>tga</c> or <c>png</c>. Not what
///     decodes it: the client decodes by the bytes, as the game does (#420).
/// </param>
/// <param name="Transfer">
///     Where the client collects the file's bytes, or null when the texture did not resolve. As for
///     a model, the bytes do not cross JSON-RPC.
/// </param>
/// <param name="Version">
///     The hash of the file's bytes: the same token while the content stands, whatever the file's
///     name or timestamps. A reply with a version, no transfer and no error says the client's copy
///     stands.
/// </param>
public sealed record GetModelTextureResult(
    string? Format,
    PreviewTransfer? Transfer,
    string? Error = null,
    string? Version = null);