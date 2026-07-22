// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A bone a hardpoint names is absent from a model of an object that mounts it (#53). Reported
///     only when the model's bone list could actually be read - see
///     <see cref="HardpointModelBonesUnavailableFact" /> for the other case - so this is never a
///     consequence of an unreadable .alo.
/// </summary>
/// <param name="HardpointId">The hardpoint naming the bone.</param>
/// <param name="TagName">The tag the bone came from (Attachment_Bone, Fire_Bone_A, ...).</param>
/// <param name="BoneName">The bone that is missing.</param>
/// <param name="ModelName">The model that lacks it.</param>
/// <param name="OwnerId">The object whose model it is - the mounting unit, or the hardpoint itself for its own Model_To_Attach.</param>
public sealed record HardpointBoneNotOnModelFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string HardpointId,
    string TagName,
    string BoneName,
    string ModelName,
    string OwnerId
) : XmlFact(DocumentUri, Line, Column, Length);
