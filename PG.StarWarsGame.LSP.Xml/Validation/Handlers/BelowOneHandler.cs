// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An upper bound of 1.0, exclusive, with no lower bound stated.
/// </summary>
/// <remarks>
///     <para>
///         <c>Error: (%s) Price_Reduction_Percentage must be &lt; 1.0</c> (<c>015560dc</c>),
///         <c>Time_Reduction_Percentage must be &lt; 1.0</c> (<c>01556204</c>) and
///         <c>Defense_Bonus_Percentage cannot be 1.0 or greater.</c> (<c>01551e1c</c>) - the same
///         rule in two wordings.
///     </para>
///     <para>
///         Separate from <see cref="FractionBelowOneHandler" />, which also floors at zero.
///         <c>Time_Reduction_Percentage</c> carries both rules, from different owning types, so it
///         takes this weaker one until each message is traced to its owner - the same treatment
///         <c>Damage_Amount</c> and <c>Damage_Bonus_Percentage</c> get.
///     </para>
/// </remarks>
public sealed class BelowOneHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ValueNotBelowOne;

    public override string ValidationId => "below-one";

    protected override float? Minimum => null;

    protected override float? Maximum => 1f;

    protected override bool MaximumInclusive => false;

    protected override string Expectation => "must be less than 1.0";
}
