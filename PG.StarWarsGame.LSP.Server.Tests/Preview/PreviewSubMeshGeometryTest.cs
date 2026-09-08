// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The per-vertex and per-face tables, paged.
/// </summary>
/// <remarks>
///     Paged rather than sent whole because this is bulk data: one Star Destroyer sub-mesh is 3814
///     triangles, and a table nobody has opened has no business in any load path. One page of one
///     table per request, which is also the shape the panel reads it in.
/// </remarks>
public sealed class PreviewSubMeshGeometryTest
{
    private static AlamoVertex Vertex(int at)
    {
        return new AlamoVertex(
            new Vector3(at, at + 1, at + 2),
            new Vector3(0, 0, 1),
            new Vector2(at / 10f, 0.5f),
            new Vector2(0.25f, 0.75f),
            Vector3.UnitX,
            Vector3.UnitY,
            new Vector4(1, 1, 1, 1),
            new AlamoBoneIndices(0, 1, 2, 3),
            new Vector4(0.5f, 0.5f, 0, 0));
    }

    private static AlamoModelContent Model(int vertices = 8, int faces = 2, int[]? skinBones = null)
    {
        var indices = new List<ushort>();
        for (var face = 0; face < faces; face++)
        {
            indices.Add((ushort)(face * 3));
            indices.Add((ushort)(face * 3 + 1));
            indices.Add((ushort)(face * 3 + 2));
        }

        var sub = new AlamoSubMesh(
            "MeshBump.fx", [], "alD3dVertNU2U3U3", AlamoSkinningMode.ReducedSkin,
            [.. Enumerable.Range(0, vertices).Select(Vertex)], indices, skinBones ?? []);

        var mesh = new AlamoMesh(
            0, "hull", new AlamoBoundingBox(Vector3.Zero, Vector3.One), true, true, null, null,
            [sub]);

        return new AlamoModelContent(
            [
                new AlamoModelBone(0, "Root", -1, true, AlamoBillboardType.Disable,
                    Matrix4x4.Identity, Matrix4x4.Identity),
                new AlamoModelBone(1, "B_Spine", 0, true, AlamoBillboardType.Disable,
                    Matrix4x4.Identity, Matrix4x4.Identity),
            ],
            [mesh], [], [], []);
    }

    [Fact]
    public void Vertices_ComeBackAsAPageWithTheTotalBesideThem()
    {
        var page = PreviewSubMeshGeometry.From("m", Model(vertices: 8), 0, 0, "vertices", 2, 3);

        Assert.Equal(8, page.TotalVertices);
        Assert.Equal(3, page.Vertices.Count);

        // Numbered by their real position, not by their position in the page - the index is what a
        // face row names, so an author matching the two has to see the same number.
        Assert.Equal(2, page.Vertices[0].Index);
        Assert.Equal([2f, 3f, 4f], page.Vertices[0].Position);
    }

    [Fact]
    public void Vertices_CarryEveryChannelTheFormatCanHold()
    {
        var vertex = PreviewSubMeshGeometry.From("m", Model(), 0, 0, "vertices", 0, 1).Vertices[0];

        Assert.Equal([0f, 0f, 1f], vertex.Normal);
        Assert.Equal([0f, 0.5f], vertex.TexCoord0);
        Assert.Equal([0.25f, 0.75f], vertex.TexCoord1);
        Assert.Equal([1f, 0f, 0f], vertex.Tangent);
        Assert.Equal([0f, 1f, 0f], vertex.Binormal);
        Assert.Equal([1f, 1f, 1f, 1f], vertex.Color);
        Assert.Equal([0, 1, 2, 3], vertex.BoneIndices);
        Assert.Equal([0.5f, 0.5f, 0f, 0f], vertex.BoneWeights);
    }

    [Fact]
    public void APageAskingBeyondTheEnd_ComesBackEmptyRatherThanThrowing()
    {
        // The panel can ask for a page that a reload has since made out of range.
        var page = PreviewSubMeshGeometry.From("m", Model(vertices: 4), 0, 0, "vertices", 99, 10);

        Assert.Empty(page.Vertices);
        Assert.Equal(4, page.TotalVertices);
    }

    [Fact]
    public void APageIsCapped_SoOneRequestCannotAskForEverything()
    {
        var page = PreviewSubMeshGeometry.From("m", Model(vertices: 5000), 0, 0, "vertices", 0,
            100_000);

        Assert.Equal(PreviewSubMeshGeometry.MaxPage, page.Vertices.Count);
    }

    [Fact]
    public void Faces_ComeBackAsTriplesOfVertexIndices()
    {
        var page = PreviewSubMeshGeometry.From("m", Model(faces: 2), 0, 0, "faces", 0, 10);

        Assert.Equal(2, page.TotalFaces);
        Assert.Equal(0, page.Faces[0].Index);
        Assert.Equal([0, 1, 2], new[] { page.Faces[0].V0, page.Faces[0].V1, page.Faces[0].V2 });
        Assert.Equal([3, 4, 5], new[] { page.Faces[1].V0, page.Faces[1].V1, page.Faces[1].V2 });
    }

    [Fact]
    public void OnlyTheAskedForTable_IsFilled()
    {
        // A vertex page carrying every face as well would defeat the point of paging at all.
        var vertices = PreviewSubMeshGeometry.From("m", Model(), 0, 0, "vertices", 0, 4);

        Assert.NotEmpty(vertices.Vertices);
        Assert.Empty(vertices.Faces);
    }

    [Fact]
    public void BoneMapping_ResolvesTheLocalSlotToTheModelsOwnBoneName()
    {
        // This is the whole point of the table: a vertex names slot 1, and only the sub-mesh's own
        // skin list says which bone of the model that actually is.
        var page = PreviewSubMeshGeometry.From(
            "m", Model(skinBones: [1, 0]), 0, 0, "boneMapping", 0, 10);

        Assert.Equal(2, page.BoneMapping.Count);
        Assert.Equal(0, page.BoneMapping[0].Slot);
        Assert.Equal(1, page.BoneMapping[0].BoneIndex);
        Assert.Equal("B_Spine", page.BoneMapping[0].Name);
        Assert.Equal("Root", page.BoneMapping[1].Name);
    }

    [Fact]
    public void BoneMapping_NamesAnOutOfRangeSlotRatherThanThrowing()
    {
        // A mod's exporter can write anything; a panel that dies on it is worse than one that says
        // the slot points nowhere.
        var page = PreviewSubMeshGeometry.From(
            "m", Model(skinBones: [42]), 0, 0, "boneMapping", 0, 10);

        Assert.Equal(42, page.BoneMapping[0].BoneIndex);
        Assert.Contains("no such bone", page.BoneMapping[0].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMissingSubMesh_IsRefusedByNameRatherThanReturningTheWrongOne()
    {
        var missing = Assert.Throws<ArgumentOutOfRangeException>(
            () => PreviewSubMeshGeometry.From("m", Model(), 0, 7, "vertices", 0, 10));

        Assert.Contains("subMeshIndex", missing.Message, StringComparison.OrdinalIgnoreCase);
    }
}
