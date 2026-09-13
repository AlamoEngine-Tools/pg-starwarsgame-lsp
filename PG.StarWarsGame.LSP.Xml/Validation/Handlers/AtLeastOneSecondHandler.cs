// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A duration the engine will not run shorter than one second.
/// </summary>
/// <remarks>
///     <c>LeechShieldsAbilityClass::Validate_Data</c> (<c>01028000</c>) tests
///     <c>Duration &lt;= 1.0 &amp;&amp; Duration != 1.0</c>, which is <c>Duration &lt; 1.0</c>, and
///     then assigns <c>Duration = 1.0</c>. The bound is therefore INCLUSIVE and the repair value is
///     the bound itself - a half-second leech runs for a full second and the file still says half.
/// </remarks>
public sealed class AtLeastOneSecondHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DurationBelowOneSecond;

    public override string ValidationId => "at-least-one-second";

    protected override float? Minimum => 1.0f;

    protected override float? Maximum => null;

    protected override string Expectation => "should have at least one second of time";

    /// <summary>
    ///     Measured: the line after the message is <c>this-&gt;Duration = 1.0</c>, so the bound and
    ///     the repair are the same number here. They are not always - see
    ///     <see cref="NumericRangeHandlerBase.RepairValue" />.
    /// </summary>
    protected override string? RepairValue => "1.0";
}
