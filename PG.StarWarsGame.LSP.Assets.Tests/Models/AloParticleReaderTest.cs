// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using static PG.StarWarsGame.LSP.Assets.Tests.Models.AloChunkFixture;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     The v1 particle format: emitter properties, spawn volumes and lifetime tracks.
/// </summary>
/// <remarks>
///     The corpus sweep proves every shipped file parses; these prove the numbers that come out are
///     the right ones. The two halves catch different things - a field read at the wrong offset still
///     parses perfectly.
/// </remarks>
public sealed class AloParticleReaderTest
{
    // ── fixture ───────────────────────────────────────────────────────────────

    /// <summary>A 64-byte spawn volume record, the one shape the file always stores.</summary>
    private static byte[] Volume(
        AlamoSpawnShape shape,
        Vector3 min = default, Vector3 max = default,
        float sideLength = 0, float sphereRadius = 0, bool sphereEdge = false,
        float cylinderRadius = 0, bool cylinderEdge = false, float cylinderHeight = 0,
        Vector3 exact = default)
    {
        return Chunk(0x1100, true, Chunk(0x1101, false,
            I32((int)shape),
            F32(min.X, min.Y, min.Z),
            F32(max.X, max.Y, max.Z),
            F32(sideLength),
            F32(sphereRadius),
            I32(sphereEdge ? 1 : 0),
            F32(cylinderRadius),
            I32(cylinderEdge ? 1 : 0),
            F32(cylinderHeight),
            F32(exact.X, exact.Y, exact.Z)));
    }

    /// <summary>
    ///     One track: a header carrying the endpoints and interpolation, then the keys between them.
    /// </summary>
    /// <param name="byteValued">
    ///     The four colour channels store endpoints as a byte and key values as an int over 255; the
    ///     rest store floats throughout. Same chunk shape either way.
    /// </param>
    private static byte[] Track(
        float first, float last, AlamoTrackInterpolation interpolation, bool byteValued,
        params (float Value, float Time)[] keys)
    {
        byte[] Endpoint(float v) => byteValued ? [(byte)Math.Round(v * 255)] : F32(v);

        var keyChunks = keys.Select(k => Mini(0x05,
            byteValued ? I32((int)Math.Round(k.Value * 255)) : F32(k.Value),
            F32(k.Time))).ToArray();

        return Concat(
            Chunk(0x0000, true,
                Mini(0x02, Endpoint(first)),
                Mini(0x03, Endpoint(last)),
                Mini(0x04, I32((int)interpolation))),
            Chunk(0x0001, true, Concat(keyChunks)));
    }

    /// <summary>Seven flat tracks, for tests that are not about tracks.</summary>
    private static byte[] DefaultTracks()
    {
        var parts = new List<byte[]>();
        for (var i = 0; i < 7; i++)
            parts.Add(Track(1f, 1f, AlamoTrackInterpolation.Linear, i < 4));

        return Concat([.. parts]);
    }

    private static byte[] Emitter(
        string name = "spark",
        string colorTexture = "p_particle_master.tga",
        byte[][]? properties = null,
        byte[]? volumes = null,
        byte[]? tracks = null,
        (int OnDeath, int DuringLife)? spawn = null,
        string? normalTexture = null)
    {
        var parts = new List<byte[]>
        {
            Chunk(0x0002, true, Concat(properties ?? [])),
            Chunk(0x0003, false, Str(colorTexture)),
            Chunk(0x0016, false, Str(name)),
            Chunk(0x0029, true, volumes ?? Concat(
                Volume(AlamoSpawnShape.Point),
                Volume(AlamoSpawnShape.Point),
                Volume(AlamoSpawnShape.Point))),
            Chunk(0x0001, true, tracks ?? DefaultTracks()),
        };

        if (spawn is { } links)
            parts.Add(Chunk(0x0036, true,
                Mini(0x37, I32(links.OnDeath)), Mini(0x39, I32(links.DuringLife))));

        if (normalTexture is not null)
            parts.Add(Chunk(0x0045, false, Str(normalTexture)));

        return Chunk(0x0700, true, Concat([.. parts]));
    }

    private static byte[] System(
        string name = "p_test", bool? leaveParticles = null, params byte[][] emitters)
    {
        var parts = new List<byte[]>
        {
            Chunk(0x0000, false, Str(name)),
            Chunk(0x0001, false, I32(0)),
            Chunk(0x0800, true, Concat(emitters.Length == 0 ? [Emitter()] : emitters)),
        };

        if (leaveParticles is { } leave)
            parts.Add(Chunk(0x0002, false, [leave ? (byte)1 : (byte)0]));

        return Chunk(0x0900, true, Concat([.. parts]));
    }

