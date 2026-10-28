// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     A hardpoint sets a unit-mode row - a <c>*_Mode_Weapon_Delay_Multiplier</c> or a
///     <c>Fire_When_In_*_Mode</c> flag - that the attaching object can never put to use.
/// </summary>
/// <remarks>
///     Measured on the game: the rows are read for the owner's first and second unit ability only,
///     keyed by the ability's type, and only while that ability is active; the rocket flag is read
///     against a ROCKET_ATTACK ability in any slot. So the row applies only when the owner's list
///     carries the matching type in a consulted slot.
/// </remarks>
/// <param name="TagName">The row's tag, as written on the hardpoint.</param>
/// <param name="AbilityType">The ability type the row needs on the owner.</param>
/// <param name="OwnerId">The object attaching the hardpoint.</param>
/// <param name="SlotOfType">
///     -1 when the owner has no ability of that type; otherwise the zero-based slot it sits in,
///     which is then 2 or more - a slot the game never asks the hardpoint about.
/// </param>
/// <param name="OwnerAbilities">The owner's ability types in slot order, for the message.</param>
public sealed record HardpointModeRowWithoutAbilityFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string HardpointId,
    string TagName,
    string AbilityType,
    string OwnerId,
    int SlotOfType,
    IReadOnlyList<string> OwnerAbilities
) : XmlFact(DocumentUri, Line, Column, Length);