// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A lower bound of -1.0, INCLUSIVE, with no upper bound.
/// </summary>
/// <remarks>
///     <para>
///         <c>Error: (%s) Percentage_Income_Modifier cannot be less than -1.0</c>
///         (<c>01555778</c>). <c>PlanetIncomeBonusAbilityClass::Validate_Data</c>
///         (<c>01014820</c>) tests <c>x &lt;= -1.0 &amp;&amp; x != -1.0</c>, which is exactly
///         <c>x &lt; -1.0</c>, so -1.0 itself passes - a total loss of income, which is a sensible
///         thing to be able to write.
///     </para>
///     <para>
///         One boundary away from <see cref="BonusPercentageHandler" /> and deliberately not it.
///         The eight <c>*_Bonus_Percentage</c> tags say "cannot be -1.0 or less" and do reject
///         -1.0; this one says "cannot be less than -1.0" and does not. The wordings differ by two
///         words and the comparisons differ by a boundary, so the only safe way to tell them apart
///         is the decompiled test.
///     </para>
/// </remarks>
public sealed class AtLeastNegativeOneHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ValueBelowNegativeOne;

    public override string ValidationId => "at-least-negative-one";

    protected override float? Minimum => -1f;

    protected override float? Maximum => null;

    protected override string Expectation => "cannot be less than -1.0";
}
