// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Flags a zero or negative value on a tag the engine requires to be strictly greater than zero.
/// </summary>
/// <remarks>
///     <para>
///         The engine states this rule about 37 tags, in three wordings:
///         <c>Error: (%s) Damage_Radius must be greater than zero.</c>,
///         <c>Berserker_Damage cannot be less than or equal to zero</c> and
///         <c>Neutralization_Cost_Multiplier must be a positive value!</c>
///     </para>
///     <para>
///         Distinct from <see cref="NonNegativeValueHandler" /> only in whether zero passes. Zero is
///         the value that actually turns up in data - a radius or an interval left at its default -
///         so the two must not be conflated.
///     </para>
///     <para>
///         <c>DamageNonzeroHandler</c> makes the same check for <c>Damage</c> alone, with a message
///         about the AI declining to use the unit. That one stays: it says something this cannot.
///     </para>
/// </remarks>
public sealed class PositiveValueHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ValueMustBePositive;

    public override string ValidationId => "positive-value";

    protected override float? Minimum => 0f;

    protected override float? Maximum => null;

    protected override bool MinimumInclusive => false;

    protected override string Expectation => "must be greater than zero";
}
