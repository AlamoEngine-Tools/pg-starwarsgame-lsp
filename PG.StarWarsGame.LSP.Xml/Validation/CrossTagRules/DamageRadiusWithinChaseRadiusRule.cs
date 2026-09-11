// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Buzz droids that damage further than they will chase.
/// </summary>
/// <remarks>
///     <c>Error: (%s) Damage_Radius must be less than Chase_Radius.</c> (<c>01558a44</c>). The
///     droids chase a target and damage it once close enough, so a damage radius at or beyond the
///     chase radius describes a state the approach can never reach.
/// </remarks>
public sealed class DamageRadiusWithinChaseRadiusRule : TagComparisonRuleBase
{
    protected override string LeftTag => "Damage_Radius";

    protected override string RightTag => "Chase_Radius";

    protected override string Expectation => "must be less than";

    // "less than", not "at most": the engine rejects equality too.
    protected override bool IsAcceptable(double left, double right)
    {
        return left < right;
    }
}
