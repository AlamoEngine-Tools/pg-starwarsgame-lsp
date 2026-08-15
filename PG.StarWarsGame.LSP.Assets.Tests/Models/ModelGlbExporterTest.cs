// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Assets.Models;
using SharpGLTF.Schema2;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AloChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Exporting a parsed model to GLB, checked by reading the GLB back.
/// </summary>
/// <remarks>
///     Every assertion here goes through <see cref="ModelRoot.ReadGLB" /> rather than inspecting the
///     builder, so what is verified is the actual file a client would receive. That also means the
///     library's own validation runs on the way in - a malformed accessor or a non-unit quaternion
///     fails the read rather than reaching the renderer as a silently wrong picture.
/// </remarks>
public sealed class ModelGlbExporterTest
{
    private static ModelRoot Roundtrip(
        byte[] alo, params (string Name, byte[] Ala)[] animations)
    {
        var model = AloModelReader.Read(alo);
        var clips = animations
            .Select(a => (a.Name, AlaAnimationReader.Read(a.Ala)))
            .ToList();

        var glb = ModelGlbExporter.Export(model, clips);
        return ModelRoot.ReadGLB(new MemoryStream(glb), new ReadSettings());
    }

    private static byte[] TriangleSubMesh(
        string shader = "MeshAlpha.fx", string format = "alD3dVertNU2", byte[][]? parameters = null)
    {
        return SubMesh(shader, format,
            [
                MasterVertex(new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector2(0, 0)),
                MasterVertex(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector2(1, 0)),
                MasterVertex(new Vector3(0, 1, 0), new Vector3(0, 0, 1), new Vector2(0, 1))
            ],
            [0, 1, 2], parameters: parameters);
    }

    private static byte[] SimpleModel(params byte[][] extra)
    {
        return Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("HULL_BONE", 0, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Concat(extra),
            Connections(connections: [Connection(0, 1)]));
    }

    /// <summary>
    ///     A shadow volume as the tool pipeline authors one: two faces sharing an edge, plus the
    ///     degenerate quad laid along that edge.
    /// </summary>
    /// <remarks>
    ///     The quad has zero area as authored - both of its triangles have two corners on the same
    ///     position - and its two halves carry the normals of the two faces it joins. That is the
    ///     whole mechanism: the shader pushes the vertices facing away from the light, one half
    ///     moves and the other does not, and the quad opens into the wall that closes the volume.
    ///     Its four corners are corners of the two real faces as well, which is what lets the
    ///     export place them again after the mesh builder has welded everything.
    /// </remarks>
    private static byte[] ShadowVolumeSubMesh()
    {
        var toward = new Vector3(0, 0, 1);
        var away = new Vector3(0, 0, -1);
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(1, 0, 0);

        return SubMesh("MeshShadowVolume.fx", "alD3dVertNU2",
            [
                // The face towards the light, walking the shared edge a to b.
                MasterVertex(a, toward, new Vector2(0, 0)),
                MasterVertex(b, toward, new Vector2(1, 0)),
                MasterVertex(new Vector3(0, 1, 0), toward, new Vector2(0, 1)),

                // The face away from it, walking the same edge b to a.
                MasterVertex(b, away, new Vector2(1, 0)),
                MasterVertex(a, away, new Vector2(0, 0)),
                MasterVertex(new Vector3(0, -1, 0), away, new Vector2(0, 1))
            ],
            [
                0, 1, 2,
                3, 4, 5,

                // The quad over edge a-b: both halves of each end, one per adjoining face.
                0, 1, 3,
                0, 3, 4
            ]);
    }

