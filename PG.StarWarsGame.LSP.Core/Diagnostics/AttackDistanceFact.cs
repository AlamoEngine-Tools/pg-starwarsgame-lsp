// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     An object writes <c>Targeting_Max_Attack_Distance</c> or a <c>HardPoints</c> list, so its attack
///     distance can be compared with what its hardpoint weapons reach (#101).
/// </summary>
/// <param name="AnchoredOnAttackDistance">
///     True when the position is the <c>Targeting_Max_Attack_Distance</c> value, which a quick fix may
///     replace; false when it is the <c>HardPoints</c> value, which it must not.
/// </param>
/// <remarks>
///     Carries only the object id: both sides of the comparison are routinely inherited, so the handler
///     resolves the effective object and its hardpoints.
/// </remarks>
public sealed record AttackDistanceFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    bool AnchoredOnAttackDistance
) : XmlFact(DocumentUri, Line, Column, Length);
