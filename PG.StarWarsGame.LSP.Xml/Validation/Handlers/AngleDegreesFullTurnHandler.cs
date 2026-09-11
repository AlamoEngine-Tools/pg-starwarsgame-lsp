// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An angle in degrees bounded to a full turn, inclusive at both ends.
/// </summary>
/// <remarks>
///     <c>Error: (%s) Reaction_Arc_In_Degrees must be between 0 and 360.</c> (<c>01555e94</c>). An
///     arc is a width rather than a bearing, so 360 means "all round" and the engine does not wrap.
/// </remarks>
public sealed class AngleDegreesFullTurnHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.AngleOutsideFullTurn;

    public override string ValidationId => "angle-degrees-full-turn";

    protected override float? Minimum => 0f;

    protected override float? Maximum => 360f;

    protected override string Expectation => "must be between 0 and 360 degrees";
}
