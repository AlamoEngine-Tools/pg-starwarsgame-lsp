// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>What an <c>.alo</c> file turned out to hold.</summary>
public enum AloFileKind
{
    /// <summary>Neither of the two known root chunks - not an Alamo asset this build understands.</summary>
    Unknown,

    /// <summary>A model: skeleton, meshes, connections. <see cref="AloModelReader" /> reads it.</summary>
    Model,

    /// <summary>A particle system. <see cref="AloParticleReader" /> reads it.</summary>
    Particle
}

/// <summary>
///     Which of the two <c>.alo</c> formats a file holds.
/// </summary>
/// <remarks>
///     Models and particle systems share the extension, so the name says nothing about which reader
///     applies - <c>Data\Art\Models\</c> holds 2353 of the first and 987 of the second, side by side.
///     Classifying up front turns "this file will not parse as a model" into "this is a particle
///     system", which is the difference between an error and a preview.
/// </remarks>
public static class AloFile
{
    private const uint ModelRoot = 0x200;
    private const uint ParticleRootV1 = 0x900;

    /// <summary>The v2 particle root. Still a particle system, even though the reader refuses it.</summary>
    private const uint ParticleRootV2 = 0x1500;

    /// <summary>
    ///     Classifies a buffer by its root chunk type.
    /// </summary>
    /// <remarks>
    ///     Reads the first four bytes only. This runs over whole directories to decide what to even
    ///     attempt, so it must not cost a parse.
    /// </remarks>
    public static AloFileKind Classify(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length < sizeof(uint))
            return AloFileKind.Unknown;

        return BitConverter.ToUInt32(bytes, 0) switch
        {
            ModelRoot => AloFileKind.Model,
            ParticleRootV1 or ParticleRootV2 => AloFileKind.Particle,
            _ => AloFileKind.Unknown
        };
    }
}
