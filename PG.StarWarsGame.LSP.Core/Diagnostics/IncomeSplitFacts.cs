// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an income stream splits in the owner's favour by a share the engine will not
///     accept.
/// </summary>
/// <remarks>
///     <para>
///         Conditional, and that is the whole shape of it: the engine tests the share only when
///         <c>Split_Favors_Owner</c> AND <c>Split_Income_With_Allies</c> are both on. With either
///         flag off the value is never read, so a range check on the tag alone would report a value
///         nothing looks at.
///     </para>
///     <para>
///         The accepted range is <c>[0, 1)</c>. The engine's message says "greater than zero", but
///         its test is <c>(x &lt; 0.0) || (1.0 &lt;= x)</c>, so zero passes - the comparison is the
///         specification and the prose is not.
///     </para>
/// </remarks>
/// <param name="Value">The share as authored, for the message.</param>
/// <param name="Clamped">
///     What <c>Clamp(x, 0.0, 0.99)</c> makes of it - 0.0 below the range and 0.99 above, never 1.0.
/// </param>
public sealed record OwnerIncomeShareFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    double Value,
    string Clamped) : XmlFact(DocumentUri, Line, Column, Length);

/// <summary>
///     Observation: a flag combination on an income stream that the engine refuses and clears.
/// </summary>
/// <remarks>
///     Two of these, both stated in <c>IncomeStreamAbilityClass::Validate_Data</c> and both repaired
///     by the engine turning flags off. They share an id because they are one concern from the
///     author's side - <c>Split_Favors_Owner</c> set where it cannot mean anything - and someone
///     silencing one would mean the other.
/// </remarks>
/// <param name="FlagTag">The flag the diagnostic is anchored on.</param>
/// <param name="OtherTag">The tag it conflicts with, or the one whose absence makes it moot.</param>
/// <param name="Problem">
///     What is wrong with the combination, as a sentence fragment completing "&lt;FlagTag&gt; ...".
///     The two rules read differently - one flag is inert without its partner, the other is refused
///     alongside it - so the wording belongs to the rule that measured it.
/// </param>
/// <param name="Consequence">What the engine does about it, as a sentence fragment.</param>
/// <param name="Repair">The engine's own correction, where every tag it clears is actually present.</param>
public sealed record IncomeSplitConflictFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string FlagTag,
    string OtherTag,
    string Problem,
    string Consequence,
    XmlEngineRepair? Repair) : XmlFact(DocumentUri, Line, Column, Length);
