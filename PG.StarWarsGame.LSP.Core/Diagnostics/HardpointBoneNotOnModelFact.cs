// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A bone a hardpoint names is absent from a model of an object that attaches it (#53). Reported
///     only when the model's bone list could actually be read - see
///     <see cref="HardpointModelBonesUnavailableFact" /> for the other case - so this is never a
///     consequence of an unreadable .alo.
/// </summary>
/// <param name="HardpointId">The hardpoint naming the bone.</param>
/// <param name="TagName">The tag the bone came from (Attachment_Bone, Fire_Bone_A, ...).</param>
/// <param name="BoneName">The bone that is missing.</param>
/// <param name="ModelName">The model that lacks it.</param>
/// <param name="OwnerId">The object whose model it is - the object attaching the hardpoint, or the hardpoint itself for its own Model_To_Attach.</param>
/// <param name="AttachedModelName">
///     The hardpoint's own <c>Model_To_Attach</c>, where that was ALSO checked and also lacks the
///     bone. Only set for a tag that may resolve against either model - <c>Collision_Mesh</c> - and
///     only when that model's bone list could be read, so the report can say both were examined
///     without claiming anything about a model nobody could open.
/// </param>
/// <param name="SuggestedName">
///     The one name on the checked models that STARTS WITH what was written, offered as the quick fix -
///     or null. Only for <c>Collision_Mesh</c>, whose measured failure is a truncated suffix: every
///     Gargantuan hardpoint writes <c>..._COL</c> or <c>..._COLL</c> against models carrying
///     <c>..._COLLISION</c>. Only when exactly one name qualifies, because a quick fix must not guess.
///     And only when the fact is anchored on the tag's value: a quick fix replaces the diagnostic's
///     range, and from the attaching object's file that range is the hardpoint's id in its
///     <c>HardPoints</c> list.
/// </param>
public sealed record HardpointBoneNotOnModelFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string HardpointId,
    string TagName,
    string BoneName,
    string ModelName,
    string OwnerId,
    string? AttachedModelName = null,
    string? SuggestedName = null
) : XmlFact(DocumentUri, Line, Column, Length);
