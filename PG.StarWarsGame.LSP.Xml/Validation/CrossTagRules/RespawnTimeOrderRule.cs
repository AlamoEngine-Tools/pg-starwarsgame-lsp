// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     A respawn window whose ends are the wrong way round.
/// </summary>
/// <remarks>
///     <para>
///         <c>Error: (%s) Min_Respawn_Time cannot be greater than Max_Respawn_Time!</c>
///         (<c>01503440</c>). The engine picks a time between the two, so a reversed pair leaves it
///         with an empty interval.
///     </para>
///     <para>
///         Equality is fine and meaningful - it pins the respawn to an exact delay - so only a
///         strictly greater minimum is reported. The companion rule at <c>01503488</c>, that both
///         must be 0.0 or greater, is covered per tag by the non-negative range check.
///     </para>
/// </remarks>
public sealed class RespawnTimeOrderRule : TagComparisonRuleBase
{
    protected override string LeftTag => "Min_Respawn_Time";

    protected override string RightTag => "Max_Respawn_Time";

    protected override string Expectation => "cannot be greater than";

    protected override bool IsAcceptable(double left, double right)
    {
        return left <= right;
    }
}
