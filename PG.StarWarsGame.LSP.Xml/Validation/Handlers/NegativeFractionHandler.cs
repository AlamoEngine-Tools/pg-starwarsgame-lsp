// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A modifier bounded to the closed interval -1.0 to 0.0 - a reduction that can reach total but
///     never becomes a bonus.
/// </summary>
/// <remarks>
///     <c>Error: (%s) Min_Income_Modifier must be between -1.0 and 0.0</c> (<c>015558ec</c>). Both
///     ends are inclusive: -1.0 removes the income entirely and 0.0 leaves it untouched, and both
///     are meaningful settings.
/// </remarks>
public sealed class NegativeFractionHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.NegativeFractionOutOfRange;

    public override string ValidationId => "negative-fraction";

    protected override float? Minimum => -1f;

    protected override float? Maximum => 0f;

    protected override string Expectation => "must be between -1.0 and 0.0";
}
