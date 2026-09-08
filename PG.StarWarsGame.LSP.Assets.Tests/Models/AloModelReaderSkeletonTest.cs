// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AloChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     The skeleton block (<c>0x200</c>): bone names, the parent chain, and the transform layout.
/// </summary>
/// <remarks>
///     The transform is the load-bearing part and the easiest to get silently wrong. The file stores
///     12 floats as three rows of four - <c>[axis.x axis.y axis.z translation]</c> per row - which is
///     the transpose of the row-vector matrix the engine uses, hence <c>Models.cpp</c>'s
///     <c>relTransform.transpose()</c>. Translation therefore lands in stored floats 3, 7 and 11, and
///     these tests pin that: a layout error that swaps rows for columns still round-trips an identity
///     matrix, so identity proves nothing and every fixture here is deliberately asymmetric.
/// </remarks>
public sealed class AloModelReaderSkeletonTest
{
    [Fact]
    public void Read_Skeleton_ReadsBoneNamesInFileOrder()
    {
        var alo = Skeleton(
            Bone("ROOT", -1, true, Translation(0, 0, 0)),
            Bone("HP_F-L_Bone", 0, true, Translation(1, 2, 3)),
            Bone("HP_F-L_EmitDamage", 1, true, Translation(0, 0, 1)));

        var model = AloModelReader.Read(alo);

        Assert.Equal(["ROOT", "HP_F-L_Bone", "HP_F-L_EmitDamage"],
            model.Bones.Select(b => b.Name));
    }

    [Fact]
    public void Read_Skeleton_LinksParentsByIndexAndMarksRootsWithMinusOne()
    {
        var alo = Skeleton(
            Bone("ROOT", -1, true, Translation(0, 0, 0)),
            Bone("CHILD", 0, true, Translation(0, 0, 0)),
            Bone("GRANDCHILD", 1, true, Translation(0, 0, 0)));

        var model = AloModelReader.Read(alo);

        Assert.Equal([-1, 0, 1], model.Bones.Select(b => b.ParentIndex));
    }

    [Fact]
    public void Read_Skeleton_PutsStoredFloats3And7And11IntoTheTranslation()
    {
        // Asymmetric on purpose: 1/2/3 in the translation slots, and a non-identity basis so a
        // transposed read cannot pass by coincidence.
        float[] stored = [2, 0, 0, 1, 0, 3, 0, 2, 0, 0, 4, 3];

        var alo = Skeleton(Bone("ROOT", -1, true, stored));

        var bone = AloModelReader.Read(alo).Bones[0];

        Assert.Equal(new Vector3(1, 2, 3), bone.RelativeTransform.Translation);
        Assert.Equal(2f, bone.RelativeTransform.M11);
        Assert.Equal(3f, bone.RelativeTransform.M22);
        Assert.Equal(4f, bone.RelativeTransform.M33);
    }

    [Fact]
    public void Read_Skeleton_ComposesAbsoluteTransformThroughTheParentChain()
    {
        var alo = Skeleton(
            Bone("ROOT", -1, true, Translation(10, 0, 0)),
            Bone("CHILD", 0, true, Translation(0, 5, 0)),
            Bone("GRANDCHILD", 1, true, Translation(0, 0, 2)));

        var bones = AloModelReader.Read(alo).Bones;

        Assert.Equal(new Vector3(10, 0, 0), bones[0].AbsoluteTransform.Translation);
        Assert.Equal(new Vector3(10, 5, 0), bones[1].AbsoluteTransform.Translation);
        Assert.Equal(new Vector3(10, 5, 2), bones[2].AbsoluteTransform.Translation);
    }

    [Fact]
    public void Read_Skeleton_ReadsVisibilityFlag()
    {
        var alo = Skeleton(
            Bone("VISIBLE", -1, true, Translation(0, 0, 0)),
            Bone("HIDDEN", 0, false, Translation(0, 0, 0)));

        var bones = AloModelReader.Read(alo).Bones;

        Assert.True(bones[0].Visible);
        Assert.False(bones[1].Visible);
    }

    [Fact]
    public void Read_Skeleton_TakesBillboardTypeFrom0x206AndDefaultsTo0x205AsDisabled()
    {
        var alo = Skeleton(
            Bone("OLD_FORM", -1, true, Translation(0, 0, 0)),
            Bone("FACE_CAMERA", 0, true, Translation(0, 0, 0),
                billboard: (int)AlamoBillboardType.Face));

        var bones = AloModelReader.Read(alo).Bones;

        Assert.Equal(AlamoBillboardType.Disable, bones[0].Billboard);
        Assert.Equal(AlamoBillboardType.Face, bones[1].Billboard);
    }

    [Fact]
    public void Read_Skeleton_RejectsAParentIndexOutsideTheBoneList()
    {
        // The index is file-controlled and used to reach into the bone array. Reading past the end
        // must be a clear refusal, not a half-built skeleton.
        var alo = Skeleton(Bone("ROOT", 7, true, Translation(0, 0, 0)));

        Assert.Throws<AloFormatException>(() => AloModelReader.Read(alo));
    }

    [Fact]
    public void Read_Skeleton_RejectsABoneCountLargerThanTheFile()
    {
        // A crafted count must not drive a huge allocation before the bones are read.
        var alo = Chunk(0x200, true, Chunk(0x201, false, U32(0x0FFFFFFF), Zeros(124)));

        Assert.Throws<AloFormatException>(() => AloModelReader.Read(alo));
    }
}
