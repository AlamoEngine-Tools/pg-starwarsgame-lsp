// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>
///     How a bone orients itself towards the camera or the light. Mirrors the engine's
///     <c>BillboardType</c>; the ordinals are the values stored in the <c>0x206</c> bone chunk.
/// </summary>
public enum AlamoBillboardType
{
    Disable = 0,
    Parallel = 1,
    Face = 2,
    ZAxisView = 3,
    ZAxisLight = 4,
    ZAxisWind = 5,
    SunlightGlow = 6,
    Sun = 7
}

/// <summary>
///     One bone of a model's skeleton.
/// </summary>
/// <param name="Index">Position in the file's bone list, which is what every other chunk references.</param>
/// <param name="Name">
///     The bone name as stored. Callers that match against XML bone references should do so
///     case-insensitively - the engine uppercases, and the shipped files are mixed case
///     (<c>HP_F-L_EmitDamage</c>).
/// </param>
/// <param name="ParentIndex">Index of the parent bone, or <c>-1</c> for a root.</param>
/// <param name="Visible">Whether the bone starts visible; an animation may toggle this per frame.</param>
/// <param name="Billboard">
///     Billboard mode. Always <see cref="AlamoBillboardType.Disable" /> for the older <c>0x205</c>
///     bone form, which carries no billboard field at all.
/// </param>
/// <param name="RelativeTransform">The bone's transform in its parent's space.</param>
/// <param name="AbsoluteTransform">
///     The transform in model space, composed down the parent chain. Precomputed because nearly every
///     consumer wants it and recomputing per lookup is what made the C++ viewer cache it too.
/// </param>
public sealed record AlamoModelBone(
    int Index,
    string Name,
    int ParentIndex,
    bool Visible,
    AlamoBillboardType Billboard,
    Matrix4x4 RelativeTransform,
    Matrix4x4 AbsoluteTransform);

/// <summary>
///     What to decode when reading a model.
/// </summary>
/// <remarks>
///     Structure is always read and always validated; the options only control how much of the bulk
///     data is turned into objects. <see cref="SkipGeometry" /> exists because the bone/mesh name
///     catalog scans every model in a game repository - 2353 files and about 1.5 GB in the shipped
///     trees - and wants nothing but names. It still checks that the vertex and index chunks are the
///     size their declared counts imply, so a names-only scan never accepts a file the full read
///     would reject.
/// </remarks>
[Flags]
public enum AloReadOptions
{
    All = 0,
    SkipGeometry = 1
}

/// <summary>How many bones bind one vertex, which the vertex-format string is the only record of.</summary>
public enum AlamoSkinningMode
{
    /// <summary>Rigid: the whole sub-mesh rides its mesh's attachment bone.</summary>
    Static,

    /// <summary>One bone per vertex - the <c>RSkin</c> family.</summary>
    ReducedSkin,

    /// <summary>Four bones per vertex - the <c>B4I4</c> family.</summary>
    FullSkin
}

/// <summary>What a shader parameter holds; the chunk type carries this, not a field.</summary>
public enum AlamoShaderParameterType
{
    Int,
    Float,
    Float3,
    Float4,
    Texture
}

/// <summary>
///     One named value the exporter baked into a sub-mesh for its shader - <c>BaseTexture</c>,
///     <c>Colorization</c>, and so on.
/// </summary>
/// <remarks>
///     Only the member matching <see cref="Type" /> is meaningful; the rest hold their defaults. A
///     flat record rather than a class hierarchy because these are read in bulk, matched by name and
///     never dispatched on.
/// </remarks>
public sealed record AlamoShaderParameter(string Name, AlamoShaderParameterType Type)
{
    public int Int { get; init; }
    public float Float { get; init; }
    public Vector3 Float3 { get; init; }
    public Vector4 Float4 { get; init; }
    public string Texture { get; init; } = string.Empty;
}

/// <summary>The four bone slots a vertex can bind to, as indices into the sub-mesh's skin table.</summary>
/// <remarks>
///     Four fields rather than an array: a model carries tens of thousands of vertices and an array
///     per vertex would allocate one small object each.
/// </remarks>
public readonly record struct AlamoBoneIndices(uint I0, uint I1, uint I2, uint I3);

/// <summary>
///     One vertex, as stored.
/// </summary>
/// <remarks>
///     The file always stores the fat vertex whatever the declared format, so every field is present
///     on every vertex even when the shader binds none of it. Two stored fields are deliberately
///     dropped: UV sets 2 and 3, which were zero across all 102712 vertices sampled from the shipped
///     trees, and the unused <c>float4</c>, which no vertex declaration in the game binds - it does
///     hold data, so it is skipped rather than asserted away.
/// </remarks>
public readonly record struct AlamoVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 TexCoord0,
    Vector2 TexCoord1,
    Vector3 Tangent,
    Vector3 Binormal,
    Vector4 Color,
    AlamoBoneIndices BoneIndices,
    Vector4 BoneWeights);

/// <summary>An axis-aligned box in model space.</summary>
public readonly record struct AlamoBoundingBox(Vector3 Min, Vector3 Max);

