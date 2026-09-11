// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     An angle in degrees bounded to half a turn, inclusive at both ends.
/// </summary>
/// <remarks>
///     <c>Error: (%s) Max_Projectile_Redirection_Angle_In_Degrees must be between 0 and 180.</c>
///     (<c>01555e40</c>). A redirection angle is measured off the incoming heading, so 180 is a full
///     reversal and there is nothing beyond it to express.
/// </remarks>
public sealed class AngleDegreesHalfTurnHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.AngleOutsideHalfTurn;

    public override string ValidationId => "angle-degrees-half-turn";

    protected override float? Minimum => 0f;

    protected override float? Maximum => 180f;

    protected override string Expectation => "must be between 0 and 180 degrees";
}
