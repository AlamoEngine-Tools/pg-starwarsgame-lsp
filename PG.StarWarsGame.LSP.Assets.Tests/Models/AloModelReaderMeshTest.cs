// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AloChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     The mesh block (<c>0x400</c>) and the sub-mesh material/data pair inside it.
/// </summary>
public sealed class AloModelReaderMeshTest
{
    /// <summary>Every model starts with a skeleton, so every fixture here needs one.</summary>
    private static byte[] Model(params byte[][] meshes)
    {
        return Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Concat(meshes));
    }

    private static byte[] TrivialSubMesh(string shader = "MeshAlpha.fx", string format = "alD3dVertNU2")
    {
        return SubMesh(shader, format,
            [MasterVertex(new Vector3(0, 0, 0)), MasterVertex(new Vector3(1, 0, 0)),
                MasterVertex(new Vector3(0, 1, 0))],
            [0, 1, 2]);
    }

    // ── mesh level ────────────────────────────────────────────────────────────

    [Fact]
    public void Read_Mesh_ReadsNameAndBounds()
    {
        var alo = Model(Mesh("COLLISION", [TrivialSubMesh()],
            min: new Vector3(-1, -2, -3), max: new Vector3(4, 5, 6)));

        var mesh = Assert.Single(AloModelReader.Read(alo).Meshes);

        Assert.Equal("COLLISION", mesh.Name);
        Assert.Equal(new Vector3(-1, -2, -3), mesh.Bounds.Min);
        Assert.Equal(new Vector3(4, 5, 6), mesh.Bounds.Max);
    }

    [Fact]
    public void Read_Mesh_TreatsTheStoredVisibilityFieldAsInverted()
    {
        // The field is a HIDDEN flag: zero means visible. Getting this backwards renders every
        // shipped hull invisible and every hidden helper mesh solid, so it is pinned explicitly.
        var alo = Model(
            Mesh("SHOWN", [TrivialSubMesh()], hidden: 0),
            Mesh("HIDDEN", [TrivialSubMesh()], hidden: 1));

        var meshes = AloModelReader.Read(alo).Meshes;

        Assert.True(meshes[0].IsVisible);
        Assert.False(meshes[1].IsVisible);
    }

    [Fact]
    public void Read_Mesh_ReadsCollidableFlag()
    {
        var alo = Model(
            Mesh("SOLID", [TrivialSubMesh()], collidable: true),
            Mesh("DECOR", [TrivialSubMesh()], collidable: false));

        var meshes = AloModelReader.Read(alo).Meshes;

        Assert.True(meshes[0].IsCollidable);
        Assert.False(meshes[1].IsCollidable);
    }

    [Fact]
    public void Read_Mesh_ReadsSeveralSubMeshesInOrder()
    {
        var alo = Model(Mesh("HULL",
            [TrivialSubMesh("MeshBumpColorize.fx"), TrivialSubMesh("MeshAdditive.fx")]));

        var mesh = Assert.Single(AloModelReader.Read(alo).Meshes);

        Assert.Equal(["MeshBumpColorize.fx", "MeshAdditive.fx"],
            mesh.SubMeshes.Select(s => s.Shader));
    }

    [Theory]
    [InlineData("ENGINE_ALT2", 2, null)]
    [InlineData("ENGINE_LOD1", null, 1)]
    [InlineData("ENGINE_ALT0_LOD3", 0, 3)]
    [InlineData("ENGINE", null, null)]
    public void Read_Mesh_ParsesAltAndLodOutOfTheName(string name, int? alt, int? lod)
    {
        // ALT and LOD are encoded in the NAME, not in any field - that is how the engine switches
        // damage states and detail levels, so the reader has to recover them here.
        var alo = Model(Mesh(name, [TrivialSubMesh()]));

        var mesh = Assert.Single(AloModelReader.Read(alo).Meshes);

        Assert.Equal(alt, mesh.Alt);
        Assert.Equal(lod, mesh.Lod);
    }

    // ── sub-mesh geometry ─────────────────────────────────────────────────────

    [Fact]
    public void Read_SubMesh_ReadsVertexPositionsNormalsUvsAndColour()
    {
        var alo = Model(Mesh("HULL", [SubMesh("MeshAlpha.fx", "alD3dVertNU2C",
            [
                MasterVertex(new Vector3(1, 2, 3), new Vector3(0, 0, 1), new Vector2(0.25f, 0.5f),
                    color: new Vector4(0.1f, 0.2f, 0.3f, 0.4f))
            ],
            [0, 0, 0])]));

        var vertex = Assert.Single(AloModelReader.Read(alo).Meshes[0].SubMeshes[0].Vertices);

        Assert.Equal(new Vector3(1, 2, 3), vertex.Position);
        Assert.Equal(new Vector3(0, 0, 1), vertex.Normal);
        Assert.Equal(new Vector2(0.25f, 0.5f), vertex.TexCoord0);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), vertex.Color);
    }

    [Fact]
    public void Read_SubMesh_ReadsTangentAndBinormal()
    {
        var alo = Model(Mesh("HULL", [SubMesh("MeshBumpColorize.fx", "alD3dVertNU2U3U3",
            [MasterVertex(Vector3.Zero, tangent: new Vector3(1, 0, 0), binormal: new Vector3(0, 1, 0))],
            [0, 0, 0])]));

        var vertex = Assert.Single(AloModelReader.Read(alo).Meshes[0].SubMeshes[0].Vertices);

        Assert.Equal(new Vector3(1, 0, 0), vertex.Tangent);
        Assert.Equal(new Vector3(0, 1, 0), vertex.Binormal);
    }

    [Fact]
    public void Read_SubMesh_ReadsIndicesAsThreePerFace()
    {
        var alo = Model(Mesh("HULL", [SubMesh("MeshAlpha.fx", "alD3dVertNU2",
            [MasterVertex(Vector3.Zero), MasterVertex(Vector3.One), MasterVertex(Vector3.UnitX),
                MasterVertex(Vector3.UnitY)],
            [0, 1, 2, 2, 1, 3])]));

        var subMesh = AloModelReader.Read(alo).Meshes[0].SubMeshes[0];

        Assert.Equal<ushort[]>([0, 1, 2, 2, 1, 3], [.. subMesh.Indices]);
    }

    [Fact]
    public void Read_SubMesh_ReadsTheOlder128ByteVertexFormat()
    {
        // 0x10005 instead of 0x10007: the same fields without the unused float4, still present on 50
        // of the sampled sub-meshes.
        var alo = Model(Mesh("HULL", [SubMesh("MeshAlpha.fx", "alD3dVertNU2",
            [OldVertex(new Vector3(7, 8, 9), new Vector3(0, 1, 0), new Vector2(0.5f, 0.5f))],
            [0, 0, 0], oldVertexFormat: true)]));

        var vertex = Assert.Single(AloModelReader.Read(alo).Meshes[0].SubMeshes[0].Vertices);

        Assert.Equal(new Vector3(7, 8, 9), vertex.Position);
        Assert.Equal(new Vector3(0, 1, 0), vertex.Normal);
        Assert.Equal(new Vector2(0.5f, 0.5f), vertex.TexCoord0);
    }

    [Fact]
    public void Read_SubMesh_ReadsBoneIndicesAndWeights()
    {
        var alo = Model(Mesh("HULL", [SubMesh("RSkinAlpha.fx", "alD3dVertRSkinNU2",
            [MasterVertex(Vector3.Zero, boneIndices: [3, 1, 0, 0], boneWeights: [0.75f, 0.25f, 0, 0])],
            [0, 0, 0])]));

        var vertex = Assert.Single(AloModelReader.Read(alo).Meshes[0].SubMeshes[0].Vertices);

        Assert.Equal(new AlamoBoneIndices(3, 1, 0, 0), vertex.BoneIndices);
        Assert.Equal(new Vector4(0.75f, 0.25f, 0f, 0f), vertex.BoneWeights);
    }

    [Fact]
    public void Read_SubMesh_ReadsTheSkinRemapTable()
    {
        // Bone indices on a skinned vertex are local to the sub-mesh; this table maps them onto the
        // model's bone list. Without it a skinned mesh binds to the wrong bones entirely.
        var alo = Model(Mesh("HULL", [SubMesh("RSkinAlpha.fx", "alD3dVertRSkinNU2",
            [MasterVertex(Vector3.Zero)], [0, 0, 0], skinBones: [4, 9, 2])]));

        var subMesh = AloModelReader.Read(alo).Meshes[0].SubMeshes[0];

        Assert.Equal([4, 9, 2], subMesh.SkinBones);
    }

    [Fact]
    public void Read_SubMesh_SkipsTheCollisionTree()
    {
        var alo = Model(Mesh("HULL", [SubMesh("MeshCollision.fx", "alD3dVertN",
            [MasterVertex(Vector3.Zero)], [0, 0, 0], collisionTree: true)]));

        var subMesh = AloModelReader.Read(alo).Meshes[0].SubMeshes[0];

        Assert.Equal("MeshCollision.fx", subMesh.Shader);
        Assert.Single(subMesh.Vertices);
    }

    [Theory]
    [InlineData("alD3dVertN", AlamoSkinningMode.Static)]
    [InlineData("alD3dVertNU2U3U3C", AlamoSkinningMode.Static)]
    [InlineData("alD3dVertRSkinNU2", AlamoSkinningMode.ReducedSkin)]
    [InlineData("alD3dVertRSkinNU2U3U3", AlamoSkinningMode.ReducedSkin)]
    [InlineData("alD3dVertB4I4NU2", AlamoSkinningMode.FullSkin)]
    [InlineData("alD3dVertB4I4NU2U3U3", AlamoSkinningMode.FullSkin)]
    public void Read_SubMesh_DerivesSkinningModeFromTheVertexFormat(string format, AlamoSkinningMode mode)
    {
        // These six are every format present in the shipped trees. The stored vertex is always the
        // fat one; the format string is the only thing that says how many bones bind a vertex.
        var alo = Model(Mesh("HULL", [TrivialSubMesh(format: format)]));

        var subMesh = AloModelReader.Read(alo).Meshes[0].SubMeshes[0];

        Assert.Equal(format, subMesh.VertexFormat);
        Assert.Equal(mode, subMesh.Skinning);
    }

    // ── shader parameters ─────────────────────────────────────────────────────

    [Fact]
    public void Read_SubMesh_ReadsEveryShaderParameterType()
    {
        var alo = Model(Mesh("HULL", [SubMesh("MeshBumpColorize.fx", "alD3dVertNU2U3U3",
            [MasterVertex(Vector3.Zero)], [0, 0, 0],
            parameters:
            [
                TextureParam("BaseTexture", "AI_Rancor.tga"),
                TextureParam("NormalTexture", "AI_Rancor_B.tga"),
                Float4Param("Colorization", new Vector4(1, 0, 0, 1)),
                Float3Param("DebugColor", new Vector3(0.5f, 0.5f, 0.5f)),
                FloatParam("Emissive", 0.25f),
                IntParam("BlendMode", 3)
            ])]));

        var parameters = AloModelReader.Read(alo).Meshes[0].SubMeshes[0].Parameters;

        Assert.Equal(
            ["BaseTexture", "NormalTexture", "Colorization", "DebugColor", "Emissive", "BlendMode"],
            parameters.Select(p => p.Name));
        Assert.Equal("AI_Rancor.tga", parameters[0].Texture);
        Assert.Equal(new Vector4(1, 0, 0, 1), parameters[2].Float4);
        Assert.Equal(new Vector3(0.5f, 0.5f, 0.5f), parameters[3].Float3);
        Assert.Equal(0.25f, parameters[4].Float);
        Assert.Equal(3, parameters[5].Int);
    }

    [Fact]
    public void Read_SubMesh_ExposesTextureParametersByName()
    {
        // The material layer asks "what is bound to BaseTexture" far more often than it walks the
        // list, and the names are case-inconsistent across mods.
        var alo = Model(Mesh("HULL", [SubMesh("MeshAlpha.fx", "alD3dVertNU2",
            [MasterVertex(Vector3.Zero)], [0, 0, 0],
            parameters: [TextureParam("BaseTexture", "hull.tga")])]));

        var subMesh = AloModelReader.Read(alo).Meshes[0].SubMeshes[0];

        Assert.Equal("hull.tga", subMesh.Texture("basetexture"));
        Assert.Null(subMesh.Texture("NormalTexture"));
    }

    // ── names-only reads ──────────────────────────────────────────────────────

    [Fact]
    public void Read_SkippingGeometry_KeepsStructureButDropsVertexAndIndexData()
    {
        // The name catalog scans every model in a game repository - 2353 files, 1.5 GB - and wants
        // nothing but names. Decoding every vertex for that would be pure waste.
        var alo = Model(Mesh("HULL", [SubMesh("MeshAlpha.fx", "alD3dVertNU2",
            [MasterVertex(Vector3.Zero), MasterVertex(Vector3.One)], [0, 1, 0],
            parameters: [TextureParam("BaseTexture", "hull.tga")])]));

        var model = AloModelReader.Read(alo, AloReadOptions.SkipGeometry);

        var subMesh = model.Meshes[0].SubMeshes[0];
        Assert.Equal("HULL", model.Meshes[0].Name);
        Assert.Equal("MeshAlpha.fx", subMesh.Shader);
        Assert.Equal("hull.tga", subMesh.Texture("BaseTexture"));
        Assert.Empty(subMesh.Vertices);
        Assert.Empty(subMesh.Indices);
    }

    [Fact]
    public void Read_SkippingGeometry_StillRejectsACountTheChunkCannotHold()
    {
        // Skipping the decode must not skip the validation, or a names-only scan would quietly accept
        // files that the real read rejects.
        var alo = Model(Mesh("HULL", [Concat(
            Chunk(0x10100, true, Chunk(0x10101, false, Str("MeshAlpha.fx"))),
            Chunk(0x10000, true,
                Chunk(0x10001, false, U32(9999), U32(1)),
                Chunk(0x10002, false, Str("alD3dVertNU2")),
                Chunk(0x10007, false, MasterVertex(Vector3.Zero)),
                Chunk(0x10004, false, Zeros(6))))]));

        Assert.Throws<AloFormatException>(
            () => AloModelReader.Read(alo, AloReadOptions.SkipGeometry));
    }

    // ── refusals ──────────────────────────────────────────────────────────────

    [Fact]
    public void Read_SubMesh_RejectsAVertexCountTheChunkCannotHold()
    {
        // The declared count drives the read; a count larger than the payload must be refused rather
        // than reading past the chunk into whatever follows.
        var alo = Model(Mesh("HULL", [Concat(
            Chunk(0x10100, true, Chunk(0x10101, false, Str("MeshAlpha.fx"))),
            Chunk(0x10000, true,
                Chunk(0x10001, false, U32(9999), U32(1)),
                Chunk(0x10002, false, Str("alD3dVertNU2")),
                Chunk(0x10007, false, MasterVertex(Vector3.Zero)),
                Chunk(0x10004, false, Zeros(6))))]));

        Assert.Throws<AloFormatException>(() => AloModelReader.Read(alo));
    }

    [Fact]
    public void Read_Mesh_RejectsASubMeshCountThatDoesNotMatchTheBlock()
    {
        var alo = Model(Chunk(0x400, true,
            Chunk(0x401, false, Str("HULL")),
            Chunk(0x402, false,
                I32(4), // claims four sub-meshes
                F32(0, 0, 0), F32(0, 0, 0), I32(0), I32(0), I32(0), Zeros(88)),
            TrivialSubMesh()));

        Assert.Throws<AloFormatException>(() => AloModelReader.Read(alo));
    }
}
