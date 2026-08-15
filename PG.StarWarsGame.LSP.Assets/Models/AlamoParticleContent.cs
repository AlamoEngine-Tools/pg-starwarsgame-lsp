// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>The shape a randomised emitter property is drawn from.</summary>
public enum AlamoSpawnShape
{
    /// <summary>One exact value, not random at all.</summary>
    Point = 0,
    Box = 1,
    Cube = 2,
    Sphere = 3,
    Cylinder = 4
}

/// <summary>How a track moves between its keys.</summary>
public enum AlamoTrackInterpolation
{
    Linear = 0,
    Smooth = 1,
    Step = 2
}

/// <summary>
///     Which curve a track drives. The order is the file's, and the reader keeps it.
/// </summary>
public enum AlamoTrackChannel
{
    Red = 0,
    Green = 1,
    Blue = 2,
    Alpha = 3,
    Scale = 4,

    /// <summary>Frame index into the texture atlas.</summary>
    TextureIndex = 5,

    RotationSpeed = 6
}

/// <summary>
///     One randomised property: the shape values are drawn from, and its dimensions.
/// </summary>
/// <remarks>
///     The file stores every shape's fields in one fixed 64-byte record whatever the shape is, so all
///     of them are read and only those the shape uses mean anything. Kept whole rather than reduced to
///     the active shape's fields, because a mod switching a shape in the editor expects the numbers it
///     typed for the other shapes to still be there.
/// </remarks>
public sealed record AlamoSpawnVolume(
    AlamoSpawnShape Shape,
    Vector3 Min,
    Vector3 Max,
    float SideLength,
    float SphereRadius,
    bool SphereEdgeOnly,
    float CylinderRadius,
    bool CylinderEdgeOnly,
    float CylinderHeight,
    Vector3 ExactValue);

/// <summary>One key of a track: a value at a normalised time in the particle's life.</summary>
public readonly record struct AlamoTrackKey(float Time, float Value);

/// <summary>
///     A curve over a particle's lifetime, from birth (0) to death (1).
/// </summary>
/// <remarks>
///     The endpoints are stored apart from the middle keys - the file gives a first and last value in
///     the header and any number of keys between - so the reader folds them together into one ordered
///     list, which is what a consumer actually wants to sample.
/// </remarks>
public sealed record AlamoTrack(
    AlamoTrackChannel Channel,
    AlamoTrackInterpolation Interpolation,
    IReadOnlyList<AlamoTrackKey> Keys);

/// <summary>
///     How a sub-mesh's particles composite. Ordinals are the values stored in the file.
/// </summary>
public enum AlamoParticleBlendMode
{
    None = 0,
    Additive = 1,
    Transparent = 2,
    Inverse = 3,
    DepthAdditive = 4,
    DepthTransparent = 5,
    DepthInverse = 6,
    DiffuseTransparent = 7,
    StencilDarken = 8,
    StencilDarkenBlur = 9,
    Heat = 10,
    Bump = 11,
    DecalBump = 12,
    Scanlines = 13
}

/// <summary>What a particle does when it reaches the ground.</summary>
public enum AlamoGroundBehavior
{
    None = 0,
    Disappear = 1,
    Bounce = 2,
    Stick = 3
}

/// <summary>Whether and how an emitter spawns particles from a mesh's geometry.</summary>
public enum AlamoEmitFromMesh
{
    Disabled = 0,
    RandomVertex = 1,
    RandomMesh = 2,
    EveryVertex = 3
}

/// <summary>
///     One emitter's scalar settings.
/// </summary>
/// <remarks>
///     Every default here is the engine's own, from <c>OldEmitter::SetDefaults</c>. They matter: the
///     file omits any property left at its default, so a reader that zero-initialises instead would
///     give every emitter a one-second lifetime of zero and draw nothing.
/// </remarks>
public sealed record AlamoEmitterProperties
{
    public AlamoParticleBlendMode BlendMode { get; init; } = AlamoParticleBlendMode.Additive;

    /// <summary>Stored one less than the real count.</summary>
    public int TriangleCount { get; init; } = 2;

