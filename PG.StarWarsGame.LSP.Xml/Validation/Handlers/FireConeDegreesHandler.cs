// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A weapon hardpoint's fire cone: greater than zero, and no wider than a full turn.
/// </summary>
/// <remarks>
///     <para>
///         MEASURED, the floor. <c>HardPointClass::Can_Weapon_Point_At</c> asserts
///         <c>Data-&gt;Get_Fire_Cone_Width() &gt; 0.0f</c> (<c>HardPoint.cpp:1857</c>) and the same
///         for the height (<c>:1858</c>), above the branch and so for every weapon hardpoint. The
///         non-turret branch then tests <c>Get_Fire_Cone_Width() / 2.0 &lt; |yaw|</c>, which at zero
///         rejects every bearing off dead centre.
///     </para>
///     <para>
///         INFERRED, the ceiling. The engine states no upper bound. It halves the cone and compares
///         it against an absolute deviation, and that deviation cannot exceed 180 degrees, so at 360
///         the test already passes for every bearing and nothing above it can do more. The value is
///         inert rather than refused, which is why the two ends report different sentences and why
///         this is not <c>angle-degrees-full-turn</c>.
///     </para>
///     <para>
///         Separate from <see cref="AngleDegreesFullTurnHandler" /> for the floor, not the ceiling:
///         that rule admits zero, this one must not.
///     </para>
///     <para>
///         Measured: 866 fire-cone values across <c>eaw/</c> and <c>foc/</c>, none at or below zero,
///         two above a full turn - <c>HP_MC30_LASER_00</c> at 364.0 and
///         <c>HP_Gargantuan_Small_Turret_Front_Left</c> at 450.0. The second sits beside three
///         identical siblings that all say 45.0, and is harmless only because a turret hardpoint's
///         cone is never read. It is still a typo, and this is the rule that finds it.
///     </para>
/// </remarks>
public sealed class FireConeDegreesHandler : NumericRangeHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.FireConeOutsideFullTurn;

    public override string ValidationId => "fire-cone-degrees";

    protected override float? Minimum => 0f;

    protected override float? Maximum => 360f;

    /// <summary>The assert is <c>&gt; 0.0f</c>, so zero is out.</summary>
    protected override bool MinimumInclusive => false;

    protected override string Expectation => "must be greater than 0 and at most 360 degrees";

    protected override string BelowMinimumConsequence =>
        "A hardpoint with this cone cannot point at anything";

    protected override string AboveMaximumConsequence =>
        "The engine halves the cone and compares it against a deviation of at most 180 degrees, so "
        + "anything past a full turn has no further effect";
}