/// <summary>
///     One sub-mesh: a shader, its parameters, and the geometry drawn with them.
/// </summary>
/// <param name="SkinBones">
///     Maps a vertex's local bone slots onto the model's bone list. Empty for a static sub-mesh, whose
///     vertices ride the mesh's attachment bone instead.
/// </param>
public sealed record AlamoSubMesh(
    string Shader,
    IReadOnlyList<AlamoShaderParameter> Parameters,
    string VertexFormat,
    AlamoSkinningMode Skinning,
    IReadOnlyList<AlamoVertex> Vertices,
    IReadOnlyList<ushort> Indices,
    IReadOnlyList<int> SkinBones)
{
    /// <summary>
    ///     The texture bound to <paramref name="parameterName" />, or <see langword="null" /> when the
    ///     sub-mesh binds none.
    /// </summary>
    /// <remarks>
    ///     Case-insensitive: the shipped files and mod exporters disagree on the casing of these names,
    ///     and a case-sensitive lookup silently loses a mod's textures.
    /// </remarks>
    public string? Texture(string parameterName)
    {
        foreach (var p in Parameters)
            if (p.Type == AlamoShaderParameterType.Texture &&
                p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                return p.Texture;

        return null;
    }
}

/// <summary>
///     One mesh: a named, bounded group of sub-meshes that the connections block attaches to a bone.
/// </summary>
/// <param name="IsVisible">
///     Whether the mesh starts visible. The file stores this INVERTED, as a hidden flag; it is
///     normalised here so callers never have to remember which way round it is.
/// </param>
/// <param name="Alt">
///     The damage state this mesh belongs to, from an <c>_ALT&lt;n&gt;</c> suffix on its name, or
///     <see langword="null" /> when it carries none and is therefore always drawn.
/// </param>
/// <param name="Lod">Detail level, from a <c>_LOD&lt;n&gt;</c> suffix, on the same terms.</param>
public sealed record AlamoMesh(
    int Index,
    string Name,
    AlamoBoundingBox Bounds,
    bool IsVisible,
    bool IsCollidable,
    int? Alt,
    int? Lod,
    IReadOnlyList<AlamoSubMesh> SubMeshes)
{
    /// <summary>
    ///     The bone this mesh rides, or <c>-1</c> when the connections block attaches it to none.
    /// </summary>
    /// <remarks>
    ///     Set after the fact, because attachment lives in a block that comes after every mesh in the
    ///     file and indexes meshes and lights as one list.
    /// </remarks>
    public int BoneIndex { get; init; } = -1;
}

/// <summary>What kind of light a light object is.</summary>
public enum AlamoLightType
{
    Omni = 0,
    Directional = 1,
    Spot = 2
}

/// <summary>A light baked into the model, attached to a bone like a mesh is.</summary>
public sealed record AlamoLight(
    string Name,
    AlamoLightType Type,
    Vector3 Color,
    float Intensity,
    float FarAttenuationEnd,
    float FarAttenuationStart,
    float HotspotSize,
    float FalloffSize)
{
    /// <inheritdoc cref="AlamoMesh.BoneIndex" />
    public int BoneIndex { get; init; } = -1;
}

/// <summary>
///     An attachment point for something the model file does not itself contain - a particle system
///     or a light field, named rather than embedded.
/// </summary>
/// <remarks>
///     Proxies are how a model carries its effects. The engine strips <c>_ALT</c>/<c>_LOD</c> from the
///     name and loads <c>Data\Art\Models\&lt;name&gt;.alo</c> as a particle system, falling back to a
///     light-field source. Which proxy belongs to which hardpoint is answered through the bone tree:
///     a damage proxy's bone is parented to the bone the hardpoint's <c>Damage_Particles</c> tag names.
/// </remarks>
/// <param name="IsVisible">
///     Whether the proxy starts on. Stored inverted, like the mesh flag, and normalised here.
/// </param>
/// <param name="AltDecreaseStayHidden">
///     The proxy stays hidden while the ALT level is <em>decreasing</em>. This is the asymmetry that
///     keeps damage effects from switching back on as a unit repairs, so it is not a redundant flag.
/// </param>
public sealed record AlamoProxy(
    string Name,
    int BoneIndex,
    bool IsVisible,
    bool AltDecreaseStayHidden,
    int? Alt,
    int? Lod);

/// <summary>
///     A dazzle: a screen-space glow sprite driven off a bone.
/// </summary>
/// <remarks>
///     Added for Universe at War. NO model in either shipped Star Wars tree carries one - the count
///     field appears on exactly one model out of 2353 - so this is read to spec from
///     <c>Models.cpp</c> and is the one part of the reader no real file exercises. Treat it as
///     unverified if a mod ever produces one.
/// </remarks>
public sealed record AlamoDazzle(
    string Name,
    int BoneIndex,
    Vector3 Color,
    Vector3 Position,
    float Radius,
    string Texture,
    int TextureX,
    int TextureY,
    int TextureSize,
    float Frequency,
    float Phase,
    float Bias,
    bool NightOnly,
    bool IsVisible);

/// <summary>
///     A parsed ALO model: everything the file declares, with nothing resolved against other files.
/// </summary>
/// <remarks>
///     Deliberately a pure description of one file. Texture and shader names are the strings the file
///     holds, not resolved paths; bone references are indices, not links. Resolution belongs to the
///     caller, which is the only layer that knows the mod's layering.
/// </remarks>
public sealed record AlamoModelContent(
    IReadOnlyList<AlamoModelBone> Bones,
    IReadOnlyList<AlamoMesh> Meshes,
    IReadOnlyList<AlamoLight> Lights,
    IReadOnlyList<AlamoProxy> Proxies,
    IReadOnlyList<AlamoDazzle> Dazzles);

/// <summary>
///     An ALO file did not match the format.
/// </summary>
/// <remarks>
///     The reader is strict on purpose. A half-read model renders as a plausible-looking wrong thing,
///     which is far worse for the author than a refusal naming the chunk that did not add up - so
///     every structural expectation is checked rather than tolerated.
/// </remarks>
public sealed class AloFormatException(string message) : Exception(message);
