// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AloChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Builds synthetic <c>.ala</c> animation streams, byte-for-byte as the exporter writes them.
/// </summary>
/// <remarks>
///     <para>
///         Both format versions are in live use across the shipped trees - 2308 files are v1 and 1463
///         are v2 - so both are built here. The difference is where the sampled data lives: v1 writes a
///         track per bone inside that bone's block, v2 writes one shared block per channel at the end
///         of the file and gives each bone an index into it.
///     </para>
///     <para>
///         Sampled values are stored packed. A translation or scale sample is three
///         <c>uint16</c>, reconstituted as <c>offset + raw * scale</c> - note <c>raw</c> is the plain
///         integer, NOT normalised. A rotation sample is four <c>int16</c> divided by 32767.
///     </para>
/// </remarks>
internal static class AlaChunkFixture
{
    /// <summary>A translation or scale sample: three raw <c>uint16</c>.</summary>
    public static byte[] PackedVector(ushort x, ushort y, ushort z)
    {
        return [.. BitConverter.GetBytes(x), .. BitConverter.GetBytes(y), .. BitConverter.GetBytes(z)];
    }

    /// <summary>A rotation sample: four <c>int16</c>, each a component times 32767.</summary>
    public static byte[] PackedQuaternion(float x, float y, float z, float w)
    {
        static byte[] C(float v)
        {
            return BitConverter.GetBytes((short)Math.Round(v * short.MaxValue));
        }

        return [.. C(x), .. C(y), .. C(z), .. C(w)];
    }

    // ── file ──────────────────────────────────────────────────────────────────

    /// <summary>
    ///     A version 1 animation: every bone carries its own sampled tracks.
    /// </summary>
    public static byte[] AnimationV1(int frameCount, float fps, params byte[][] bones)
    {
        return Chunk(0x1000, true,
            Chunk(0x1001, true,
                Mini(1, I32(frameCount)), Mini(2, F32(fps)), Mini(3, I32(bones.Length))),
            Concat(bones));
    }

    /// <summary>
    ///     A version 2 animation: bones hold indices, and the samples live in shared blocks at the end.
    /// </summary>
    /// <param name="rotationBlock">
    ///     Every bone's rotation samples, frame-major: frame 0's slot for each indexed bone, then frame
    ///     1's, and so on. Omitted entirely when there are no rotation slots.
    /// </param>
    public static byte[] AnimationV2(
        int frameCount,
        float fps,
        int rotationSlots,
        int translationSlots,
        int scaleSlots,
        byte[][] bones,
        byte[]? rotationBlock = null,
        byte[]? translationBlock = null)
    {
        // The header stores each stride in int16 UNITS, not bytes - the same unit the per-bone slot
        // offsets use. A real file in the shipped corpus carries a rotation stride of 52, which is 13
        // slots and not a whole number of 8-byte quaternions, so the distinction is not academic.
        var header = Chunk(0x1001, true,
            Mini(1, I32(frameCount)),
            Mini(2, F32(fps)),
            Mini(3, I32(bones.Length)),
            Mini(11, I32(rotationSlots * 4)),
            Mini(12, I32(translationSlots * 3)),
            Mini(13, I32(scaleSlots * 3)));

        var parts = new List<byte[]> { header, Concat(bones) };

        // Order matters to the exporter: translation first, then rotation.
        if (translationBlock is not null) parts.Add(Chunk(0x100A, false, translationBlock));
        if (rotationBlock is not null) parts.Add(Chunk(0x1009, false, rotationBlock));

        return Chunk(0x1000, true, Concat([.. parts]));
    }

    // ── bones ─────────────────────────────────────────────────────────────────

