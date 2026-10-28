// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>Serves one particle system's emitter description.</summary>
/// <summary>
///     Serves a shader's source text.
/// </summary>
/// <remarks>
///     The sources are never shipped with the extension - see <see cref="ShaderSourceResolver" />.
///     This only reads whatever copy the user has, and answering null is normal.
/// </remarks>
public sealed class GetShaderSourceHandler(ShaderSourceResolver shaders)
    : IJsonRpcRequestHandler<GetShaderSourceParams, GetShaderSourceResult>
{
    public Task<GetShaderSourceResult> Handle(
        GetShaderSourceParams request, CancellationToken cancellationToken)
    {
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
    IMessageCatalog messages,
    IGameIndexService indexService,
    ISchemaProvider schema,
    IVariantTagSource tagSource)
    : IJsonRpcRequestHandler<GetParticleSystemParams, GetParticleSystemResult>
{
    public Task<GetParticleSystemResult> Handle(
        GetParticleSystemParams request, CancellationToken cancellationToken)
    {
        // Resolved ONCE: the object carries both the asset name and the render scale, and looking it
        // up twice would resolve the whole variant chain twice for one request.
        var owner = ObjectBehind(request.Name);

        // A proxy names its system with its own damage stage still attached - `p_smoke_small_thin_ALT2`
        // - and the asset is filed under the bare name. Stripped AFTER the object lookup, so a
        // <Particle> object is still found by the name it was actually asked for.
        var name = PreviewModelReference.StripLevelSuffix(PreviewModelReference.Normalise(
            (owner is null ? null : owner.ValueOf("Space_Model_Name") ?? owner.ValueOf("Land_Model_Name"))
            ?? request.Name));

        if (string.IsNullOrEmpty(name))
            return Task.FromResult(
                new GetParticleSystemResult(null,
                    Error: messages.Format("PreviewHandlers_NoParticleSystemWasNamed")));

        var scale = ScaleFactorOf(owner);

        // A proxy names the system without an extension; particle systems are .alo like models.
        if (!Path.HasExtension(name))
            name += ".alo";

        var bytes = assets.Read(PreviewModelReference.ModelPath(name));
        if (bytes is null)
            return Task.FromResult(new GetParticleSystemResult(null, scale,
                messages.Format("PreviewHandlers_ParticleSystemWasNotFound", name,
                    assets.Tiers.Explain(messages))));

        try
        {
            var system = AloParticleReader.Read(bytes);
            return Task.FromResult(new GetParticleSystemResult(system, scale)
            {
                Problems = ParticleNoticeProblems.For(system)
            });
        }
        catch (AloFormatException e)
        {
            return Task.FromResult(new GetParticleSystemResult(null, scale,
                messages.Format("PreviewHandlers_CouldNotBeRead", name, e.Message)));
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
            .Resolve(objectId.Trim(), GameTypeNames.GameObjectType);
    }

    /// <summary>
    ///     The object's uniform render scale, defaulting to 1.
    /// </summary>
    /// <remarks>
    ///     Through <see cref="EngineScaleFactor" />, which the scene builder reads too - the two
    ///     must not be able to disagree about one object. The variant chain is already walked by
    ///     the resolver.
    /// </remarks>
    private static float ScaleFactorOf(EffectiveObject? effective)
    {
        return effective is null
            ? EngineScaleFactor.None
            : EngineScaleFactor.Of(effective.ValueOf("Scale_Factor"));
    }
}