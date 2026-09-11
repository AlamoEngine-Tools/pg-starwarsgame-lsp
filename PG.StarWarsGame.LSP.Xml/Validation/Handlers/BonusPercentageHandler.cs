// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Flags a bonus percentage of -1.0 or below, which the engine rejects on load.
/// </summary>
/// <remarks>
///     <para>
///         These tags are multipliers the engine adds to 1.0, so -1.0 cancels the stat outright and
///         anything past it would invert its sign. The engine states the rule about nine of them,
///         e.g. <c>Error: (%s) Health_Bonus_Percentage cannot be -1.0 or less.</c>
///         (<c>01552038</c>).
///     </para>
///     <para>
///         The bound is exclusive - a value of exactly -1.0 is rejected - which is what separates
///         this from a plain lower-bound check. Negative values above it are legitimate penalties
///         and appear in shipped data.
///     </para>
/// </remarks>
public sealed class BonusPercentageHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.BonusPercentageTooLow;

    public override string ValidationId => "bonus-percentage";

    protected override float? Minimum => -1f;

    protected override float? Maximum => null;

    protected override bool MinimumInclusive => false;

    protected override string Expectation =>
        "must be greater than -1.0, since -1.0 would cancel the stat outright";
}
