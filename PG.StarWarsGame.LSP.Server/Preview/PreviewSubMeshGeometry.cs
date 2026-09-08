// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     One page of one sub-mesh's bulk geometry: its vertices, its faces, or its bone mapping.
/// </summary>
/// <remarks>
///     <para>
///         Paged, and one table at a time, because this is the only genuinely large thing the
///         preview can be asked for - a single Star Destroyer sub-mesh is 3814 triangles. Nobody
///         scrolls thousands of rows; they look up a handful ("are these normals zero?", "which
///         bones does this vertex bind to?"), so a page with the total beside it answers the
///         question without ever putting the whole buffer on the wire.
///     </para>
///     <para>
///         Nothing here is derived. The values are the ones the file holds, in the file's own axes -
///         which is the point of the panel: an author comparing the preview against their exporter
///         needs the numbers they wrote, not the ones three.js ended up with.
///     </para>
/// </remarks>
public sealed record PreviewSubMeshGeometry(
    string Model,
    int MeshIndex,
    int SubMeshIndex,
    string Table,
    int Offset,
    int TotalVertices,
    int TotalFaces,
    IReadOnlyList<PreviewVertexRow> Vertices,
    IReadOnlyList<PreviewFaceRow> Faces,
    IReadOnlyList<PreviewBoneMappingRow> BoneMapping)
{
    /// <summary>
    ///     The most rows one request can return.
    /// </summary>
    /// <remarks>
    ///     A cap rather than a suggestion: the page size arrives from the client, and a panel asking
    ///     for a million rows would serialise the whole buffer through JSON-RPC. Large enough that a
    ///     reader paging by hand rarely reaches the end of one.
    /// </remarks>
    public const int MaxPage = 500;

    /// <summary>Reads one page out of a parsed model.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The mesh or sub-mesh does not exist. Refused by name rather than clamped: silently
    ///     answering about a different sub-mesh is the one outcome an inspector must never have.
    /// </exception>
    public static PreviewSubMeshGeometry From(
        string model, AlamoModelContent content, int meshIndex, int subMeshIndex, string table,
        int offset, int count)
    {
        if (meshIndex < 0 || meshIndex >= content.Meshes.Count)
            throw new ArgumentOutOfRangeException(nameof(meshIndex),
                $"Model '{model}' has {content.Meshes.Count} meshes; {meshIndex} is not one of them.");

        var mesh = content.Meshes[meshIndex];

        if (subMeshIndex < 0 || subMeshIndex >= mesh.SubMeshes.Count)
            throw new ArgumentOutOfRangeException(nameof(subMeshIndex),
                $"Mesh '{mesh.Name}' has {mesh.SubMeshes.Count} sub-meshes; " +
                $"{subMeshIndex} is not one of them.");

        var sub = mesh.SubMeshes[subMeshIndex];
        var totalFaces = sub.Indices.Count / 3;
        var page = Math.Clamp(count, 0, MaxPage);
        var start = Math.Max(0, offset);

        // Only the table that was asked for. Filling the others as well would put the whole buffer
        // back on the wire and defeat the paging entirely.
        return new PreviewSubMeshGeometry(
            model, meshIndex, subMeshIndex, table, start,
            sub.Vertices.Count, totalFaces,
            table == "vertices" ? VertexPage(sub, start, page) : [],
            table == "faces" ? FacePage(sub, start, page) : [],
            table == "boneMapping" ? BoneMappingRows(sub, content) : []);
    }

    private static IReadOnlyList<PreviewVertexRow> VertexPage(
        AlamoSubMesh sub, int offset, int count)
    {
        return
        [
            .. sub.Vertices.Skip(offset).Take(count).Select((vertex, at) => new PreviewVertexRow(
                // The vertex's REAL index, not its place in the page: a face row names it by that
                // number, and an author cross-referencing the two tables has to see the same one.
                offset + at,
                Floats(vertex.Position),
                Floats(vertex.Normal),
                [vertex.TexCoord0.X, vertex.TexCoord0.Y],
                [vertex.TexCoord1.X, vertex.TexCoord1.Y],
                Floats(vertex.Tangent),
                Floats(vertex.Binormal),
                [vertex.Color.X, vertex.Color.Y, vertex.Color.Z, vertex.Color.W],
                [
                    (int)vertex.BoneIndices.I0, (int)vertex.BoneIndices.I1,
                    (int)vertex.BoneIndices.I2, (int)vertex.BoneIndices.I3,
                ],
                [
                    vertex.BoneWeights.X, vertex.BoneWeights.Y,
                    vertex.BoneWeights.Z, vertex.BoneWeights.W,
                ])),
        ];
    }

    private static IReadOnlyList<PreviewFaceRow> FacePage(AlamoSubMesh sub, int offset, int count)
    {
        var faces = new List<PreviewFaceRow>();
        var total = sub.Indices.Count / 3;

        for (var face = offset; face < Math.Min(total, offset + count); face++)
            faces.Add(new PreviewFaceRow(
                face, sub.Indices[face * 3], sub.Indices[face * 3 + 1], sub.Indices[face * 3 + 2]));

        return faces;
    }

    /// <summary>
    ///     The sub-mesh's skin table, with each model bone named.
    /// </summary>
    /// <remarks>
    ///     Never paged: the longest skin table in the shipped corpus is a fraction of one page, and
    ///     it is the table a reader needs whole - a vertex names a local SLOT, and this is the only
    ///     thing that says which bone of the model that slot actually is.
    /// </remarks>
    private static IReadOnlyList<PreviewBoneMappingRow> BoneMappingRows(
        AlamoSubMesh sub, AlamoModelContent content)
    {
        return
        [
            .. sub.SkinBones.Select((bone, slot) => new PreviewBoneMappingRow(
                slot,
                bone,
                bone >= 0 && bone < content.Bones.Count
                    ? content.Bones[bone].Name

                    // Said plainly rather than thrown: a mod's own exporter can write anything here,
                    // and a slot pointing nowhere is precisely the fault worth surfacing.
                    : $"(no such bone: {bone})")),
        ];
    }

    private static IReadOnlyList<float> Floats(Vector3 v)
    {
        return [v.X, v.Y, v.Z];
    }
}

/// <summary>One vertex, every channel the format can hold.</summary>
/// <remarks>
///     Channels a format does not bind still travel, as the zeros the file stores. Tangents are
///     all-zero on 69% of shipped vertices because only the <c>U3U3</c> formats bind them - and
///     seeing that zero is how an author confirms it, so it is not filtered out here.
/// </remarks>
public sealed record PreviewVertexRow(
    int Index,
    IReadOnlyList<float> Position,
    IReadOnlyList<float> Normal,
    IReadOnlyList<float> TexCoord0,
    IReadOnlyList<float> TexCoord1,
    IReadOnlyList<float> Tangent,
    IReadOnlyList<float> Binormal,
    IReadOnlyList<float> Color,
    IReadOnlyList<int> BoneIndices,
    IReadOnlyList<float> BoneWeights);

/// <summary>One triangle, as the three vertex indices it draws.</summary>
public sealed record PreviewFaceRow(int Index, int V0, int V1, int V2);

/// <summary>One entry of a sub-mesh's skin table.</summary>
/// <param name="Slot">The local slot a vertex's bone index names.</param>
/// <param name="BoneIndex">The model bone it resolves to.</param>
public sealed record PreviewBoneMappingRow(int Slot, int BoneIndex, string Name);
