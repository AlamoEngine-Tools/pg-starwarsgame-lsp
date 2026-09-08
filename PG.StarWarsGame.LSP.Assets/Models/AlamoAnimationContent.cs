// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>
///     One bone's pose on one frame, in its parent's space.
/// </summary>
/// <param name="Visible">
///     Whether the bone is drawn on this frame. Animations switch geometry on and off this way -
///     it is how a destroyed section appears mid-sequence - so it is a track, not a constant.
/// </param>
public readonly record struct AlamoAnimationFrame(
    Vector3 Scale,
    Quaternion Rotation,
    Vector3 Translation,
    bool Visible);

/// <summary>
///     Every frame of one animated bone.
/// </summary>
/// <param name="BoneIndex">
///     Index into the <em>model's</em> bone list. An animation only describes the bones it moves, so
///     these are sparse; a consumer fills the gaps from the model's own rest pose.
/// </param>
/// <param name="Name">
///     The bone name as stored. Matching it against the model's bone of the same index is what
///     confirms an animation belongs to a given model - the check that makes it possible to work out
///     which of 1363 <c>.ala</c> files pair with which <c>.alo</c>.
/// </param>
public sealed record AlamoAnimationBone(
    int BoneIndex,
    string Name,
    IReadOnlyList<AlamoAnimationFrame> Frames);

/// <summary>
///     A parsed <c>.ala</c> animation.
/// </summary>
/// <remarks>
///     <para>
///         Transforms are <strong>bone-local</strong>, unlike <c>Animations.cpp</c>, which composes
///         each bone through its parent before handing anything back. glTF animation tracks are
///         bone-local too, so composing here would mean decomposing again in the exporter - two lossy
///         trips through matrix decomposition to arrive back where we started. Staying local also
///         means the reader needs no model, keeping it a pure description of one file.
///     </para>
///     <para>
///         Only the bones the animation actually drives appear. Everything else holds its rest pose,
///         which lives in the model, not here.
///     </para>
/// </remarks>
/// <param name="FrameCount">
///     Frames as stored. The <strong>last frame duplicates the first</strong> so the engine can loop
///     without special-casing the wrap, which means the true period is
///     <c>(FrameCount - 1) / Fps</c> seconds. Kept as stored rather than silently decremented,
///     because glTF wants every sample including the duplicate - that is exactly what makes its loop
///     seamless.
/// </param>
/// <param name="FormatVersion">
///     1 or 2. The two differ only in where samples live - v1 per bone, v2 in shared blocks - and both
///     are in live use, so this is worth reporting rather than hiding: it is the fact a corpus check
///     needs to prove both paths are still exercised.
/// </param>
public sealed record AlamoAnimationContent(
    float Fps,
    int FrameCount,
    IReadOnlyList<AlamoAnimationBone> Bones,
    int FormatVersion)
{
    /// <summary>The animation's period in seconds, excluding the duplicated final frame.</summary>
    public float Duration => Fps > 0 ? Math.Max(0, FrameCount - 1) / Fps : 0f;

    /// <summary>
    ///     Whether this animation's bones line up with <paramref name="modelBoneNames" />.
    /// </summary>
    /// <remarks>
    ///     The pairing test the viewer uses: every driven bone must exist at the index it claims and
    ///     carry the name it claims. Filename prefixes get the candidate list down to a handful -
    ///     1342 of 1363 shipped animations match their model that way - and this settles it.
    ///     Case-insensitive, because the engine uppercases bone names and the files do not.
    /// </remarks>
    public bool MatchesModel(IReadOnlyList<string> modelBoneNames)
    {
        return WhyNotModel(modelBoneNames) is null;
    }

    /// <summary>
    ///     Whether this animation can be APPLIED to a skeleton, whatever its bones are called there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A weaker question than <see cref="MatchesModel" />, and the one that decides whether a
    ///         clip is playable. The user reported that the identical-skeleton rule fires all over the
    ///         BASE GAME - on units that animate perfectly well in the engine - so an exact name match
    ///         is not what the engine requires, and enforcing it dropped clips the game plays.
    ///     </para>
    ///     <para>
    ///         An index past the end of the bone list is different in kind: there is no node to drive,
    ///         so the track cannot be written whatever anyone decides about names. That is a hard
    ///         limit rather than a strictness setting, and it stays.
    ///     </para>
    ///     <para>
    ///         Use <see cref="MatchesModel" /> where the job is to CHOOSE which model a loose clip
    ///         belongs to - there, a name disagreement is the evidence that settles it.
    ///     </para>
    /// </remarks>
    public bool FitsSkeleton(IReadOnlyList<string> modelBoneNames)
    {
        ArgumentNullException.ThrowIfNull(modelBoneNames);

        return Bones.All(bone => bone.BoneIndex >= 0 && bone.BoneIndex < modelBoneNames.Count);
    }

    /// <summary>
    ///     Why this animation does not pair with a model, or <see langword="null" /> when it does.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="MatchesModel" /> so a caller that DROPS a clip can say what it
    ///     dropped and why. A silent mismatch is indistinguishable from a missing file: a reader
    ///     who opens a model and finds an animation missing has no way to tell a clip that was
    ///     rejected from one that was never there. The two reasons are exactly the two ways a
    ///     pairing fails, and which one it is says whether to suspect the model or the clip.
    /// </remarks>
    public string? WhyNotModel(IReadOnlyList<string> modelBoneNames)
    {
        ArgumentNullException.ThrowIfNull(modelBoneNames);

        foreach (var bone in Bones)
        {
            if (bone.BoneIndex < 0 || bone.BoneIndex >= modelBoneNames.Count)
                return $"it drives bone {bone.BoneIndex} ('{bone.Name}') and the model has " +
                       $"{modelBoneNames.Count}";

            if (!string.Equals(modelBoneNames[bone.BoneIndex], bone.Name,
                    StringComparison.OrdinalIgnoreCase))
                return $"bone {bone.BoneIndex} is '{bone.Name}' here and " +
                       $"'{modelBoneNames[bone.BoneIndex]}' on the model";
        }

        return null;
    }
}
