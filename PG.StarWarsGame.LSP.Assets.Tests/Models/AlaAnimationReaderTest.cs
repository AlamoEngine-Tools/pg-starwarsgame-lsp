// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AlaChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     The <c>.ala</c> animation reader, in both shipped format versions.
/// </summary>
/// <remarks>
///     <para>
///         The reader produces <em>bone-local</em> transforms per frame, deliberately unlike
///         <c>Animations.cpp</c>, which composes each bone through its parent and hands back absolute
///         transforms. glTF animation tracks are bone-local, so composing here and decomposing again
///         in the exporter would be two lossy round trips through matrix decomposition for nothing.
///         Not composing also means the reader needs no model, which keeps it a pure description of
///         one file - the same contract <see cref="AloModelReader" /> holds to.
///     </para>
///     <para>
///         Sampled values are packed. Translation and scale are three raw <c>uint16</c> reconstituted
///         as <c>offset + raw * scale</c> - the raw integer, not a normalised fraction. Rotation is
///         four <c>int16</c> over 32767.
///     </para>
/// </remarks>
public sealed class AlaAnimationReaderTest
{
    private const float Tolerance = 1e-4f;

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"expected {expected}, got {actual}");
    }

    private static void AssertClose(Quaternion expected, Quaternion actual)
    {
        Assert.True(
            Math.Abs(expected.X - actual.X) < Tolerance && Math.Abs(expected.Y - actual.Y) < Tolerance &&
            Math.Abs(expected.Z - actual.Z) < Tolerance && Math.Abs(expected.W - actual.W) < Tolerance,
            $"expected {expected}, got {actual}");
    }

    // ── header ────────────────────────────────────────────────────────────────

    [Fact]
    public void Read_V1_ReadsFrameCountAndFps()
    {
        var ala = AnimationV1(31, 30f, BoneV1("ROOT", 0));

        var animation = AlaAnimationReader.Read(ala);

        Assert.Equal(31, animation.FrameCount);
        Assert.Equal(30f, animation.Fps);
    }

    [Fact]
    public void Read_ReportsBoneNamesAndTheIndicesTheyDriveOnTheModel()
    {
        // The index is what ties a track to a bone of the .alo; the name is what confirms the pairing
        // when hunting for which animations belong to which model.
        var ala = AnimationV1(2, 30f,
            BoneV1("ROOT", 0),
            BoneV1("Bone_Turret", 7));

        var bones = AlaAnimationReader.Read(ala).Bones;

        Assert.Equal(["ROOT", "Bone_Turret"], bones.Select(b => b.Name));
        Assert.Equal([0, 7], bones.Select(b => b.BoneIndex));
    }

    [Fact]
    public void Read_ProducesOneFramePerDeclaredFrame()
    {
        var ala = AnimationV1(5, 30f, BoneV1("ROOT", 0));

        Assert.Equal(5, AlaAnimationReader.Read(ala).Bones[0].Frames.Count);
    }

    // ── version 1 tracks ──────────────────────────────────────────────────────

    [Fact]
    public void Read_V1_ReconstitutesTranslationAsOffsetPlusRawTimesScale()
    {
        var ala = AnimationV1(2, 30f,
            BoneV1("ROOT", 0,
                translationOffset: new Vector3(10, 20, 30),
                translationScale: new Vector3(0.5f, 0.25f, 0.1f),
                translationSamples: [PackedVector(2, 4, 10), PackedVector(4, 8, 20)]));

        var frames = AlaAnimationReader.Read(ala).Bones[0].Frames;

        AssertClose(new Vector3(11, 21, 31), frames[0].Translation);
        AssertClose(new Vector3(12, 22, 32), frames[1].Translation);
    }

    [Fact]
    public void Read_V1_ReconstitutesScaleTheSameWay()
    {
        // No shipped animation carries a scale track, but the format defines one and the engine reads
        // it, so a mod producing one must not be mis-read.
        var ala = AnimationV1(1, 30f,
            BoneV1("ROOT", 0,
                scaleOffset: new Vector3(1, 1, 1),
                scaleScale: new Vector3(0.01f, 0.02f, 0.04f),
                scaleSamples: [PackedVector(100, 100, 100)]));

        AssertClose(new Vector3(2, 3, 5), AlaAnimationReader.Read(ala).Bones[0].Frames[0].Scale);
    }

    [Fact]
    public void Read_V1_UnpacksRotationSamples()
    {
        var ala = AnimationV1(2, 30f,
            BoneV1("ROOT", 0, rotationSamples:
            [
                PackedQuaternion(0, 0, 0, 1),
                PackedQuaternion(1, 0, 0, 0)
            ]));

        var frames = AlaAnimationReader.Read(ala).Bones[0].Frames;

        AssertClose(Quaternion.Identity, frames[0].Rotation);
        AssertClose(new Quaternion(1, 0, 0, 0), frames[1].Rotation);
    }

    [Fact]
    public void Read_V1_TreatsASingleSampleRotationBlockAsConstant()
    {
        // A rotation chunk holding exactly one sample means "this bone never rotates", not "this
        // animation has one frame" - the engine distinguishes them by chunk size alone.
        var ala = AnimationV1(4, 30f,
            BoneV1("ROOT", 0, rotationSamples: [PackedQuaternion(1, 0, 0, 0)]));

        var frames = AlaAnimationReader.Read(ala).Bones[0].Frames;

        Assert.Equal(4, frames.Count);
        Assert.All(frames, f => AssertClose(new Quaternion(1, 0, 0, 0), f.Rotation));
    }

    [Fact]
    public void Read_V1_WithoutTracks_HoldsTheOffsetsAndAnIdentityRotation()
    {
        // 38466 shipped bones carry a descriptor and no tracks at all. The reference implementation
        // leaves the rotation UNINITIALISED in this case, which is a bug rather than a behaviour to
        // reproduce; identity is the only sane answer.
        var ala = AnimationV1(3, 30f,
            BoneV1("ROOT", 0,
                translationOffset: new Vector3(1, 2, 3),
                scaleOffset: new Vector3(4, 5, 6)));

        var frames = AlaAnimationReader.Read(ala).Bones[0].Frames;

        Assert.All(frames, f =>
        {
            AssertClose(new Vector3(1, 2, 3), f.Translation);
            AssertClose(new Vector3(4, 5, 6), f.Scale);
            AssertClose(Quaternion.Identity, f.Rotation);
        });
    }

    [Fact]
    public void Read_V1_SkipsTheTrackItDoesNotUnderstand()
    {
        var ala = AnimationV1(2, 30f,
            BoneV1("ROOT", 0, rotationSamples:
                [PackedQuaternion(0, 0, 0, 1), PackedQuaternion(0, 0, 0, 1)],
                includeSkippedTrack: true));

        Assert.Equal(2, AlaAnimationReader.Read(ala).Bones[0].Frames.Count);
    }

    [Fact]
    public void Read_ToleratesADescriptorWithoutTheIgnoredMiniChunk()
    {
        // 1806 of the shipped descriptors omit mini-chunk 10.
        var ala = AnimationV1(1, 30f, BoneV1("ROOT", 0, includeIgnoredMini10: false));

        Assert.Single(AlaAnimationReader.Read(ala).Bones);
    }

    // ── visibility ────────────────────────────────────────────────────────────

    [Fact]
    public void Read_ReadsVisibilityOneBitPerFrame()
    {
        // Bit i of byte i/8, least-significant first. The reference shifts without masking, so any set
        // bit above the one being tested reads as visible; that is a bug and is not reproduced here.
        var ala = AnimationV1(10, 30f,
            BoneV1("ROOT", 0, visibility:
                [true, false, true, false, false, false, false, false, false, true]));

        var frames = AlaAnimationReader.Read(ala).Bones[0].Frames;

        Assert.Equal(
            [true, false, true, false, false, false, false, false, false, true],
            frames.Select(f => f.Visible));
    }

    [Fact]
    public void Read_WithoutAVisibilityTrack_LeavesEveryFrameVisible()
    {
        var ala = AnimationV1(3, 30f, BoneV1("ROOT", 0));

        Assert.All(AlaAnimationReader.Read(ala).Bones[0].Frames, f => Assert.True(f.Visible));
    }

    // ── version 2 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Read_V2_PullsSamplesFromTheSharedBlocksByBoneSlot()
    {
        // Two bones share one rotation block. It is frame-major: every bone's slot for frame 0, then
        // every bone's slot for frame 1. Reading it bone-major instead swaps the two bones' motion,
        // which looks plausible and is completely wrong.
        var rotation = AloChunkFixture.Concat(
            PackedQuaternion(0, 0, 0, 1), PackedQuaternion(1, 0, 0, 0),
            PackedQuaternion(0, 1, 0, 0), PackedQuaternion(0, 0, 1, 0));

        var ala = AnimationV2(2, 30f, rotationSlots: 2, translationSlots: 0, scaleSlots: 0,
            bones:
            [
                BoneV2("A", 0, rotationSlot: 0),
                BoneV2("B", 1, rotationSlot: 1)
            ],
            rotationBlock: rotation);

        var bones = AlaAnimationReader.Read(ala).Bones;

        AssertClose(Quaternion.Identity, bones[0].Frames[0].Rotation);
        AssertClose(new Quaternion(0, 1, 0, 0), bones[0].Frames[1].Rotation);
        AssertClose(new Quaternion(1, 0, 0, 0), bones[1].Frames[0].Rotation);
        AssertClose(new Quaternion(0, 0, 1, 0), bones[1].Frames[1].Rotation);
    }

    [Fact]
    public void Read_V2_PullsTranslationFromItsOwnSharedBlock()
    {
        var translation = AloChunkFixture.Concat(PackedVector(2, 0, 0), PackedVector(4, 0, 0));

        var ala = AnimationV2(2, 30f, rotationSlots: 0, translationSlots: 1, scaleSlots: 0,
            bones: [BoneV2("A", 0, translationOffset: new Vector3(1, 0, 0),
                translationScale: new Vector3(0.5f, 0, 0), translationSlot: 0)],
            translationBlock: translation);

        var frames = AlaAnimationReader.Read(ala).Bones[0].Frames;

        AssertClose(new Vector3(2, 0, 0), frames[0].Translation);
        AssertClose(new Vector3(3, 0, 0), frames[1].Translation);
    }

    [Fact]
    public void Read_V2_FallsBackToTheStoredDefaultRotationWhenABoneHasNoSlot()
    {
        var ala = AnimationV2(2, 30f, rotationSlots: 0, translationSlots: 0, scaleSlots: 0,
            bones: [BoneV2("A", 0, defaultRotation: (1, 0, 0, 0))]);

        var frames = AlaAnimationReader.Read(ala).Bones[0].Frames;

        Assert.All(frames, f => AssertClose(new Quaternion(1, 0, 0, 0), f.Rotation));
    }

    [Fact]
    public void Read_V2_ReadsVisibilityLikeVersionOne()
    {
        var ala = AnimationV2(3, 30f, rotationSlots: 0, translationSlots: 0, scaleSlots: 0,
            bones: [BoneV2("A", 0, visibility: [true, false, true])]);

        Assert.Equal([true, false, true],
            AlaAnimationReader.Read(ala).Bones[0].Frames.Select(f => f.Visible));
    }

    // ── refusals ──────────────────────────────────────────────────────────────

    [Fact]
    public void Read_V1_RejectsATrackShorterThanTheFrameCount()
    {
        var ala = AnimationV1(10, 30f,
            BoneV1("ROOT", 0, translationSamples: [PackedVector(1, 1, 1)]));

        Assert.Throws<AloFormatException>(() => AlaAnimationReader.Read(ala));
    }

    [Fact]
    public void Read_V2_RejectsASharedBlockThatDoesNotMatchItsDeclaredStride()
    {
        var ala = AnimationV2(4, 30f, rotationSlots: 2, translationSlots: 0, scaleSlots: 0,
            bones: [BoneV2("A", 0, rotationSlot: 0)],
            rotationBlock: PackedQuaternion(0, 0, 0, 1));

        Assert.Throws<AloFormatException>(() => AlaAnimationReader.Read(ala));
    }

    [Fact]
    public void Read_V2_RejectsASlotOutsideTheBlock()
    {
        var rotation = AloChunkFixture.Concat(PackedQuaternion(0, 0, 0, 1));

        var ala = AnimationV2(1, 30f, rotationSlots: 1, translationSlots: 0, scaleSlots: 0,
            bones: [BoneV2("A", 0, rotationSlot: 5)],
            rotationBlock: rotation);

        Assert.Throws<AloFormatException>(() => AlaAnimationReader.Read(ala));
    }

    [Fact]
    public void Read_RejectsABufferThatIsNotAnAnimation()
    {
        Assert.Throws<AloFormatException>(
            () => AlaAnimationReader.Read(AloChunkFixture.Chunk(0x200, true, AloChunkFixture.Zeros(4))));
    }
}
