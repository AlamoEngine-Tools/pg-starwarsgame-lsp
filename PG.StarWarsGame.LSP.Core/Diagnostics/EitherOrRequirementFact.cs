// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an ability has both halves of an either/or flag pair switched off, so it loads
///     and does nothing.
/// </summary>
/// <remarks>
///     <para>
///         Three of these, one per ability class, each stated as "You should set either A or B to
///         'Yes', otherwise this ability won't do anything". Unlike the gated requirements, the
///         engine does NOT repair the object here - it leaves the ability inert and says so once at
///         load.
///     </para>
///     <para>
///         Neither flag is wrong alone, which is what makes this a cross-tag rule: a No is the
///         ordinary value for whichever half an ability is not meant to cover. Only the combination
///         is the defect, so it is reported once against the object rather than twice against the
///         flags.
///     </para>
/// </remarks>
/// <param name="OwningType">The ability type, for the message.</param>
/// <param name="FirstTag">One half of the pair, in the order the engine names them.</param>
/// <param name="SecondTag">The other half.</param>
/// <param name="State">
///     How to describe both halves being unset - "off" for a pair of flags, "empty" for a pair of
///     lists. The tags decide what "set" means, so they decide how to say it.
/// </param>
/// <param name="Consequence">What the object cannot do, in the engine's own words where it gives them.</param>
/// <param name="Remedy">What the author should do about it.</param>
public sealed record EitherOrRequirementFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string OwningType,
    string FirstTag,
    string SecondTag,
    string State = "off",
    string Consequence = "does nothing",
    string Remedy = "set one of them to Yes") : XmlFact(DocumentUri, Line, Column, Length);