    // ── structure ─────────────────────────────────────────────────────────────

    [Fact]
    public void Read_ReadsTheSystemNameAndItsEmitters()
    {
        var content = AloParticleReader.Read(System("p_basepad_spark", null,
            Emitter("Electrical"), Emitter("Sparks")));

        Assert.Equal("p_basepad_spark", content.Name);
        Assert.Equal(["Electrical", "Sparks"], content.Emitters.Select(e => e.Name));
    }

    [Fact]
    public void Read_ReadsBothTextures()
    {
        var content = AloParticleReader.Read(System("p", null,
            Emitter(colorTexture: "p_particle_master.tga",
                normalTexture: "p_particle_depth_master.tga")));

        Assert.Equal("p_particle_master.tga", content.Emitters[0].ColorTexture);
        Assert.Equal("p_particle_depth_master.tga", content.Emitters[0].NormalTexture);
    }

    [Fact]
    public void Read_LeavesTheNormalTextureUnsetWhenTheFileCarriesNone()
    {
        Assert.Null(AloParticleReader.Read(System()).Emitters[0].NormalTexture);
    }

    [Fact]
    public void Read_DefaultsLeaveParticlesToTrueWhenTheChunkIsAbsent()
    {
        // Absent on 54 of the 987 shipped systems.
        Assert.True(AloParticleReader.Read(System()).LeaveParticles);
        Assert.False(AloParticleReader.Read(System("p", false)).LeaveParticles);
    }

    [Fact]
    public void Read_ReadsTheSpawnLinksThatChainOneEmitterToAnother()
    {
        // How an explosion starts its own smoke. -1 means no link.
        var content = AloParticleReader.Read(System("p", null, Emitter(spawn: (2, -1))));

        Assert.Equal(2, content.Emitters[0].SpawnOnDeath);
        Assert.Equal(-1, content.Emitters[0].SpawnDuringLife);
    }

    [Fact]
    public void Read_RefusesAVersion2System()
    {
        // A Universe at War format built from around sixty plugin types. No Star Wars file uses it,
        // and mis-reading one would be worse than refusing it.
        var v2 = Chunk(0x1500, true, Chunk(0x0000, false, Zeros(4)));

        var error = Assert.Throws<AloFormatException>(() => AloParticleReader.Read(v2));
        Assert.Contains("version 2", error.Message);
    }

    // ── spawn volumes ─────────────────────────────────────────────────────────

    [Fact]
    public void Read_ReadsEveryFieldOfASpawnVolumeAtItsOwnOffset()
    {
        // One fixed record holds every shape's fields whatever the shape, so a field read at the
        // wrong offset still parses - only distinct values catch it.
        var volumes = Concat(
            Volume(AlamoSpawnShape.Box, min: new Vector3(1, 2, 3), max: new Vector3(4, 5, 6)),
            Volume(AlamoSpawnShape.Sphere, sphereRadius: 7.5f, sphereEdge: true),
            Volume(AlamoSpawnShape.Cylinder,
                cylinderRadius: 2.5f, cylinderEdge: true, cylinderHeight: 9f));

        var emitter = AloParticleReader.Read(System("p", null, Emitter(volumes: volumes))).Emitters[0];

        Assert.Equal(AlamoSpawnShape.Box, emitter.Speed.Shape);
        Assert.Equal(new Vector3(1, 2, 3), emitter.Speed.Min);
        Assert.Equal(new Vector3(4, 5, 6), emitter.Speed.Max);

        Assert.Equal(7.5f, emitter.Lifetime.SphereRadius);
        Assert.True(emitter.Lifetime.SphereEdgeOnly);

        Assert.Equal(2.5f, emitter.Position.CylinderRadius);
        Assert.Equal(9f, emitter.Position.CylinderHeight);
        Assert.True(emitter.Position.CylinderEdgeOnly);
    }

