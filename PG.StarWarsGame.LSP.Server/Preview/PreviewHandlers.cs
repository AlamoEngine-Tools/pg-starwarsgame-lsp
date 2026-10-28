// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
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
            .Resolve(objectId.Trim());
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