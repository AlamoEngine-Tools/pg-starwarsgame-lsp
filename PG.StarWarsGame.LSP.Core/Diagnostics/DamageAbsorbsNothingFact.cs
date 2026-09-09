// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an object configures damage absorption that resolves to nothing.
/// </summary>
/// <remarks>
///     The two tags are one setting: <c>healed = (projectile damage * Damage_Absorb_Percentage) +
///     Damage_Absorb_Amount</c>. Either term may be zero - a flat-only or percentage-only absorb is
///     a normal configuration - but with both at zero the ability still triggers and still absorbs
///     nothing, which is the kind of defect that reads as "the ability is broken" in play and shows
///     up nowhere in the file.
/// </remarks>
/// <param name="Percentage">The percentage term as authored, or null when the tag is absent.</param>
/// <param name="Amount">The flat term as authored, or null when the tag is absent.</param>
public sealed record DamageAbsorbsNothingFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    double? Percentage,
    double? Amount) : XmlFact(DocumentUri, Line, Column, Length);
