// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A hardpoint is declared <c>Is_Destroyable</c> but has no <c>Attachment_Bone</c>. The engine has
///     nothing to attach it to, and the result is not a visual glitch: the hardpoint becomes
///     <b>indestructible</b>, contradicting its own declaration and silently removing a weak point the
///     unit was supposed to have.
///     <para>
///         Only destroyable hardpoints qualify - one that is deliberately not destroyable has nothing to
///         attach and legitimately omits the bone, which is the case for all 133 such hardpoints in
///         vanilla EaW and FoC.
///     </para>
///     <para>
///         Reported without needing model data - the tag is either present or it is not - which is why
///         this is an error rather than a warning, unlike the bone-exists-on-model checks that depend on
///         how completely the .alo models could be read.
///     </para>
/// </summary>
/// <param name="HardpointId">Name of the offending hardpoint, for the message.</param>
public sealed record HardpointMissingAttachmentBoneFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string HardpointId
) : XmlFact(DocumentUri, Line, Column, Length);
