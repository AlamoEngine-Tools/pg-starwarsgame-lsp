// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     An object's uniform render scale, read the way the engine reads it.
/// </summary>
/// <remarks>
///     <para>
///         Shared because two callers need the same answer and were about to have two copies: the
///         scene builder, which sends it so the whole subject can be drawn in world space, and the
///         particle handler, which has sent it since the hero powerup effects were found drawing at
///         a twentieth of their size.
///     </para>
///     <para>
///         The guard is the reference's own (<c>GameObjectCatalog.cpp</c>): anything non-finite or
///         non-positive falls back to 1, because a zero or negative scale collapses the object
///         rather than sizing it.
///     </para>
/// </remarks>
public static class EngineScaleFactor
{
    /// <summary>The identity - what an object that declares nothing is drawn at.</summary>
    public const float None = 1f;

    /// <summary>
    ///     The scale a <c>Scale_Factor</c> tag value means, or <see cref="None" />.
    /// </summary>
    public static float Of(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return None;

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && float.IsFinite(value) && value > 0f
            ? value
            : None;
    }
}