    private static byte[] ShadowVolumeModel()
    {
        return Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("HULL_BONE", 0, true, Translation(0, 0, 0))),
            Mesh("SHADOW", [ShadowVolumeSubMesh()]),
            Connections(connections: [Connection(0, 1)]));
    }

    [Fact]
    public void Export_KeepsTheDegenerateEdgeQuadsAShadowVolumeIsMadeOf()
    {
        // Measured on EV_LambdaShuttle.ALO: 505 of its body volume's 717 faces are these quads, and
        // every one was being dropped - SharpGLTF's mesh builder discards a zero-area triangle. The
        // volume then tears open along every silhouette edge the moment it extrudes, the stencil
        // count never closes, and the model reads as sitting inside its own shadow.
        var gltf = Roundtrip(ShadowVolumeModel());

        var primitive = Assert.Single(Assert.Single(gltf.LogicalMeshes).Primitives);

        Assert.Equal(4, primitive.GetTriangleIndices().Count());
    }

    [Fact]
    public void Export_KeepsBothNormalsOnACoincidentEdgeQuadCorner()
    {
        // Welding the two halves together would be as fatal as dropping the triangle: a wall can
        // only open while its two halves are still separate vertices.
        var gltf = Roundtrip(ShadowVolumeModel());

        var primitive = Assert.Single(Assert.Single(gltf.LogicalMeshes).Primitives);
        var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
        var normals = primitive.GetVertexAccessor("NORMAL").AsVector3Array();

        var atB = Enumerable.Range(0, positions.Count)
            .Where(at => Math.Abs(positions[at].X - 1) < 1e-4)
            .Select(at => normals[at])
            .ToList();

        Assert.Equal(2, atB.Distinct().Count());
    }

    [Fact]
    public void Export_LeavesAMeshWithNothingToRestoreAlone()
    {
        // The restore only rewrites an index accessor that lost something. A mesh whose faces all
        // have area comes out exactly as the mesh builder made it.
        var gltf = Roundtrip(SimpleModel());

        var primitive = Assert.Single(Assert.Single(gltf.LogicalMeshes).Primitives);

        Assert.Equal(1, primitive.GetTriangleIndices().Count());
    }

    // ── structure ─────────────────────────────────────────────────────────────

    [Fact]
    public void Export_ProducesAReadableGlb()
    {
        var gltf = Roundtrip(SimpleModel());

        Assert.NotEmpty(gltf.LogicalMeshes);
        Assert.Single(gltf.LogicalScenes);
    }

    [Fact]
    public void Export_KeepsTheBoneHierarchy()
    {
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("CHILD", 0, true, Translation(0, 0, 0)),
                Bone("GRANDCHILD", 1, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var gltf = Roundtrip(alo);

        var child = gltf.LogicalNodes.Single(n => n.Name == "CHILD#1");
        var grandchild = gltf.LogicalNodes.Single(n => n.Name == "GRANDCHILD#2");

        Assert.Equal(child, grandchild.VisualParent);
        Assert.Equal("ROOT#0", child.VisualParent!.Name);
    }

    [Fact]
    public void Export_DisambiguatesRepeatedBoneNames()
    {
        // A hull carries many bones called p_hp_imperial_damage; glTF node names must still be
        // distinguishable, without losing the name the XML references.
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("p_hp_imperial_damage", 0, true, Translation(0, 0, 0)),
                Bone("p_hp_imperial_damage", 0, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var gltf = Roundtrip(alo);

        Assert.Contains(gltf.LogicalNodes, n => n.Name == "p_hp_imperial_damage#1");
        Assert.Contains(gltf.LogicalNodes, n => n.Name == "p_hp_imperial_damage#2");
    }

    [Fact]
    public void Export_ConvertsZUpToYUp()
    {
        // Alamo is Z-up; glTF is Y-up. A bone one unit "up" in Alamo must land one unit up in glTF,
        // which is +Y. Getting this wrong lays every model on its side.
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("UP", 0, true, Translation(0, 0, 1))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var gltf = Roundtrip(alo);
        var up = gltf.LogicalNodes.Single(n => n.Name == "UP#1");

        var world = up.WorldMatrix.Translation;
        Assert.True(world.Y > 0.99f, $"expected +Y up, got {world}");
        Assert.True(MathF.Abs(world.Z) < 0.01f, $"expected no Z component, got {world}");
    }

    // ── geometry ──────────────────────────────────────────────────────────────

    [Fact]
    public void Export_WritesPositionsNormalsAndBothUvSets()
    {
        // Both UV sets travel: they differ on 96% of sampled shipped vertices, so dropping the second
        // would be a silent data loss rather than a simplification.
        var gltf = Roundtrip(SimpleModel());

        var primitive = gltf.LogicalMeshes[0].Primitives[0];

        Assert.NotNull(primitive.GetVertexAccessor("POSITION"));
        Assert.NotNull(primitive.GetVertexAccessor("NORMAL"));
        Assert.NotNull(primitive.GetVertexAccessor("TEXCOORD_0"));
        Assert.NotNull(primitive.GetVertexAccessor("TEXCOORD_1"));
    }

    [Fact]
    public void Export_OmitsTangentsForAFormatThatDoesNotBindThem()
    {
        // 69% of sampled shipped vertices have an all-zero tangent because their format never binds
        // one. Writing that zero produces a degenerate TBN the validator rejects.
        var gltf = Roundtrip(SimpleModel());

        Assert.Null(gltf.LogicalMeshes[0].Primitives[0].GetVertexAccessor("TANGENT"));
    }

    [Fact]
    public void Export_WritesTangentsForABumpVertexFormat()
    {
        var alo = Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Mesh("HULL", [SubMesh("MeshBumpColorize.fx", "alD3dVertNU2U3U3",
                [
                    MasterVertex(new Vector3(0, 0, 0), new Vector3(0, 0, 1),
                        tangent: new Vector3(1, 0, 0), binormal: new Vector3(0, 1, 0)),
                    MasterVertex(new Vector3(1, 0, 0), new Vector3(0, 0, 1),
                        tangent: new Vector3(1, 0, 0), binormal: new Vector3(0, 1, 0)),
                    MasterVertex(new Vector3(0, 1, 0), new Vector3(0, 0, 1),
                        tangent: new Vector3(1, 0, 0), binormal: new Vector3(0, 1, 0))
                ],
                [0, 1, 2])]),
            Connections());

        var gltf = Roundtrip(alo);

        Assert.NotNull(gltf.LogicalMeshes[0].Primitives[0].GetVertexAccessor("TANGENT"));
    }

    [Fact]
    public void Export_GivesEachSubMeshItsOwnGlbMesh()
    {
        // Per sub-mesh, because sub-meshes of one mesh may differ in tangents and skinning, and a
        // mesh builder is one vertex type throughout.
        var alo = Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh("MeshAlpha.fx"), TriangleSubMesh("MeshAdditive.fx")]),
            Connections());

        Assert.Equal(2, Roundtrip(alo).LogicalMeshes.Count);
    }

    [Fact]
    public void Export_SkipsAnEmptySubMeshRatherThanEmittingADegenerateMesh()
    {
        var alo = Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Mesh("EMPTY", [SubMesh("MeshAlpha.fx", "alD3dVertNU2", [], [])]),
            Connections());

        Assert.Empty(Roundtrip(alo).LogicalMeshes);
    }

    // ── skinning ──────────────────────────────────────────────────────────────

    [Fact]
    public void Export_BuildsASkinForASkinnedSubMesh()
    {
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("J0", 0, true, Translation(0, 0, 0)),
                Bone("J1", 0, true, Translation(1, 0, 0))),
            Mesh("BODY", [SubMesh("RSkinAlpha.fx", "alD3dVertRSkinNU2",
                [
                    MasterVertex(Vector3.Zero, boneIndices: [0, 0, 0, 0], boneWeights: [1, 0, 0, 0]),
                    MasterVertex(Vector3.UnitX, boneIndices: [1, 0, 0, 0], boneWeights: [1, 0, 0, 0]),
                    MasterVertex(Vector3.UnitY, boneIndices: [0, 0, 0, 0], boneWeights: [1, 0, 0, 0])
                ],
                [0, 1, 2], skinBones: [1, 2])]),
            Connections());

        var gltf = Roundtrip(alo);

        var skinned = gltf.LogicalNodes.Single(n => n.Skin is not null);
        Assert.Equal(2, skinned.Skin!.JointsCount);
        Assert.Equal(["J0#1", "J1#2"],
            Enumerable.Range(0, skinned.Skin.JointsCount).Select(i => skinned.Skin.GetJoint(i).Joint.Name));
    }

    [Fact]
    public void Export_AttachesAStaticSubMeshToItsOwnBoneWithoutASkin()
    {
        var gltf = Roundtrip(SimpleModel());

        Assert.All(gltf.LogicalNodes, n => Assert.Null(n.Skin));
        Assert.Contains(gltf.LogicalNodes,
            n => n.Name == "HULL_BONE#1" && n.Mesh is not null);
    }

    // ── materials ─────────────────────────────────────────────────────────────

    [Fact]
    public void Export_CarriesEachBonesBillboardModeInItsNodeExtras()
    {
        // glTF has no notion of a billboard, and the client cannot recover the mode any other way -
        // it is a per-bone field of the 0x206 chunk and nothing else in the file implies it.
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("CARD", 0, true, Translation(0, 0, 0), (int)AlamoBillboardType.ZAxisView),
                Bone("PLAIN", 0, true, Translation(0, 0, 0), (int)AlamoBillboardType.Disable)),
            Mesh("CARD", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var gltf = Roundtrip(alo);

        Assert.Equal("ZAxisView", gltf.LogicalNodes.Single(n => n.Name == "CARD#1")
            .Extras!["alamoBillboard"]!.GetValue<string>());

        // Nothing at all on the ordinary bones, which is all but 268 of the 22866 in the shipped
        // models - writing "Disable" on every one of them would be pure weight in every file.
        Assert.Null(gltf.LogicalNodes.Single(n => n.Name == "PLAIN#2").Extras);
        Assert.Null(gltf.LogicalNodes.Single(n => n.Name == "ROOT#0").Extras);
    }

    [Fact]
    public void Export_CarriesTheShaderNameAndParametersInMaterialExtras()
    {
        // glTF has no equivalent of an Alamo shader binding, so the client reconstructs the material
        // from these. Anything lost here cannot be recovered downstream.
        var alo = Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh("MeshBumpColorize.fx", parameters:
            [
                TextureParam("BaseTexture", "AI_Rancor.tga"),
                Float4Param("Colorization", new Vector4(1, 0, 0, 1)),
                FloatParam("Emissive", 0.25f),
                IntParam("BlendMode", 3)
            ])]),
            Connections());

        var extras = Roundtrip(alo).LogicalMaterials[0].Extras;

        Assert.Equal("MeshBumpColorize.fx", extras!["alamoShader"]!.GetValue<string>());
        Assert.Equal("HULL", extras["alamoMesh"]!.GetValue<string>());
        Assert.Equal("AI_Rancor.tga", extras["param:BaseTexture"]!.GetValue<string>());
        Assert.Equal(0.25f, extras["param:Emissive"]!.GetValue<float>());
        Assert.Equal(3, extras["param:BlendMode"]!.GetValue<int>());
        Assert.Equal([1f, 0f, 0f, 1f],
            extras["param:Colorization"]!.AsArray().Select(v => v!.GetValue<float>()));
    }

    [Fact]
    public void Export_CarriesTheMeshIndexSoTheClientCanJoinBackToTheFile()
    {
        // The inspector reads mesh BOUNDS from the server, which knows meshes by index. Joining on
        // the name instead would be ambiguous the moment a model carries two meshes with one name -
        // and it would be ambiguous SILENTLY, showing one mesh's bounding box against another.
        var alo = Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections([Connection(0, 0), Connection(1, 0)]));

        var materials = Roundtrip(alo).LogicalMaterials;

        Assert.Equal(0, materials[0].Extras!["alamoMeshIndex"]!.GetValue<int>());
        Assert.Equal(1, materials[1].Extras!["alamoMeshIndex"]!.GetValue<int>());
    }

    [Fact]
    public void Export_CarriesTheSubMeshIndexBesideTheMeshIndex()
    {
        // One glTF mesh per ALO SUB-mesh, so a material describes a sub-mesh and the pair of indices
        // is what names it in the file. The sub-mesh index is also in the node name, but reading it
        // back out of a string is exactly the kind of parsing that has already gone wrong here once.
        var alo = Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh("MeshBump.fx"), TriangleSubMesh("MeshAlpha.fx")]),
            Connections());

        var materials = Roundtrip(alo).LogicalMaterials;

        Assert.Equal(0, materials[0].Extras!["alamoSubMeshIndex"]!.GetValue<int>());
        Assert.Equal(1, materials[1].Extras!["alamoSubMeshIndex"]!.GetValue<int>());
        Assert.Equal(0, materials[1].Extras!["alamoMeshIndex"]!.GetValue<int>());
    }

    [Fact]
    public void Export_CarriesAltAndLodSoDamageStatesSurviveTheTrip()
    {
        // ALT and LOD live in the mesh NAME and gate visibility at runtime; without them the client
        // cannot implement damage states at all.
        var alo = Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Mesh("ENGINE_ALT2_LOD1", [TriangleSubMesh()]),
            Connections());

        var extras = Roundtrip(alo).LogicalMaterials[0].Extras;

        Assert.Equal(2, extras!["alamoAlt"]!.GetValue<int>());
        Assert.Equal(1, extras["alamoLod"]!.GetValue<int>());
    }

    // ── animation ─────────────────────────────────────────────────────────────

    [Fact]
    public void Export_WritesAnAnimationAsANamedClip()
    {
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("TURRET", 0, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var ala = AlaChunkFixture.AnimationV1(3, 30f,
            AlaChunkFixture.BoneV1("TURRET", 1,
                translationOffset: new Vector3(1, 0, 0),
                translationScale: new Vector3(1, 0, 0),
                translationSamples:
                [
                    AlaChunkFixture.PackedVector(0, 0, 0),
                    AlaChunkFixture.PackedVector(1, 0, 0),
                    AlaChunkFixture.PackedVector(2, 0, 0)
                ]));

        var gltf = Roundtrip(alo, ("turret_spin", ala));

        var clip = Assert.Single(gltf.LogicalAnimations);
        Assert.Equal("turret_spin", clip.Name);

        // Three frames at 30fps: the last sample sits at 2/30s, and the duplicate final frame is what
        // makes the loop seamless rather than something to trim.
        Assert.Equal(2f / 30f, clip.Duration, 4);
    }

    [Fact]
    public void Export_IgnoresAnAnimationTrackNamingADifferentBone()
    {
        // 50 shipped animations sit beside a model they were not authored against. The bone must keep
        // its rest pose rather than be driven by another skeleton's motion.
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("TURRET", 0, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var ala = AlaChunkFixture.AnimationV1(2, 30f,
            AlaChunkFixture.BoneV1("SOMETHING_ELSE", 1,
                translationSamples:
                    [AlaChunkFixture.PackedVector(0, 0, 0), AlaChunkFixture.PackedVector(1, 0, 0)]));

        var gltf = Roundtrip(alo, ("mismatched", ala));

        Assert.Empty(gltf.LogicalAnimations.SelectMany(a => a.Channels));
    }

    // ── visibility ────────────────────────────────────────────────────────────

    /// <summary>
    ///     A model whose turret bone can be hidden, with one animation that hides it.
    /// </summary>
    private static (byte[] Alo, byte[] Ala) HidingTurret(params bool[] visibility)
    {
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("TURRET", 0, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var ala = AlaChunkFixture.AnimationV1(visibility.Length, 30f,
            AlaChunkFixture.BoneV1("TURRET", 1, visibility: visibility));

        return (alo, ala);
    }

    private static JsonNode? Visibility(Animation clip)
    {
        return (clip.Extras as JsonObject)?["alamoVisibility"];
    }

    /// <summary>
    ///     The visibility track was read and then dropped on the floor.
    /// </summary>
    /// <remarks>
    ///     662 of the 1363 shipped animations hide at least one bone, across 4052 bone tracks, and
    ///     2285 of those tracks are particle proxies - so the dominant use is timing an effect to the
    ///     motion. The rancor's death explosion hangs off <c>P_ATST_Die</c>, hidden for all 61 frames
    ///     of <c>attack_00</c> and visible for 35 of the 61 frames of <c>die_00</c>. Dropping the
    ///     track meant every one of those fired from the first frame of every clip.
    /// </remarks>
    [Fact]
    public void Export_RecordsABoneHiddenByAnAnimation()
    {
        var (alo, ala) = HidingTurret(false, false, true, true);

        var gltf = Roundtrip(alo, ("die", ala));

        var track = Visibility(Assert.Single(gltf.LogicalAnimations));
        Assert.NotNull(track);
        Assert.Equal(30f, (float)track!["fps"]!, 3);
        Assert.Equal("0011", (string)track["bones"]!["TURRET#1"]!);
    }

    /// <summary>
    ///     glTF has no visibility channel, so this rides as extras and every reader that does not
    ///     know about it still gets a valid file with correct motion.
    /// </summary>
    [Fact]
    public void Export_LeavesTheClipItselfValidAndUnchanged()
    {
        var (alo, ala) = HidingTurret(false, true);

        var gltf = Roundtrip(alo, ("die", ala));

        Assert.Equal("die", Assert.Single(gltf.LogicalAnimations).Name);
    }

    /// <summary>
    ///     Writing a track of all-ones would put an extras object on most animations to say nothing:
    ///     701 of the 1363 shipped clips hide no bone at all.
    /// </summary>
    [Fact]
    public void Export_WritesNothingWhenNoBoneIsEverHidden()
    {
        var (alo, ala) = HidingTurret(true, true, true);

        var gltf = Roundtrip(alo, ("idle", ala));

        Assert.Null(Visibility(Assert.Single(gltf.LogicalAnimations)));
    }

    /// <summary>
    ///     Same rule as the motion tracks: a track naming a bone this skeleton does not have is an
    ///     animation authored against a different model, and must not reach into this one.
    /// </summary>
    [Fact]
    public void Export_IgnoresAVisibilityTrackNamingADifferentBone()
    {
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("TURRET", 0, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var ala = AlaChunkFixture.AnimationV1(2, 30f,
            AlaChunkFixture.BoneV1("SOMETHING_ELSE", 1, visibility: [false, true]));

        var gltf = Roundtrip(alo, ("mismatched", ala));

        // With nothing left to drive, the clip is not written at all - which is the same outcome as
        // an ignored motion track, and equally correct.
        Assert.DoesNotContain(gltf.LogicalAnimations, clip => Visibility(clip) is not null);
    }

    /// <summary>
    ///     One entry per bone that hides, and none for the bones that do not.
    /// </summary>
    [Fact]
    public void Export_RecordsOnlyTheBonesThatHide()
    {
        var alo = Concat(
            Skeleton(
                Bone("ROOT", -1, true, Translation(0, 0, 0)),
                Bone("TURRET", 0, true, Translation(0, 0, 0)),
                Bone("MUZZLE", 1, true, Translation(0, 0, 0))),
            Mesh("HULL", [TriangleSubMesh()]),
            Connections(connections: [Connection(0, 1)]));

        var ala = AlaChunkFixture.AnimationV1(2, 30f,
            AlaChunkFixture.BoneV1("TURRET", 1, visibility: [true, true]),
            AlaChunkFixture.BoneV1("MUZZLE", 2, visibility: [true, false]));

        var gltf = Roundtrip(alo, ("fire", ala));

        var bones = Visibility(Assert.Single(gltf.LogicalAnimations))!["bones"]!.AsObject();
        Assert.Equal(["MUZZLE#2"], bones.Select(pair => pair.Key));
    }
}