    /// <summary>
    ///     A version 1 animated bone.
    /// </summary>
    /// <param name="translationSamples">One <see cref="PackedVector" /> per frame, or null for none.</param>
    /// <param name="rotationSamples">
    ///     One <see cref="PackedQuaternion" /> per frame, or null for none. A block holding exactly one
    ///     sample means a constant rotation rather than a one-frame track.
    /// </param>
    /// <param name="visibility">
    ///     One bit per frame, least-significant bit first, or null to leave every frame visible.
    /// </param>
    public static byte[] BoneV1(
        string name,
        int boneIndex,
        Vector3 translationOffset = default,
        Vector3 translationScale = default,
        Vector3 scaleOffset = default,
        Vector3 scaleScale = default,
        byte[][]? translationSamples = null,
        byte[][]? scaleSamples = null,
        byte[][]? rotationSamples = null,
        bool[]? visibility = null,
        bool includeIgnoredMini10 = true,
        bool includeSkippedTrack = false)
    {
        var parts = new List<byte[]>
        {
            Descriptor(name, boneIndex, translationOffset, translationScale, scaleOffset, scaleScale,
                includeIgnoredMini10, null)
        };

        if (translationSamples is not null) parts.Add(Chunk(0x1004, false, Concat(translationSamples)));
        if (scaleSamples is not null) parts.Add(Chunk(0x1005, false, Concat(scaleSamples)));
        if (rotationSamples is not null) parts.Add(Chunk(0x1006, false, Concat(rotationSamples)));
        if (visibility is not null) parts.Add(Chunk(0x1007, false, VisibilityBits(visibility)));

        // 0x1008 is a track the engine reads and this reader does not; present on 1774 shipped bones,
        // so skipping it correctly is not optional.
        if (includeSkippedTrack) parts.Add(Chunk(0x1008, false, Zeros(4)));

        return Chunk(0x1002, true, Concat([.. parts]));
    }

    /// <summary>
    ///     A version 2 animated bone: indices into the shared blocks instead of its own tracks.
    /// </summary>
    /// <remarks>
    ///     The stored indices are offsets in <c>int16</c> units, not slot numbers - a bone using
    ///     translation slot 2 stores 6, because a translation sample is three shorts wide. Passing
    ///     slots here and converting keeps the fixture in the format's units rather than the reader's.
    /// </remarks>
    public static byte[] BoneV2(
        string name,
        int boneIndex,
        Vector3 translationOffset = default,
        Vector3 translationScale = default,
        Vector3 scaleOffset = default,
        Vector3 scaleScale = default,
        int? translationSlot = null,
        int? scaleSlot = null,
        int? rotationSlot = null,
        (float X, float Y, float Z, float W)? defaultRotation = null,
        bool[]? visibility = null)
    {
        const ushort none = ushort.MaxValue;
        var d = defaultRotation ?? (0f, 0f, 0f, 1f);

        var indices = Concat(
            Mini(14, U16(translationSlot is null ? none : (ushort)(translationSlot.Value * 3))),
            Mini(15, U16(scaleSlot is null ? none : (ushort)(scaleSlot.Value * 3))),
            Mini(16, U16(rotationSlot is null ? none : (ushort)(rotationSlot.Value * 4))),
            Mini(17, PackedQuaternion(d.X, d.Y, d.Z, d.W)));

        var parts = new List<byte[]>
        {
            Descriptor(name, boneIndex, translationOffset, translationScale, scaleOffset, scaleScale,
                true, indices)
        };

        if (visibility is not null) parts.Add(Chunk(0x1007, false, VisibilityBits(visibility)));

        return Chunk(0x1002, true, Concat([.. parts]));
    }

    /// <summary>
    ///     The bone descriptor both versions share, plus v2's index block when supplied.
    /// </summary>
    /// <param name="includeIgnoredMini10">
    ///     Mini-chunk 10 sits between the index and the offsets and is ignored by the engine. Present
    ///     on 101844 of the 103650 shipped bone descriptors, absent on 1806, so both shapes occur.
    /// </param>
    private static byte[] Descriptor(
        string name, int boneIndex, Vector3 tOffset, Vector3 tScale, Vector3 sOffset, Vector3 sScale,
        bool includeIgnoredMini10, byte[]? v2Indices)
    {
        var parts = new List<byte[]> { Mini(4, Str(name)), Mini(5, I32(boneIndex)) };
        if (includeIgnoredMini10) parts.Add(Mini(10, I32(0)));

        parts.Add(Mini(6, F32(tOffset.X, tOffset.Y, tOffset.Z)));
        parts.Add(Mini(7, F32(tScale.X, tScale.Y, tScale.Z)));
        parts.Add(Mini(8, F32(sOffset.X, sOffset.Y, sOffset.Z)));
        parts.Add(Mini(9, F32(sScale.X, sScale.Y, sScale.Z)));

        if (v2Indices is not null) parts.Add(v2Indices);

        return Chunk(0x1003, true, Concat([.. parts]));
    }

    /// <summary>One bit per frame, least-significant bit of the first byte being frame 0.</summary>
    private static byte[] VisibilityBits(bool[] visibility)
    {
        var bytes = new byte[(visibility.Length + 7) / 8];
        for (var i = 0; i < visibility.Length; i++)
            if (visibility[i])
                bytes[i / 8] |= (byte)(1 << (i % 8));

        return bytes;
    }

    private static byte[] U16(ushort value)
    {
        return BitConverter.GetBytes(value);
    }
}
