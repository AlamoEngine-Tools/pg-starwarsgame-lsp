// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A curve that needs at least two control points to describe anything.
/// </summary>
/// <remarks>
///     <para>
///         <c>Error: (%s) Cost_Mod_By_Base_Level must have at least two control points!</c>
///         (<c>01543398</c>), and the same for <c>Cost_Mod_By_Planetary_Corruption_Level</c>
///         (<c>015432e0</c>) and <c>Cost_Mod_By_Previous_Neutralizations</c> (<c>01543340</c>).
///         A single point gives the engine nothing to interpolate between.
///     </para>
///     <para>
///         Each point is an x,y pair, so the shipped <c>0.0,0.0, 5.0,1.0</c> is two points from four
///         values - exactly the minimum, which is why the base game passes.
///     </para>
/// </remarks>
public sealed class ControlPointCurveHandler : ListLengthHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.CurveNeedsTwoControlPoints;

    public override string ValidationId => "control-point-curve";

    protected override int ValuesPerEntry => 2;

    protected override int? MinimumEntries => 2;

    protected override int? MaximumEntries => null;

    protected override string Expectation => "needs at least two control points";
}
