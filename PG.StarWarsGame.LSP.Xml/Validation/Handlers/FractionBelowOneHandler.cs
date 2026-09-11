// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A fraction in the half-open interval [0.0, 1.0) - zero allowed, one not.
/// </summary>
/// <remarks>
///     <c>Error: (%s) Retreat_Loss_Mitigation must be &gt;= 0.0 and &lt; 1.0</c> (<c>0155659c</c>).
///     These are proportions subtracted from something, so 1.0 would reduce it to nothing and the
///     engine refuses rather than let a total reduction pass as a fraction.
/// </remarks>
public sealed class FractionBelowOneHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.FractionNotBelowOne;

    public override string ValidationId => "fraction-below-one";

    protected override float? Minimum => 0f;

    protected override float? Maximum => 1f;

    protected override bool MaximumInclusive => false;

    protected override string Expectation => "must be at least 0.0 and less than 1.0";
}
