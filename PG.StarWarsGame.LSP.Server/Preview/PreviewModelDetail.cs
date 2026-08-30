// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Everything about a model the client cannot read off the glTF it was sent.
/// </summary>
/// <remarks>
///     <para>
///         The split is by AXIS-DEPENDENCE, not by convenience. Shader names, parameters, vertex
///         formats and the ALT/LOD tags already travel in the glTF material extras and are read
///         there. Bone transforms and mesh bounds cannot: the exporter rotates the whole model
///         Z-up to Y-up and splits doubled-up bone/mesh nodes apart, so every matrix the client
///         holds is one the author has never seen. These come straight off the file instead.
///     </para>
///     <para>
///         Proxies are here for a blunter reason: the exporter does not write them into the glTF at
///         all, so <c>altDecreaseStayHidden</c> - the flag behind the repair asymmetry on 1225
///         shipped proxies - is otherwise unreadable from the client.
///     </para>
/// </remarks>
/// <param name="Model">
///     The model this describes. Carried so a reply that arrives after the preview has changed
///     subject can be recognised and dropped rather than shown against the wrong model.
/// </param>
/// <param name="LightCount">
///     How many lights the file carries, and deliberately nothing else about them. Measured across
///     the shipped corpus: they are 3ds Max target-camera-era export residue - 246 of them in 75 of
///     3340 models, named <c>Spot01</c>/<c>Omni01</c>, and no shipped effect declares a point or
///     spot uniform that could consume one. The count exists so the panel can say they are ignored.
/// </param>
public sealed record PreviewModelDetail(
    string Model,
    IReadOnlyList<PreviewBoneDetail> Bones,
    IReadOnlyList<PreviewMeshDetail> Meshes,
    IReadOnlyList<PreviewProxyDetail> Proxies,
    int LightCount)
{
    /// <summary>Reads one parsed model into the shape the inspector shows.</summary>
    public static PreviewModelDetail From(string model, AlamoModelContent content)
    {
        return new PreviewModelDetail(
            model,
            [.. content.Bones.Select(BoneDetail)],
            [.. content.Meshes.Select(MeshDetail)],
            [.. content.Proxies.Select(ProxyDetail)],
            content.Lights.Count);
    }

    private static PreviewBoneDetail BoneDetail(AlamoModelBone bone)
    {
        return new PreviewBoneDetail(
            bone.Index,
            bone.Name,
            bone.ParentIndex,
            bone.Visible,

            // By NAME. An enum crossing this wire as an ordinal has already cost one bug here, and
            // a billboard mode read as a number silently becomes the wrong mode rather than none.
            bone.Billboard.ToString(),
            Floats(bone.RelativeTransform),
            Floats(bone.AbsoluteTransform));
    }

    private static PreviewMeshDetail MeshDetail(AlamoMesh mesh)
    {
        var subMeshes = mesh.SubMeshes;

        return new PreviewMeshDetail(
            mesh.Index,
            mesh.Name,
            mesh.BoneIndex,
            mesh.IsVisible,
            mesh.IsCollidable,

            // Null, never a defaulted 0: "carries no ALT tag" and "is pinned to ALT 0" are different
            // statements about the file.
            mesh.Alt,
            mesh.Lod,
            [mesh.Bounds.Min.X, mesh.Bounds.Min.Y, mesh.Bounds.Min.Z],
            [mesh.Bounds.Max.X, mesh.Bounds.Max.Y, mesh.Bounds.Max.Z],
            subMeshes.Count,
            subMeshes.Sum(sub => sub.Vertices.Count),
            subMeshes.Sum(sub => sub.Indices.Count) / 3);
    }

    private static PreviewProxyDetail ProxyDetail(AlamoProxy proxy)
    {
        return new PreviewProxyDetail(
            proxy.Name, proxy.BoneIndex, proxy.IsVisible, proxy.AltDecreaseStayHidden,
            proxy.Alt, proxy.Lod);
    }

    /// <summary>
    ///     A matrix as sixteen floats, four rows of four.
    /// </summary>
    /// <remarks>
    ///     Row-major with the translation LAST, which is how <see cref="Matrix4x4" /> stores it and
    ///     how AloViewer's own bone panel lays it out - so an author reading the fourth row sees the
    ///     position they authored rather than a column they have to reassemble.
    /// </remarks>
    private static IReadOnlyList<float> Floats(Matrix4x4 m)
    {
        return
        [
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44,
        ];
    }
}

/// <summary>One bone, in the file's own axes.</summary>
/// <param name="ParentIndex">The parent's index, or <c>-1</c> for a root.</param>
/// <param name="Billboard">The billboard mode by name; <c>Disable</c> for the overwhelming majority.</param>
public sealed record PreviewBoneDetail(
    int Index,
    string Name,
    int ParentIndex,
    bool Visible,
    string Billboard,
    IReadOnlyList<float> RelativeTransform,
    IReadOnlyList<float> AbsoluteTransform);

/// <summary>One mesh, with the bounding box the file stores rather than one measured here.</summary>
/// <param name="BoneIndex">The bone it rides, or <c>-1</c> when the connections block attaches it to none.</param>
public sealed record PreviewMeshDetail(
    int Index,
    string Name,
    int BoneIndex,
    bool Visible,
    bool Collidable,
    int? Alt,
    int? Lod,
    IReadOnlyList<float> BoundsMin,
    IReadOnlyList<float> BoundsMax,
    int SubMeshCount,
    int VertexCount,
    int TriangleCount);

/// <summary>One proxy - a named attachment point that a particle system or another model is attached to.</summary>
/// <param name="AltDecreaseStayHidden">
///     Whether the proxy stays hidden when the damage state is REPAIRED rather than coming back. Set
///     on 1225 proxies across 86 shipped models, so the asymmetry is real rather than theoretical.
/// </param>
public sealed record PreviewProxyDetail(
    string Name,
    int BoneIndex,
    bool Visible,
    bool AltDecreaseStayHidden,
    int? Alt,
    int? Lod);
