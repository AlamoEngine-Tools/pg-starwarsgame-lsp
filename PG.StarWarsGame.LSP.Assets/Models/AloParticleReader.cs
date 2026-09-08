// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using static PG.StarWarsGame.LSP.Assets.Models.AloChunkStream;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>
///     Reads an ALO particle system.
/// </summary>
/// <remarks>
///     <para>
///         Ported from <c>OldEmitter</c> in <c>alo-viewer/src/RenderEngine/Particles/ParticleSystem.cpp</c>.
///         Only format version 1 - and that is the whole format in practice: reading the root chunk of
///         every <c>.alo</c> in both shipped trees found 987 particle systems and every one of them is
///         v1. Version 2 is a Universe at War format assembled from around sixty plugin types; it is
///         detected and refused rather than half-read.
///     </para>
///     <para>
///         The layout was checked against all 987 files before this was written: three spawn volumes
///         per emitter, a 64-byte volume record, fourteen chunks in the track block (a header and a key
///         list for each of seven tracks), and every property id in the corpus is one this handles.
///     </para>
/// </remarks>
public static class AloParticleReader
{
    private const uint ChunkRootV1 = 0x900;
    private const uint ChunkRootV2 = 0x1500;

    private const uint ChunkName = 0x0000;
    private const uint ChunkId = 0x0001;
    private const uint ChunkLeaveParticles = 0x0002;
    private const uint ChunkEmitters = 0x0800;
    private const uint ChunkEmitter = 0x0700;

    private const uint ChunkProperties = 0x0002;
    private const uint ChunkColorTexture = 0x0003;
    private const uint ChunkEmitterName = 0x0016;
    private const uint ChunkVolumes = 0x0029;
    private const uint ChunkTracks = 0x0001;
    private const uint ChunkSpawn = 0x0036;
    private const uint ChunkNormalTexture = 0x0045;

    private const uint ChunkVolume = 0x1100;
    private const uint ChunkVolumeData = 0x1101;

    private const uint ChunkTrackHeader = 0x0000;
    private const uint ChunkTrackKeys = 0x0001;

    private const byte MiniTrackFirst = 0x02;
    private const byte MiniTrackLast = 0x03;
    private const byte MiniTrackInterpolation = 0x04;
    private const byte MiniTrackKey = 0x05;

    private const byte MiniSpawnOnDeath = 0x37;
    private const byte MiniSpawnDuringLife = 0x39;

    /// <summary>Every shape's fields in one fixed record, whatever the shape.</summary>
    private const int VolumeRecordSize = 64;

    /// <summary>Speed, lifetime and position, in that order.</summary>
    private const int VolumeCount = 3;

    /// <summary>
    ///     Red, green, blue and alpha come first and store their values as bytes; scale, texture index
    ///     and rotation speed follow and store floats.
    /// </summary>
    private const int ByteValuedTrackCount = 4;

    private const int TrackCount = 7;

    /// <summary>Parses <paramref name="bytes" /> as an ALO particle system.</summary>
    /// <exception cref="AloFormatException">
    ///     The buffer is not a well-formed v1 particle system, including when it is a v2 one.
    /// </exception>
    public static AlamoParticleContent Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var top = Children(bytes, 0, bytes.Length);
        if (top.Count == 0)
            throw Malformed("the file contains no chunks");

        if (top[0].Type == ChunkRootV2)
            throw Malformed(
                "this is a version 2 particle system, which is a Universe at War format built from " +
                "plugin types this reader does not implement. No Star Wars particle file uses it");

        var root = Expect(top, 0, ChunkRootV1, "the particle system block");
        var children = Children(bytes, root.BodyStart, root.BodyEnd);

        var name = ReadString(bytes, Expect(children, 0, ChunkName, "the system name"));
        Expect(children, 1, ChunkId, "the system id");

        var emitters = new List<AlamoEmitter>();
        var leaveParticles = true;

        for (var i = 2; i < children.Count; i++)
        {
            var chunk = children[i];
            switch (chunk.Type)
            {
                case ChunkEmitters:
                    foreach (var emitter in Children(bytes, chunk.BodyStart, chunk.BodyEnd))
                    {
                        if (emitter.Type != ChunkEmitter)
                            throw Malformed(
                                $"the emitter list holds unexpected chunk 0x{emitter.Type:X}");

                        emitters.Add(ReadEmitter(bytes, emitter, emitters.Count));
                    }

                    break;

                case ChunkLeaveParticles:
                    if (chunk.BodyLength != 1)
                        throw Malformed(
                            $"the leave-particles flag is {chunk.BodyLength} bytes, expected 1");
                    leaveParticles = bytes[chunk.BodyStart] != 0;
                    break;

                default:
                    throw Malformed($"unexpected chunk 0x{chunk.Type:X} in the particle system");
            }
        }

