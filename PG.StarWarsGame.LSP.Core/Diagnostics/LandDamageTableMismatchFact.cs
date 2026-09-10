// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     An object's <c>Land_Damage_Thresholds</c> and <c>Land_Damage_Alternates</c> carry a different
///     number of entries, so the tail of the longer column pairs with nothing.
/// </summary>
/// <remarks>
///     The two columns are one positional table: entry <c>n</c> of each belongs to the same damage
///     stage. The third column, <c>Land_Damage_SFX</c>, is deliberately not part of this fact -
///     vanilla disagrees on it constantly, and the numbers are in <c>LandDamageTableRule</c>.
/// </remarks>
public sealed record LandDamageTableMismatchFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    IReadOnlyList<string> ThresholdEntries,
    IReadOnlyList<string> AlternateEntries,
    (int Line, int Column, int Length) ThresholdsPosition,
    (int Line, int Column, int Length) AlternatesPosition
) : XmlFact(DocumentUri, Line, Column, Length)
{
    public int Thresholds => ThresholdEntries.Count;

    public int Alternates => AlternateEntries.Count;
}
