// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Projection;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AloChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

// ModelNameCatalog is the stable seam that unions a model's skeleton bones with its mesh names, since
// the engine resolves any bone reference against either. Bones are supplied by the caller (the ALO
// loader, which exposes none); mesh names come from AloModelReader, read without geometry.
public sealed class ModelNameCatalogTest
{

    [Fact]
    public void ReadBoneReferenceTargets_UnionsBonesAndMeshNames()
    {
        var alo = Model("HP02_LC_Blast");

        var result = ModelNameCatalog.ReadBoneReferenceTargets(alo, _ => ["Root", "HP02_LC_Bone"]);

        // The bone list plus the mesh-only decal name the loader never exposed.
        Assert.Equal(["Root", "HP02_LC_Bone", "HP02_LC_Blast"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_KeepsBonesFirstAndInOrder()
    {
        var alo = Model("mesh_a", "mesh_b");

        var result = ModelNameCatalog.ReadBoneReferenceTargets(alo, _ => ["bone_1", "bone_2"]);

        Assert.Equal(["bone_1", "bone_2", "mesh_a", "mesh_b"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_DropsMeshNamesAlreadyPresentAsBones_CaseInsensitively()
    {
        // On L1 station models the blast decal exists as BOTH a bone and a mesh; it must appear once.
        var alo = Model("HP01_SHG_Blast", "collision_com");

        var result = ModelNameCatalog.ReadBoneReferenceTargets(alo,
            _ => ["Root", "hp01_shg_blast"]);

        Assert.Equal(["Root", "hp01_shg_blast", "collision_com"], result);
    }

    [Fact]
    public void ReadBoneReferenceTargets_DeduplicatesRepeatedMeshNames()
    {
        var alo = Model("dup", "dup");

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

    /// <summary>
    ///     A structurally complete model carrying the named meshes and nothing else.
    /// </summary>
    /// <remarks>
    ///     Whole models rather than loose mesh chunks: the reader behind this seam refuses a buffer
    ///     that is not a real ALO, so a fixture made of bare mesh chunks would test the refusal path
    ///     instead of the union these tests are about. The meshes carry no sub-meshes, which is legal
    ///     and keeps the fixtures to the one thing they exercise - names.
    /// </remarks>
    private static byte[] Model(params string[] meshNames)
    {
        return Concat(
            Skeleton(Bone("ROOT", -1, true, Translation(0, 0, 0))),
            Concat([.. meshNames.Select(n => Mesh(n, []))]),
            Connections());
    }
}
