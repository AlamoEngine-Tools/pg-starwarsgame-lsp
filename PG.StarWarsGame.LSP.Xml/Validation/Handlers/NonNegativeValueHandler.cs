// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Flags a negative value on a tag the engine requires to be zero or greater.
/// </summary>
/// <remarks>
///     The engine states this rule about 29 tags in three wordings that mean the same thing:
///     <c>Error: (%s) Damage_Absorb_Amount must be 0 or greater.</c> (<c>0155100c</c>),
///     <c>Damage_Amount cannot be less than zero</c> and
///     <c>Charging_Time_In_Seconds must be greater than or equal to zero</c>. It checks at load, so
///     the modder currently hears about it only when the ability misbehaves.
/// </remarks>
public sealed class NonNegativeValueHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ValueMustBeNonNegative;

    public override string ValidationId => "non-negative-value";

    protected override float? Minimum => 0f;

    protected override float? Maximum => null;

    protected override string Expectation => "cannot be less than zero";
}