        return new AlamoParticleContent(name, leaveParticles, emitters);
    }

    // ── emitters ──────────────────────────────────────────────────────────────

    private static AlamoEmitter ReadEmitter(byte[] bytes, AloChunk chunk, int index)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);
        var where = $"emitter {index}";

        var properties = ReadProperties(
            bytes, Expect(children, 0, ChunkProperties, $"the properties of {where}"), where);
        var colorTexture = ReadString(
            bytes, Expect(children, 1, ChunkColorTexture, $"the colour texture of {where}"));
        var name = ReadString(
            bytes, Expect(children, 2, ChunkEmitterName, $"the name of {where}"));
        var volumes = ReadVolumes(
            bytes, Expect(children, 3, ChunkVolumes, $"the spawn volumes of {where}"), where);
        var tracks = ReadTracks(
            bytes, Expect(children, 4, ChunkTracks, $"the tracks of {where}"), where);

        var spawnOnDeath = -1;
        var spawnDuringLife = -1;
        string? normalTexture = null;

        for (var i = 5; i < children.Count; i++)
        {
            var extra = children[i];
            switch (extra.Type)
            {
                case ChunkSpawn:
                    (spawnOnDeath, spawnDuringLife) = ReadSpawnLinks(bytes, extra, where);
                    break;
                case ChunkNormalTexture:
                    normalTexture = ReadString(bytes, extra);
                    break;
                default:
                    throw Malformed($"{where} carries unexpected chunk 0x{extra.Type:X}");
            }
        }

        // With random rotation on, the engine reinterprets the rotation track's first key as an
        // average and the stored variance as a fraction of it. Without this an emitter set to rotate
        // randomly spins at entirely the wrong rate.
        if (properties.RandomRotation)
            properties = ApplyRandomRotation(properties, tracks);

        return new AlamoEmitter(name, colorTexture, normalTexture,
            volumes[0], volumes[1], volumes[2], tracks, spawnOnDeath, spawnDuringLife, properties);
    }

    private static AlamoEmitterProperties ApplyRandomRotation(
        AlamoEmitterProperties properties, IReadOnlyList<AlamoTrack> tracks)
    {
        var rotation = tracks.FirstOrDefault(t => t.Channel == AlamoTrackChannel.RotationSpeed);
        var average = rotation?.Keys.Count > 0 ? rotation.Keys[0].Value : 0f;

        if (average <= 0)
            return properties with { RandomRotationVariance = 0f, RandomRotationAverage = 0f };

        return properties with
        {
            RandomRotationVariance = properties.RandomRotationVariance / average,
            // Only the fractional part survives: the whole turns are not a speed.
            RandomRotationAverage = average - MathF.Truncate(average)
        };
    }

    private static (int OnDeath, int DuringLife) ReadSpawnLinks(
        byte[] bytes, AloChunk chunk, string where)
    {
        var onDeath = -1;
        var duringLife = -1;

        foreach (var mini in MiniChildren(bytes, chunk.BodyStart, chunk.BodyEnd))
            switch (mini.Type)
            {
                case MiniSpawnOnDeath:
                    onDeath = ReadInt32(bytes, mini.BodyStart);
                    break;
                case MiniSpawnDuringLife:
                    duringLife = ReadInt32(bytes, mini.BodyStart);
                    break;
                default:
                    throw Malformed($"{where} has unknown spawn mini-chunk {mini.Type}");
            }

        return (onDeath, duringLife);
    }

    // ── spawn volumes ─────────────────────────────────────────────────────────

    private static List<AlamoSpawnVolume> ReadVolumes(byte[] bytes, AloChunk chunk, string where)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);
        if (children.Count != VolumeCount)
            throw Malformed(
                $"{where} has {children.Count} spawn volumes, expected {VolumeCount} " +
                "(speed, lifetime, position)");

        var volumes = new List<AlamoSpawnVolume>(VolumeCount);

        foreach (var volume in children)
        {
            if (volume.Type != ChunkVolume)
                throw Malformed($"{where} has chunk 0x{volume.Type:X} where a spawn volume was expected");

            var data = Expect(
                Children(bytes, volume.BodyStart, volume.BodyEnd), 0, ChunkVolumeData,
                $"a spawn volume record of {where}");

            if (data.BodyLength != VolumeRecordSize)
                throw Malformed(
                    $"{where} has a {data.BodyLength}-byte spawn volume, expected {VolumeRecordSize}");

            var o = data.BodyStart;
            volumes.Add(new AlamoSpawnVolume(
                (AlamoSpawnShape)ReadUInt32(bytes, o),
                ReadVector3(bytes, o + 4),
                ReadVector3(bytes, o + 16),
                ReadSingle(bytes, o + 28),
                ReadSingle(bytes, o + 32),
                ReadUInt32(bytes, o + 36) != 0,
                ReadSingle(bytes, o + 40),
                ReadUInt32(bytes, o + 44) != 0,
                ReadSingle(bytes, o + 48),
                ReadVector3(bytes, o + 52)));
        }

        return volumes;
    }

    // ── tracks ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Reads the seven tracks.
    /// </summary>
    /// <remarks>
    ///     Each is TWO sibling chunks - a header carrying the endpoints and the interpolation, then a
    ///     list of the keys between them - which is why the block holds fourteen children, as every
    ///     shipped emitter does. The endpoints are folded in so the result is one ordered curve.
    /// </remarks>
    private static List<AlamoTrack> ReadTracks(byte[] bytes, AloChunk chunk, string where)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);
        if (children.Count != TrackCount * 2)
            throw Malformed(
                $"{where} has {children.Count} track chunks, expected {TrackCount * 2} " +
                $"(a header and a key list for each of {TrackCount} tracks)");

        var tracks = new List<AlamoTrack>(TrackCount);

        for (var i = 0; i < TrackCount; i++)
        {
            var header = children[i * 2];
            var keys = children[i * 2 + 1];

            if (header.Type != ChunkTrackHeader || keys.Type != ChunkTrackKeys)
                throw Malformed($"track {i} of {where} is not a header and key-list pair");

            // The first four channels store their values as a single byte over 255; the rest store
            // floats. Same chunk shape, different payload - the position in the block is the only
            // thing that says which.
            var byteValued = i < ByteValuedTrackCount;
            tracks.Add(ReadTrack(bytes, header, keys, (AlamoTrackChannel)i, byteValued, where));
        }

        return tracks;
    }

    private static AlamoTrack ReadTrack(
        byte[] bytes, AloChunk header, AloChunk keyList, AlamoTrackChannel channel,
        bool byteValued, string where)
    {
        float first = 0, last = 0;
        var interpolation = AlamoTrackInterpolation.Linear;

        foreach (var mini in MiniChildren(bytes, header.BodyStart, header.BodyEnd))
            switch (mini.Type)
            {
                case MiniTrackFirst:
                    first = Endpoint(bytes, mini, byteValued);
                    break;
                case MiniTrackLast:
                    last = Endpoint(bytes, mini, byteValued);
                    break;
                case MiniTrackInterpolation:
                    // Anything unrecognised is linear, matching the engine's own default arm.
                    var raw = ReadInt32(bytes, mini.BodyStart);
                    interpolation = raw is >= 0 and <= 2
                        ? (AlamoTrackInterpolation)raw
                        : AlamoTrackInterpolation.Linear;
                    break;
                default:
                    throw Malformed(
                        $"the {channel} track of {where} has unknown header mini-chunk {mini.Type}");
            }

        var keys = new List<AlamoTrackKey> { new(0f, first) };

        foreach (var mini in MiniChildren(bytes, keyList.BodyStart, keyList.BodyEnd))
        {
            if (mini.Type != MiniTrackKey)
                throw Malformed(
                    $"the {channel} track of {where} has unknown key mini-chunk {mini.Type}");

            // Value first, then time - the reverse of how a key reads.
            var value = byteValued
                ? ReadInt32(bytes, mini.BodyStart) / 255f
                : ReadSingle(bytes, mini.BodyStart);
            var time = ReadSingle(bytes, mini.BodyStart + sizeof(float));

            keys.Add(new AlamoTrackKey(time, value));
        }

        keys.Add(new AlamoTrackKey(1f, last));
        return new AlamoTrack(channel, interpolation, keys);
    }

    private static float Endpoint(byte[] bytes, AloChunk mini, bool byteValued)
    {
        return byteValued ? bytes[mini.BodyStart] / 255f : ReadSingle(bytes, mini.BodyStart);
    }

    // ── properties ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Reads the emitter's scalar settings.
    /// </summary>
    /// <remarks>
    ///     Absent ids keep the engine's defaults - the exporter omits anything left alone, so this is
    ///     a sparse record rather than a struct. An id nobody recognises is refused rather than
    ///     skipped: mini-chunks carry their own length, so a stray one would be survivable, but it
    ///     would also mean the file holds a setting that silently does nothing in the preview.
    /// </remarks>
    private static AlamoEmitterProperties ReadProperties(byte[] bytes, AloChunk chunk, string where)
    {
        var p = new AlamoEmitterProperties();

        // Only meaningful when the file says so, and the engine zeroes it otherwise.
        var useLinkStrength = false;

        foreach (var mini in MiniChildren(bytes, chunk.BodyStart, chunk.BodyEnd))
        {
            var at = mini.BodyStart;

            p = mini.Type switch
            {
                0x04 => p with
                {
                    // Modulo, as the engine does: a file may name a mode this build does not have.
                    BlendMode = (AlamoParticleBlendMode)(ReadInt32(bytes, at) % 14)
                },
                0x05 => p with { TriangleCount = ReadInt32(bytes, at) + 1 },
                0x07 => p with { UseBursts = bytes[at] != 0 },
                0x08 => p with { LinkToSystem = bytes[at] != 0 },
                // Stored positive, applied inward.
                0x09 => p with { InwardSpeed = -ReadSingle(bytes, at) },
                0x0A => p with { Acceleration = ReadVector3(bytes, at) },
                0x0B => p with { InwardAcceleration = -ReadSingle(bytes, at) },
                0x0C => p with { Gravity = ReadSingle(bytes, at) },
                0x0F => p with { Lifetime = ReadSingle(bytes, at) },
                0x10 => p with { TextureSize = ReadInt32(bytes, at) },
                0x12 => p with { RandomScalePercent = ReadSingle(bytes, at) },
                0x13 => p with { RandomLifetimePercent = ReadSingle(bytes, at) },
                0x17 => p with { RandomRotationVariance = MathF.Abs(ReadSingle(bytes, at)) },
                0x23 => p with { RandomRotationDirection = bytes[at] != 0 },
                0x24 => p with { InitialDelay = ReadSingle(bytes, at) },
                0x25 => p with { BurstDelay = ReadSingle(bytes, at) },
                0x26 => p with { ParticlesPerBurst = ReadInt32(bytes, at) },
                // -1 means none, which is not a count.
                0x27 => p with { BurstCount = Math.Max(0, ReadInt32(bytes, at)) },
                0x28 => p with { ParentLinkStrength = ReadSingle(bytes, at) },
                0x2A => p with { ParticlesPerSecond = ReadInt32(bytes, at) },
                0x2C => p with { RandomColors = ReadVector4(bytes, at) },
                0x2D => p with { ColorAddGrayscale = bytes[at] != 0 },
                0x2E => p with { WorldOriented = bytes[at] != 0 },
                0x2F => p with { GroundBehavior = (AlamoGroundBehavior)ReadInt32(bytes, at) },
                0x30 => p with { Bounciness = ReadSingle(bytes, at) },
                0x31 => p with { AffectedByWind = bytes[at] != 0 },
                0x32 => p with { FreezeTime = ReadSingle(bytes, at) },
                0x33 => p with { SkipTime = ReadSingle(bytes, at) },
                0x34 => p with { EmitFromMesh = (AlamoEmitFromMesh)ReadInt32(bytes, at) },
                0x35 => p with { ObjectSpaceAcceleration = bytes[at] != 0 },
                0x3B => p with { IsHeatParticle = bytes[at] != 0 },
                0x3C => p with { EmitFromMeshOffset = ReadSingle(bytes, at) },
                0x3D => p with { IsWeatherParticle = bytes[at] != 0 },
                0x3E => p with { WeatherCubeSize = ReadSingle(bytes, at) },
                0x40 => p with { WeatherFadeoutDistance = ReadSingle(bytes, at) },
                0x41 => p with { HasTail = bytes[at] != 0 },
                0x42 => p with { TailSize = ReadSingle(bytes, at) },
                0x43 => Remember(bytes[at] != 0, ref useLinkStrength, p),
                0x46 => p with { NoDepthTest = bytes[at] != 0 },
                0x47 => p with { WeatherCubeDistance = ReadSingle(bytes, at) },
                0x48 => p with { RandomRotation = bytes[at] != 0 },

                // Read by the engine's editor but not by its renderer. Present in the corpus, so
                // they are skipped deliberately rather than refused.
                0x06 or 0x11 or 0x15 or 0x14 or 0x2B or 0x3F or 0x44 or 0x49 => p,

                _ => throw Malformed($"{where} has unknown property mini-chunk 0x{mini.Type:X}")
            };
        }

        return useLinkStrength ? p : p with { ParentLinkStrength = 0f };
    }

    /// <summary>Records a flag that gates another property, without changing the record.</summary>
    private static AlamoEmitterProperties Remember(
        bool value, ref bool target, AlamoEmitterProperties properties)
    {
        target = value;
        return properties;
    }

    private static Vector3 ReadVector3(byte[] bytes, int offset)
    {
        return new Vector3(
            ReadSingle(bytes, offset), ReadSingle(bytes, offset + 4), ReadSingle(bytes, offset + 8));
    }

    private static Vector4 ReadVector4(byte[] bytes, int offset)
    {
        return new Vector4(
            ReadSingle(bytes, offset), ReadSingle(bytes, offset + 4),
            ReadSingle(bytes, offset + 8), ReadSingle(bytes, offset + 12));
    }
}
