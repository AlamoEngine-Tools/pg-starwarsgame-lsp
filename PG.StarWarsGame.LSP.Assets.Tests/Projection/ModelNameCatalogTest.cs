// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

// ModelNameCatalog is the stable seam that unions a model's skeleton bones with its mesh names, since
// the engine resolves any bone reference against either. Bones are supplied by the caller (the ALO
// loader); mesh names are recovered by the deprecated AloMeshNameReader shim from the same bytes.
public sealed class ModelNameCatalogTest
{
    private const uint Mesh = 0x0400;
    private const uint MeshName = 0x0401;
    private const uint ContainerBit = 0x80000000;

    [Fact]
    public void ReadBoneReferenceTargets_UnionsBonesAndMeshNames()
    {
        var alo = MeshChunk("HP02_LC_Blast");

        var result = ModelNameCatalog.ReadBoneReferenceTargets(alo, _ => ["Root", "HP02_LC_Bone"]);

        // The bone list plus the mesh-only decal name the loader never exposed.
        Assert.Equal(["Root", "HP02_LC_Bone", "HP02_LC_Blast"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_KeepsBonesFirstAndInOrder()
    {
        var alo = Concat(MeshChunk("mesh_a"), MeshChunk("mesh_b"));

        var result = ModelNameCatalog.ReadBoneReferenceTargets(alo, _ => ["bone_1", "bone_2"]);

        Assert.Equal(["bone_1", "bone_2", "mesh_a", "mesh_b"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_DropsMeshNamesAlreadyPresentAsBones_CaseInsensitively()
    {
        // On L1 station models the blast decal exists as BOTH a bone and a mesh; it must appear once.
        var alo = Concat(MeshChunk("HP01_SHG_Blast"), MeshChunk("collision_com"));

        var result = ModelNameCatalog.ReadBoneReferenceTargets(alo,
            _ => ["Root", "hp01_shg_blast"]);

        Assert.Equal(["Root", "hp01_shg_blast", "collision_com"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_DeduplicatesRepeatedMeshNames()
    {
        var alo = Concat(MeshChunk("dup"), MeshChunk("dup"));

        var result = ModelNameCatalog.ReadBoneReferenceTargets(alo, _ => ["root"]);

        Assert.Equal(["root", "dup"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_PreservesDuplicateBones()
    {
        // Skeletons legitimately repeat bone names (e.g. many p_u_light_throb emitters); the bone list
        // is passed through verbatim - only mesh additions are de-duplicated.
        var result = ModelNameCatalog.ReadBoneReferenceTargets([],
            _ => ["p_u_light_throb", "p_u_light_throb"]);

        Assert.Equal(["p_u_light_throb", "p_u_light_throb"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_NoMeshes_ReturnsBonesOnly()
    {
        var result = ModelNameCatalog.ReadBoneReferenceTargets([], _ => ["Root", "Turret"]);

        Assert.Equal(["Root", "Turret"], result);
    }

    // ── fixtures ──────────────────────────────────────────────────────────────

    private static byte[] MeshChunk(string meshName)
    {
        var name = Encoding.ASCII.GetBytes(meshName);
        var nameChunk = Chunk(MeshName, false, [.. name, 0x00]);
        return Chunk(Mesh, true, nameChunk);
    }

    private static byte[] Chunk(uint type, bool container, byte[] body)
    {
        var size = (uint)body.Length | (container ? ContainerBit : 0u);
        return [.. BitConverter.GetBytes(type), .. BitConverter.GetBytes(size), .. body];
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new List<byte>();
        foreach (var p in parts) result.AddRange(p);
        return [.. result];
    }
}
