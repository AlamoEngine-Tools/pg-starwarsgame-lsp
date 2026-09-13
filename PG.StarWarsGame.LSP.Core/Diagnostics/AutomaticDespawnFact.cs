// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an ability that fires automatically also despawns its owner.
/// </summary>
/// <remarks>
///     <para>
///         Stated once, on the base class, and therefore true of every ability type:
///         <c>SpecialAbilityClass::Validate_Data</c> complains and then sets
///         <c>CausesDespawn = false</c> itself. The ability keeps running; it just no longer does
///         the thing the author asked for.
///     </para>
///     <para>
///         Neither tag is wrong alone - an automatic style is ordinary, and a despawning ability is
///         ordinary. Only the pair is refused.
///     </para>
/// </remarks>
/// <param name="ActivationStyle">The style as written, so the message can quote the author's own value.</param>
/// <param name="Repair">
///     The engine's own correction as an edit over the <c>Causes_Despawn</c> VALUE - it assigns
///     false. Built by the rule, which is what holds the parsed node and the engine knowledge.
///     Null when the value spans lines, where a single-line replacement would be wrong; the
///     diagnostic still stands, it just carries no fix.
/// </param>
public sealed record AutomaticDespawnFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ActivationStyle,
    XmlDiagnosticEdit? Repair = null) : XmlFact(DocumentUri, Line, Column, Length);