    [Fact]
    public void Read_KeepsTheThreeVolumesInTheirDeclaredRoles()
    {
        var volumes = Concat(
            Volume(AlamoSpawnShape.Box),
            Volume(AlamoSpawnShape.Sphere),
            Volume(AlamoSpawnShape.Cylinder));

        var emitter = AloParticleReader.Read(System("p", null, Emitter(volumes: volumes))).Emitters[0];

        Assert.Equal(AlamoSpawnShape.Box, emitter.Speed.Shape);
        Assert.Equal(AlamoSpawnShape.Sphere, emitter.Lifetime.Shape);
        Assert.Equal(AlamoSpawnShape.Cylinder, emitter.Position.Shape);
    }

    // ── tracks ────────────────────────────────────────────────────────────────

    [Fact]
    public void Read_FoldsTheEndpointsIntoTheKeyList()
    {
        // The file stores the first and last value in the header and only the middle keys in a list.
        // A consumer wants one ordered curve, so they are folded together.
        var tracks = Concat(
            Track(0f, 1f, AlamoTrackInterpolation.Linear, true, (0.5f, 0.25f)),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, false),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, false),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, false));

        var red = AloParticleReader.Read(System("p", null, Emitter(tracks: tracks)))
            .Emitters[0].Track(AlamoTrackChannel.Red)!;

        Assert.Equal(3, red.Keys.Count);
        Assert.Equal(0f, red.Keys[0].Time);
        Assert.Equal(0.25f, red.Keys[1].Time, 4);
        Assert.Equal(1f, red.Keys[2].Time);
    }

    [Fact]
    public void Read_ScalesTheColourChannelsOutOfBytesButNotTheOthers()
    {
        // The first four tracks store bytes over 255; scale, index and rotation store floats. Using
        // one rule for both makes a scale of 3 read as 0.0117 or a colour of 255 read as garbage.
        var tracks = Concat(
            Track(1f, 0f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(3.5f, 12f, AlamoTrackInterpolation.Linear, false),
            Track(0f, 0f, AlamoTrackInterpolation.Linear, false),
            Track(0f, 0f, AlamoTrackInterpolation.Linear, false));

        var emitter = AloParticleReader.Read(System("p", null, Emitter(tracks: tracks))).Emitters[0];

        var red = emitter.Track(AlamoTrackChannel.Red)!;
        Assert.Equal(1f, red.Keys[0].Value, 2);
        Assert.Equal(0f, red.Keys[^1].Value, 2);

        var scale = emitter.Track(AlamoTrackChannel.Scale)!;
        Assert.Equal(3.5f, scale.Keys[0].Value, 4);
        Assert.Equal(12f, scale.Keys[^1].Value, 4);
    }

    [Fact]
    public void Read_ReadsTheInterpolationMode()
    {
        var tracks = Concat(
            Track(0f, 1f, AlamoTrackInterpolation.Step, true),
            Track(0f, 1f, AlamoTrackInterpolation.Smooth, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, false),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, false),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, false));

        var emitter = AloParticleReader.Read(System("p", null, Emitter(tracks: tracks))).Emitters[0];

        Assert.Equal(AlamoTrackInterpolation.Step,
            emitter.Track(AlamoTrackChannel.Red)!.Interpolation);
        Assert.Equal(AlamoTrackInterpolation.Smooth,
            emitter.Track(AlamoTrackChannel.Green)!.Interpolation);
    }

    [Fact]
    public void Read_NamesEveryTrackByItsChannel()
    {
        var emitter = AloParticleReader.Read(System()).Emitters[0];

        Assert.Equal(
            [
                AlamoTrackChannel.Red, AlamoTrackChannel.Green, AlamoTrackChannel.Blue,
                AlamoTrackChannel.Alpha, AlamoTrackChannel.Scale, AlamoTrackChannel.TextureIndex,
                AlamoTrackChannel.RotationSpeed,
            ],
            emitter.Tracks.Select(t => t.Channel));
    }

    // ── properties ────────────────────────────────────────────────────────────

    [Fact]
    public void Read_KeepsTheEnginesDefaultsForPropertiesTheFileOmits()
    {
        // The exporter writes only what was changed. A reader that zero-initialised instead would
        // give every emitter a lifetime of zero and draw nothing at all.
        var p = AloParticleReader.Read(System()).Emitters[0].Properties;

        Assert.Equal(1f, p.Lifetime);
        Assert.Equal(64, p.TextureSize);
        Assert.Equal(2, p.TriangleCount);
        Assert.Equal(1, p.ParticlesPerSecond);
        Assert.Equal(0.2f, p.Bounciness);
        Assert.Equal(500f, p.WeatherCubeSize);
        Assert.Equal(AlamoParticleBlendMode.Additive, p.BlendMode);
    }

    [Fact]
    public void Read_ReadsTheScalarPropertiesItIsGiven()
    {
        var p = AloParticleReader.Read(System("p", null, Emitter(properties: [
            Mini(0x04, I32((int)AlamoParticleBlendMode.DepthTransparent)),
            Mini(0x0F, F32(2.5f)),
            Mini(0x10, I32(128)),
            Mini(0x2A, I32(40)),
            Mini(0x0C, F32(-9.8f)),
            Mini(0x0A, F32(1f, 2f, 3f)),
            Mini(0x41, [1]),
            Mini(0x42, F32(12f)),
        ]))).Emitters[0].Properties;

        Assert.Equal(AlamoParticleBlendMode.DepthTransparent, p.BlendMode);
        Assert.Equal(2.5f, p.Lifetime);
        Assert.Equal(128, p.TextureSize);
        Assert.Equal(40, p.ParticlesPerSecond);
        Assert.Equal(-9.8f, p.Gravity);
        Assert.Equal(new Vector3(1, 2, 3), p.Acceleration);
        Assert.True(p.HasTail);
        Assert.Equal(12f, p.TailSize);
    }

    [Fact]
    public void Read_StoresTriangleCountOneHigherThanTheFile()
    {
        var p = AloParticleReader.Read(System("p", null,
            Emitter(properties: [Mini(0x05, I32(3))]))).Emitters[0].Properties;

        Assert.Equal(4, p.TriangleCount);
    }

    [Fact]
    public void Read_TreatsMinusOneBurstsAsNone()
    {
        var p = AloParticleReader.Read(System("p", null,
            Emitter(properties: [Mini(0x27, I32(-1))]))).Emitters[0].Properties;

        Assert.Equal(0, p.BurstCount);
    }

    [Fact]
    public void Read_InvertsTheInwardSpeeds()
    {
        // Stored positive, applied inward. Keeping the sign would blow every implosion outwards.
        var p = AloParticleReader.Read(System("p", null, Emitter(properties: [
            Mini(0x09, F32(5f)),
            Mini(0x0B, F32(2f)),
        ]))).Emitters[0].Properties;

        Assert.Equal(-5f, p.InwardSpeed);
        Assert.Equal(-2f, p.InwardAcceleration);
    }

    [Fact]
    public void Read_DiscardsParentLinkStrengthUnlessTheFileEnablesIt()
    {
        var withoutFlag = AloParticleReader.Read(System("p", null,
            Emitter(properties: [Mini(0x28, F32(0.8f))]))).Emitters[0].Properties;
        Assert.Equal(0f, withoutFlag.ParentLinkStrength);

        var withFlag = AloParticleReader.Read(System("p", null, Emitter(properties: [
            Mini(0x28, F32(0.8f)),
            Mini(0x43, [1]),
        ]))).Emitters[0].Properties;
        Assert.Equal(0.8f, withFlag.ParentLinkStrength);
    }

    [Fact]
    public void Read_DerivesTheRandomRotationAverageFromTheRotationTrack()
    {
        // With random rotation on, the engine reinterprets the rotation track's first key as an
        // average and the stored variance as a fraction of it. Skipping this spins the emitter at
        // entirely the wrong rate.
        var tracks = Concat(
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, true),
            Track(1f, 1f, AlamoTrackInterpolation.Linear, false),
            Track(0f, 0f, AlamoTrackInterpolation.Linear, false),
            Track(2.25f, 2.25f, AlamoTrackInterpolation.Linear, false));

        var p = AloParticleReader.Read(System("p", null, Emitter(
            properties: [Mini(0x48, [1]), Mini(0x17, F32(0.9f))],
            tracks: tracks))).Emitters[0].Properties;

        // 0.9 / 2.25 = 0.4, and only the fraction of 2.25 is a speed.
        Assert.Equal(0.4f, p.RandomRotationVariance, 4);
        Assert.Equal(0.25f, p.RandomRotationAverage, 4);
    }

    [Fact]
    public void Read_RejectsAPropertyIdItDoesNotKnow()
    {
        // Strict on purpose: a mini-chunk carries its own length so a stray one is survivable, but
        // skipping it means the file holds a setting the preview silently ignores.
        var alo = System("p", null, Emitter(properties: [Mini(0x7F, I32(1))]));

        Assert.Throws<AloFormatException>(() => AloParticleReader.Read(alo));
    }
}
