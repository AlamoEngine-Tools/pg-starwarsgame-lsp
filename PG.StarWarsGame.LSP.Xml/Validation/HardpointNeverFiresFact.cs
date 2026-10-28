// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     A hardpoint whose rocket-attack flags leave it no state to fire in.
/// </summary>
/// <remarks>
///     Measured on the game: with <c>Fire_When_In_Normal_Attack_Mode</c> off, the hardpoint fires
///     only while a ROCKET_ATTACK ability is active; with <c>Fire_When_In_Rocket_Attack_Mode</c>
///     off, it does not fire while one is. Both off is a weapon that never fires; the normal flag
///     off on a unit with no ROCKET_ATTACK ability is the same weapon.
/// </remarks>
/// <param name="OwnerId">
///     The object attaching the hardpoint; null when both flags are off, which holds for any owner.
/// </param>
/// <param name="BothFlagsOff">
///     True when both flags are off, which kills the weapon on any owner; false when only the
///     normal flag is off and the owner has no ROCKET_ATTACK ability.
/// </param>
public sealed record HardpointNeverFiresFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string HardpointId,
    string? OwnerId,
    bool BothFlagsOff
) : XmlFact(DocumentUri, Line, Column, Length);