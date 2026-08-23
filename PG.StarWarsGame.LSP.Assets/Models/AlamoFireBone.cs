// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>
///     Where a weapon bone actually points.
/// </summary>
/// <remarks>
///     <para>
///         A fire bone aims along its own local <b>X</b> axis, not the <b>Y</b> that reads as
///         "forward". Everything that draws a fire arc, places a muzzle flash or sends a projectile
///         must go through here rather than taking the bone's transform at face value.
///     </para>
///     <para>
///         The naive reading is wrong by a quarter turn about the vertical, and mirrored hulls make
///         that mistake hide itself: on <c>Ev_stardestroyer.alo</c> the port mounts'
///         local Y bears 0.57 <em>astern</em> while the starboard mounts' bears 0.57
///         <em>forward</em>, so a spot check on one side of the ship looks plausible and the other
///         side is 90 degrees out. Measured across all 12 of its <c>FP_*</c> bones; pinned by
///         <c>FireBoneAimAxisTest</c>, which asserts both sides together for exactly that reason.
///     </para>
///     <para>
///         Alamo is Z-up: the quarter turn is about Z, so this is a heading error, not a pitch one.
///         Elevated mounts keep their pitch - the middle mounts on the Star Destroyer carry a real
///         +0.64 Z component - so this rotates the aim without flattening it.
///     </para>
/// </remarks>
public static class AlamoFireBone
{
    /// <summary>
    ///     The unit direction <paramref name="bone" /> fires along, in MODEL space.
    /// </summary>
    /// <remarks>
    ///     Taken from the bone's absolute transform, so the whole parent chain is already applied.
    ///     Falls back to the model's forward when the bone carries a degenerate basis, which a
    ///     zero-scaled bone in a broken export can.
    /// </remarks>
    public static Vector3 AimDirection(AlamoModelBone bone)
    {
        ArgumentNullException.ThrowIfNull(bone);

        var m = bone.AbsoluteTransform;
        var axis = new Vector3(m.M11, m.M12, m.M13);

        return axis.LengthSquared() < 1e-12f ? -Vector3.UnitY : Vector3.Normalize(axis);
    }
}