    public bool UseBursts { get; init; }
    public bool LinkToSystem { get; init; }
    public float InwardSpeed { get; init; }
    public Vector3 Acceleration { get; init; }
    public float InwardAcceleration { get; init; }
    public float Gravity { get; init; }
    public float Lifetime { get; init; } = 1f;
    public int TextureSize { get; init; } = 64;
    public float RandomScalePercent { get; init; }
    public float RandomLifetimePercent { get; init; }
    public float RandomRotationVariance { get; init; }
    public bool RandomRotationDirection { get; init; }
    public float InitialDelay { get; init; }
    public float BurstDelay { get; init; } = 1f;
    public int ParticlesPerBurst { get; init; } = 1;
    public int BurstCount { get; init; }
    public float ParentLinkStrength { get; init; }
    public int ParticlesPerSecond { get; init; } = 1;
    public Vector4 RandomColors { get; init; }
    public bool ColorAddGrayscale { get; init; }
    public bool WorldOriented { get; init; }
    public AlamoGroundBehavior GroundBehavior { get; init; } = AlamoGroundBehavior.None;
    public float Bounciness { get; init; } = 0.2f;
    public bool AffectedByWind { get; init; }
    public float FreezeTime { get; init; }
    public float SkipTime { get; init; }
    public AlamoEmitFromMesh EmitFromMesh { get; init; } = AlamoEmitFromMesh.Disabled;
    public bool ObjectSpaceAcceleration { get; init; }
    public bool IsHeatParticle { get; init; }
    public float EmitFromMeshOffset { get; init; } = 0.5f;
    public bool IsWeatherParticle { get; init; }
    public float WeatherCubeSize { get; init; } = 500f;
    public float WeatherFadeoutDistance { get; init; } = 100f;
    public bool HasTail { get; init; }
    public float TailSize { get; init; } = 50f;
    public bool NoDepthTest { get; init; }
    public float WeatherCubeDistance { get; init; }
    public bool RandomRotation { get; init; }

    /// <summary>
    ///     Average rotation speed, derived rather than stored.
    /// </summary>
    /// <remarks>
    ///     When <see cref="RandomRotation" /> is set the engine reinterprets the rotation-speed
    ///     track's first key as an average and the stored variance as a fraction of it. Reproduced
    ///     because without it a randomly-rotating emitter spins at entirely the wrong rate.
    /// </remarks>
    public float RandomRotationAverage { get; init; }
}

/// <summary>One emitter of a particle system.</summary>
/// <param name="SpawnOnDeath">
///     Index of the emitter to start when a particle of this one dies, or <c>-1</c>. This is how the
///     chained effects work - an explosion spawning its own smoke.
/// </param>
/// <param name="SpawnDuringLife">Index of an emitter that runs alongside each particle, or <c>-1</c>.</param>
public sealed record AlamoEmitter(
    string Name,
    string ColorTexture,
    string? NormalTexture,
    AlamoSpawnVolume Speed,
    AlamoSpawnVolume Lifetime,
    AlamoSpawnVolume Position,
    IReadOnlyList<AlamoTrack> Tracks,
    int SpawnOnDeath,
    int SpawnDuringLife,
    AlamoEmitterProperties Properties)
{
    /// <summary>The track for one channel, or null when the file carried none.</summary>
    public AlamoTrack? Track(AlamoTrackChannel channel)
    {
        foreach (var track in Tracks)
            if (track.Channel == channel)
                return track;

        return null;
    }
}

/// <summary>
///     A parsed particle system.
/// </summary>
/// <remarks>
///     Format version 1 only, which is not a limitation in practice: every one of the 987 particle
///     files across the two shipped trees is v1. Version 2 is a Universe at War format built from
///     around sixty plugin types, and <see cref="AloParticleReader" /> refuses it outright rather than
///     mis-reading it.
/// </remarks>
public sealed record AlamoParticleContent(
    string Name,
    bool LeaveParticles,
    IReadOnlyList<AlamoEmitter> Emitters);
