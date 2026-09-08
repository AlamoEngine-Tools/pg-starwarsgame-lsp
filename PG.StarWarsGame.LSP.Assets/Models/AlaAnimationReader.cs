// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using static PG.StarWarsGame.LSP.Assets.Models.AloChunkStream;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>
///     Reads a <c>.ala</c> animation file.
/// </summary>
/// <remarks>
///     <para>
///         Ported from <c>alo-viewer/src/Assets/Animations.cpp</c>. Both format versions are in live
///         use - 2308 of the shipped animations are v1 and 1463 are v2 - and they differ only in where
///         the samples live: v1 writes a track per bone inside that bone's block, v2 writes one shared
///         block per channel at the end of the file and gives each bone a slot in it.
///     </para>
///     <para>
///         Two bugs in the reference are deliberately NOT reproduced, both noted at their sites: an
///         uninitialised default rotation for a v1 bone with no rotation track, and a visibility test
///         that shifts without masking so any higher set bit reads as visible.
///     </para>
/// </remarks>
public static class AlaAnimationReader
{
    private const uint ChunkRoot = 0x1000;
    private const uint ChunkInfo = 0x1001;
    private const uint ChunkBone = 0x1002;
    private const uint ChunkBoneDescriptor = 0x1003;
    private const uint ChunkBoneTranslation = 0x1004;
    private const uint ChunkBoneScale = 0x1005;
    private const uint ChunkBoneRotation = 0x1006;
    private const uint ChunkBoneVisibility = 0x1007;
    private const uint ChunkBoneUnusedTrack = 0x1008;
    private const uint ChunkSharedRotation = 0x1009;
    private const uint ChunkSharedTranslation = 0x100A;

    private const byte MiniFrameCount = 1;
    private const byte MiniFps = 2;
    private const byte MiniBoneCount = 3;
    private const byte MiniName = 4;
    private const byte MiniBoneIndex = 5;
    private const byte MiniTranslationOffset = 6;
    private const byte MiniTranslationScale = 7;
    private const byte MiniScaleOffset = 8;
    private const byte MiniScaleScale = 9;
    private const byte MiniIgnored = 10;
    private const byte MiniRotationStride = 11;
    private const byte MiniTranslationStride = 12;
    private const byte MiniScaleStride = 13;
    private const byte MiniTranslationSlot = 14;
    private const byte MiniScaleSlot = 15;
    private const byte MiniRotationSlot = 16;
    private const byte MiniDefaultRotation = 17;

    /// <summary>Three <c>uint16</c>.</summary>
    private const int PackedVectorSize = 3 * sizeof(ushort);

    /// <summary>Four <c>int16</c>.</summary>
    private const int PackedQuaternionSize = 4 * sizeof(short);

    /// <summary>
    ///     Sample widths in <c>int16</c> units - the unit the header strides and the per-bone slot
    ///     offsets are both stored in. Kept as named constants because reading either of those as
    ///     bytes is a mistake that survives most files before failing on a real one.
    /// </summary>
    private const int ShortsPerVector = PackedVectorSize / sizeof(ushort);

    private const int ShortsPerQuaternion = PackedQuaternionSize / sizeof(short);

    /// <summary>A slot index of this value means the bone has no track on that channel.</summary>
    private const ushort NoSlot = ushort.MaxValue;

    /// <summary>Parses <paramref name="bytes" /> as an ALA animation.</summary>
    /// <exception cref="AloFormatException">The buffer is not a well-formed ALA animation.</exception>
    public static AlamoAnimationContent Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var top = Children(bytes, 0, bytes.Length);
        var root = Expect(top, 0, ChunkRoot, "the animation block");

        var children = Children(bytes, root.BodyStart, root.BodyEnd);
        var info = Expect(children, 0, ChunkInfo, "the animation header");
        var header = ReadHeader(bytes, info);

        // v2's shared blocks sit after every bone, so they are located before the bones are walked.
        var shared = ReadSharedBlocks(bytes, children, header);

        var bones = new List<AlamoAnimationBone>(header.BoneCount);
        var boneChunks = 0;

        for (var i = 1; i < children.Count; i++)
        {
            if (children[i].Type != ChunkBone)
                continue;

            bones.Add(ReadBone(bytes, children[i], boneChunks, header, shared));
            boneChunks++;
        }

        if (boneChunks != header.BoneCount)
            throw Malformed(
                $"the animation declares {header.BoneCount} bones but carries {boneChunks} bone blocks");

        return new AlamoAnimationContent(header.Fps, header.FrameCount, bones,
            header.IsVersion2 ? 2 : 1);
    }

    // ── header ────────────────────────────────────────────────────────────────

    /// <param name="RotationSlots">
    ///     Slots per frame in the shared rotation block, or zero for v1.
    /// </param>
    private readonly record struct Header(
        int FrameCount,
        float Fps,
        int BoneCount,
        bool IsVersion2,
        int RotationSlots,
        int TranslationSlots,
        int ScaleSlots);

    private static Header ReadHeader(byte[] bytes, AloChunk info)
    {
        var minis = MiniChildren(bytes, info.BodyStart, info.BodyEnd);
        var by = Index(minis);

        var frameCount = ReadInt32(bytes, Require(by, MiniFrameCount, "the frame count").BodyStart);
        var fps = ReadSingle(bytes, Require(by, MiniFps, "the frame rate").BodyStart);
        var boneCount = ReadInt32(bytes, Require(by, MiniBoneCount, "the bone count").BodyStart);

        if (frameCount < 0)
            throw Malformed($"the animation declares {frameCount} frames");
        if (boneCount < 0)
            throw Malformed($"the animation declares {boneCount} bones");

        // The presence of the rotation stride is what marks the file as version 2. All three strides
        // travel together: 1463 of the shipped animations carry the trio, 2308 carry none of it.
        var isVersion2 = by.ContainsKey(MiniRotationStride);
        if (!isVersion2)
            return new Header(frameCount, fps, boneCount, false, 0, 0, 0);

        return new Header(frameCount, fps, boneCount, true,
            Slots(bytes, by, MiniRotationStride, ShortsPerQuaternion, "rotation"),
            Slots(bytes, by, MiniTranslationStride, ShortsPerVector, "translation"),
            Slots(bytes, by, MiniScaleStride, ShortsPerVector, "scale"));
    }

    /// <summary>
    ///     Slots per frame on one channel.
    /// </summary>
    /// <remarks>
    ///     The stored stride counts <c>int16</c> values, NOT bytes - the same unit the per-bone slot
    ///     offsets use. Reading it as bytes happens to work on any file whose stride divides evenly
    ///     both ways and fails loudly on the rest: the shipped corpus contains a 52 stride, which is 13
    ///     rotation slots but not a whole number of 8-byte quaternions.
    /// </remarks>
    private static int Slots(
        byte[] bytes, Dictionary<byte, AloChunk> by, byte mini, int shortsPerSample, string channel)
    {
        var stride = ReadInt32(bytes, Require(by, mini, $"the {channel} stride").BodyStart);
        if (stride < 0 || stride % shortsPerSample != 0)
            throw Malformed(
                $"the {channel} stride is {stride} shorts, which is not whole {shortsPerSample}-short samples");

        return stride / shortsPerSample;
    }

    // ── shared sample blocks (version 2) ──────────────────────────────────────

    private readonly record struct SharedBlocks(AloChunk? Rotation, AloChunk? Translation);

    private static SharedBlocks ReadSharedBlocks(byte[] bytes, List<AloChunk> children, Header header)
    {
        AloChunk? rotation = null;
        AloChunk? translation = null;

        foreach (var chunk in children)
            switch (chunk.Type)
            {
                case ChunkSharedRotation:
                    rotation = chunk;
                    break;
                case ChunkSharedTranslation:
                    translation = chunk;
                    break;
            }

        if (!header.IsVersion2)
        {
            if (rotation is not null || translation is not null)
                throw Malformed("a version 1 animation carries version 2 shared sample blocks");
            return new SharedBlocks(null, null);
        }

        CheckBlock(rotation, header.RotationSlots, header.FrameCount, PackedQuaternionSize, "rotation");
        CheckBlock(translation, header.TranslationSlots, header.FrameCount, PackedVectorSize,
            "translation");

        // There is no shared SCALE block in this format: the header reserves a stride for it and no
        // file ever writes the data, so every version 2 bone holds its scale offset unchanged.
        return new SharedBlocks(rotation, translation);
    }

    private static void CheckBlock(
        AloChunk? chunk, int slots, int frameCount, int sampleSize, string channel)
    {
        var expected = (long)slots * frameCount * sampleSize;

        if (slots == 0)
        {
            if (chunk is not null && chunk.Value.BodyLength != 0)
                throw Malformed(
                    $"the animation declares no {channel} slots but carries a " +
                    $"{chunk.Value.BodyLength}-byte {channel} block");
            return;
        }

        if (chunk is null)
            throw Malformed(
                $"the animation declares {slots} {channel} slots but carries no {channel} block");

        if (chunk.Value.BodyLength != expected)
            throw Malformed(
                $"the {channel} block holds {chunk.Value.BodyLength} bytes but {slots} slots over " +
                $"{frameCount} frames needs {expected}");
    }

    // ── bones ─────────────────────────────────────────────────────────────────

    private static AlamoAnimationBone ReadBone(
        byte[] bytes, AloChunk chunk, int ordinal, Header header, SharedBlocks shared)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);
        var descriptor = Expect(children, 0, ChunkBoneDescriptor, $"the descriptor of bone {ordinal}");
        var by = Index(MiniChildren(bytes, descriptor.BodyStart, descriptor.BodyEnd));

        var name = ReadString(bytes, Require(by, MiniName, $"the name of bone {ordinal}"));
        var boneIndex = ReadInt32(bytes, Require(by, MiniBoneIndex, $"the index of bone '{name}'").BodyStart);

        var translationOffset = ReadVector3(bytes, Require(by, MiniTranslationOffset, "a translation offset").BodyStart);
        var translationScale = ReadVector3(bytes, Require(by, MiniTranslationScale, "a translation scale").BodyStart);
        var scaleOffset = ReadVector3(bytes, Require(by, MiniScaleOffset, "a scale offset").BodyStart);
        var scaleScale = ReadVector3(bytes, Require(by, MiniScaleScale, "a scale scale").BodyStart);

        // Mini-chunk 10 is present on nearly every descriptor and means nothing to the engine.
        _ = by.ContainsKey(MiniIgnored);

        // The reference leaves this uninitialised for a v1 bone with no rotation track, so its pose is
        // whatever was on the stack. Identity is the only defensible answer.
        var defaultRotation = Quaternion.Identity;
        int? rotationSlot = null, translationSlot = null, scaleSlot = null;

        if (header.IsVersion2)
        {
            translationSlot = Slot(bytes, by, MiniTranslationSlot, ShortsPerVector);
            scaleSlot = Slot(bytes, by, MiniScaleSlot, ShortsPerVector);
            rotationSlot = Slot(bytes, by, MiniRotationSlot, ShortsPerQuaternion);
            defaultRotation = UnpackQuaternion(bytes,
                Require(by, MiniDefaultRotation, $"the default rotation of bone '{name}'").BodyStart);
        }

        var tracks = ReadOwnTracks(bytes, children, name, header);

        var frames = new List<AlamoAnimationFrame>(header.FrameCount);
        for (var f = 0; f < header.FrameCount; f++)
        {
            var translation = translationOffset;
            var scale = scaleOffset;
            var rotation = defaultRotation;

            if (tracks.Translation is { } ownTranslation)
                translation += UnpackVector(bytes, ownTranslation.BodyStart + f * PackedVectorSize)
                               * translationScale;
            else if (translationSlot is { } ts && shared.Translation is { } sharedTranslation)
                translation += UnpackVector(bytes,
                                   SampleAt(sharedTranslation, f, header.TranslationSlots, ts,
                                       PackedVectorSize, "translation"))
                               * translationScale;

            if (tracks.Scale is { } ownScale)
                scale += UnpackVector(bytes, ownScale.BodyStart + f * PackedVectorSize) * scaleScale;
            else if (scaleSlot is not null)
                // No file writes a shared scale block, so a slot here has nothing to point at and the
                // bone keeps its offset. Reaching into a block that does not exist would be worse.
                scale = scaleOffset;

            if (tracks.Rotation is { } ownRotation)
                rotation = UnpackQuaternion(bytes, ownRotation.BodyStart +
                                                   (tracks.RotationIsConstant ? 0 : f * PackedQuaternionSize));
            else if (rotationSlot is { } rs && shared.Rotation is { } sharedRotation)
                rotation = UnpackQuaternion(bytes,
                    SampleAt(sharedRotation, f, header.RotationSlots, rs, PackedQuaternionSize,
                        "rotation"));

            frames.Add(new AlamoAnimationFrame(scale, rotation, translation,
                tracks.Visibility is not { } bits || IsVisible(bytes, bits, f)));
        }

        return new AlamoAnimationBone(boneIndex, name, frames);
    }

    /// <param name="RotationIsConstant">
    ///     A rotation block holding exactly one sample means the bone never rotates, rather than the
    ///     animation having one frame. Chunk size is the only thing that distinguishes them.
    /// </param>
    private readonly record struct OwnTracks(
        AloChunk? Translation,
        AloChunk? Scale,
        AloChunk? Rotation,
        bool RotationIsConstant,
        AloChunk? Visibility);

    private static OwnTracks ReadOwnTracks(
        byte[] bytes, List<AloChunk> children, string name, Header header)
    {
        AloChunk? translation = null, scale = null, rotation = null, visibility = null;
        var rotationIsConstant = false;

        for (var i = 1; i < children.Count; i++)
        {
            var chunk = children[i];
            switch (chunk.Type)
            {
                case ChunkBoneTranslation:
                    RequireVersion1(header, name, "a translation track");
                    CheckTrack(chunk, header.FrameCount, PackedVectorSize, name, "translation");
                    translation = chunk;
                    break;
                case ChunkBoneScale:
                    RequireVersion1(header, name, "a scale track");
                    CheckTrack(chunk, header.FrameCount, PackedVectorSize, name, "scale");
                    scale = chunk;
                    break;
                case ChunkBoneRotation:
                    RequireVersion1(header, name, "a rotation track");
                    rotationIsConstant = chunk.BodyLength == PackedQuaternionSize;
                    if (!rotationIsConstant)
                        CheckTrack(chunk, header.FrameCount, PackedQuaternionSize, name, "rotation");
                    rotation = chunk;
                    break;
                case ChunkBoneVisibility:
                    var needed = (header.FrameCount + 7) / 8;
                    if (chunk.BodyLength < needed)
                        throw Malformed(
                            $"bone '{name}' has a {chunk.BodyLength}-byte visibility track, but " +
                            $"{header.FrameCount} frames need {needed}");
                    visibility = chunk;
                    break;
                case ChunkBoneUnusedTrack:
                    // Read by the engine, not by us. Present on 1774 shipped bones.
                    break;
                default:
                    throw Malformed($"bone '{name}' carries unexpected chunk 0x{chunk.Type:X}");
            }
        }

        return new OwnTracks(translation, scale, rotation, rotationIsConstant, visibility);
    }

    private static void RequireVersion1(Header header, string name, string what)
    {
        if (header.IsVersion2)
            throw Malformed($"bone '{name}' carries {what}, which version 2 keeps in a shared block");
    }

    private static void CheckTrack(
        AloChunk chunk, int frameCount, int sampleSize, string name, string channel)
    {
        var expected = (long)frameCount * sampleSize;
        if (chunk.BodyLength != expected)
            throw Malformed(
                $"bone '{name}' has a {chunk.BodyLength}-byte {channel} track, but {frameCount} " +
                $"frames need {expected}");
    }

    private static int SampleAt(
        AloChunk block, int frame, int slots, int slot, int sampleSize, string channel)
    {
        if (slot < 0 || slot >= slots)
            throw Malformed(
                $"a bone claims {channel} slot {slot}, but the block holds {slots} slots per frame");

        // Frame-major: every slot for frame 0, then every slot for frame 1. Reading it slot-major
        // instead swaps whole bones' motion around, which looks plausible and is entirely wrong.
        return block.BodyStart + (frame * slots + slot) * sampleSize;
    }

    /// <summary>The slot a bone claims on one channel, or null when it claims none.</summary>
    /// <param name="shortsPerSample">
    ///     Stored slots are offsets in <c>int16</c> units, so a bone using translation slot 2 stores 6.
    /// </param>
    private static int? Slot(
        byte[] bytes, Dictionary<byte, AloChunk> by, byte mini, int shortsPerSample)
    {
        if (!by.TryGetValue(mini, out var chunk))
            return null;

        var raw = BitConverter.ToUInt16(bytes, chunk.BodyStart);
        if (raw == NoSlot)
            return null;

        if (raw % shortsPerSample != 0)
            throw Malformed(
                $"a slot offset of {raw} is not a whole number of {shortsPerSample}-short samples");

        return raw / shortsPerSample;
    }

    // ── unpacking ─────────────────────────────────────────────────────────────

    /// <summary>
    ///     Three raw <c>uint16</c>. Deliberately NOT normalised - the caller multiplies by the bone's
    ///     own scale and adds its offset, which is where the real magnitude comes from.
    /// </summary>
    private static Vector3 UnpackVector(byte[] bytes, int offset)
    {
        return new Vector3(
            BitConverter.ToUInt16(bytes, offset),
            BitConverter.ToUInt16(bytes, offset + 2),
            BitConverter.ToUInt16(bytes, offset + 4));
    }

    /// <summary>Four <c>int16</c> over 32767.</summary>
    private static Quaternion UnpackQuaternion(byte[] bytes, int offset)
    {
        return new Quaternion(
            BitConverter.ToInt16(bytes, offset) / (float)short.MaxValue,
            BitConverter.ToInt16(bytes, offset + 2) / (float)short.MaxValue,
            BitConverter.ToInt16(bytes, offset + 4) / (float)short.MaxValue,
            BitConverter.ToInt16(bytes, offset + 6) / (float)short.MaxValue);
    }

    /// <summary>
    ///     One bit per frame, least-significant first.
    /// </summary>
    /// <remarks>
    ///     The reference shifts without masking, so any set bit above the one under test reads as
    ///     visible - meaning a bone hidden on frame 0 but visible on frame 1 reads as visible on both.
    ///     The mask is the fix.
    /// </remarks>
    private static bool IsVisible(byte[] bytes, AloChunk track, int frame)
    {
        return ((bytes[track.BodyStart + frame / 8] >> (frame % 8)) & 1) != 0;
    }

    private static Vector3 ReadVector3(byte[] bytes, int offset)
    {
        return new Vector3(
            ReadSingle(bytes, offset), ReadSingle(bytes, offset + 4), ReadSingle(bytes, offset + 8));
    }

    private static Dictionary<byte, AloChunk> Index(List<AloChunk> minis)
    {
        var by = new Dictionary<byte, AloChunk>();
        foreach (var mini in minis) by[(byte)mini.Type] = mini;
        return by;
    }

    private static AloChunk Require(Dictionary<byte, AloChunk> by, byte type, string what)
    {
        if (!by.TryGetValue(type, out var chunk))
            throw Malformed($"the animation is missing {what} (mini-chunk {type})");

        return chunk;
    }
}
