// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A weapon hardpoint's fire cone, whose two ends fail for different reasons.
/// </summary>
/// <remarks>
///     <para>
///         MEASURED, the lower bound: <c>HardPointClass::Can_Weapon_Point_At</c> asserts
///         <c>Get_Fire_Cone_Width() &gt; 0.0f</c> (<c>HardPoint.cpp:1857</c>) and the same for the
///         height (<c>:1858</c>), and the non-turret branch then tests
///         <c>Get_Fire_Cone_Width() / 2.0 &lt; |yaw|</c>. At zero that test rejects every bearing
///         off dead centre, so the hardpoint can point at nothing.
///     </para>
///     <para>
///         INFERRED, the upper bound: the engine states none. The halved cone is compared against
///         an absolute deviation, which cannot exceed 180 degrees, so at 360 the test is already
///         unconditional and nothing above it can do more. The value is not rejected - it is
///         inert - and the message says so rather than claiming a refusal the engine never makes.
///     </para>
///     <para>
///         Measured on the corpus: 866 fire-cone values across <c>eaw/</c> and <c>foc/</c>, none at
///         or below zero and exactly two above a full turn - <c>HP_MC30_LASER_00</c> at 364.0 and
///         <c>HP_Gargantuan_Small_Turret_Front_Left</c> at 450.0, the latter sitting beside three
///         identical siblings that all say 45.0. That is the typo this half of the rule exists to
///         catch.
///     </para>
/// </remarks>
public sealed class FireConeDegreesHandlerTest
{
    private static readonly FireConeDegreesHandler Handler = new();

    private static readonly XmlTagDefinition Width =
        XmlHandlerTestFixtures.MakeTag("Fire_Cone_Width", XmlValueType.Float);

    [Fact]
    public void ValidationId_is_stable()
    {
        // The schema refers to this by name; renaming it silently disables every tag using it.
        Assert.Equal("fire-cone-degrees", Handler.ValidationId);
    }

    /// <summary>The values vanilla actually uses, including the full turn at the top.</summary>
    [Theory]
    [InlineData("0.001")]
    [InlineData("1.0")]
    [InlineData("45.0")]
    [InlineData("180.0")]
    [InlineData("360")]
    [InlineData("360.0")]
    public void A_cone_inside_a_full_turn_is_accepted(string value)
    {
        Assert.Empty(Handler.Handle(XmlHandlerTestFixtures.MakeFact(Width, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    /// <summary>The bound is EXCLUSIVE at zero - the assert is <c>&gt; 0.0f</c>, not <c>&gt;= 0</c>.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-45")]
    public void A_cone_of_zero_or_less_is_reported(string value)
    {
        var d = Assert.Single(Handler.Handle(XmlHandlerTestFixtures.MakeFact(Width, value),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Fire_Cone_Width", d.Message, StringComparison.Ordinal);
    }

    /// <summary>Both vanilla offenders, plus the first value past the bound.</summary>
    [Theory]
    [InlineData("360.1")]
    [InlineData("364.0")]
    [InlineData("450.0")]
    public void A_cone_past_a_full_turn_is_reported(string value)
    {
        var d = Assert.Single(Handler.Handle(XmlHandlerTestFixtures.MakeFact(Width, value),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
    }

    /// <summary>
    ///     The two ends mean different things to the engine, and the author needs to be told which
    ///     one they hit. Zero is a hardpoint that cannot shoot; 450 is a number with no effect.
    /// </summary>
    [Fact]
    public void The_two_ends_report_different_consequences()
    {
        var tooSmall = Assert.Single(Handler.Handle(
            XmlHandlerTestFixtures.MakeFact(Width, "0"), XmlHandlerTestFixtures.EmptyCtx));
        var tooLarge = Assert.Single(Handler.Handle(
            XmlHandlerTestFixtures.MakeFact(Width, "450"), XmlHandlerTestFixtures.EmptyCtx));

        Assert.NotEqual(tooSmall.Message, tooLarge.Message);
        Assert.Contains("cannot point at anything", tooSmall.Message, StringComparison.Ordinal);
        Assert.Contains("no further effect", tooLarge.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The engine states no upper bound, so the report must not claim it refuses the value.
    ///     Separating measured from inferred is the whole reason this handler is not
    ///     <c>angle-degrees-full-turn</c>.
    /// </summary>
    [Fact]
    public void The_upper_bound_does_not_claim_the_engine_rejects_the_value()
    {
        var d = Assert.Single(Handler.Handle(
            XmlHandlerTestFixtures.MakeFact(Width, "450"), XmlHandlerTestFixtures.EmptyCtx));

        Assert.DoesNotContain("rejects", d.Message, StringComparison.OrdinalIgnoreCase);
    }

    // A value that is not a number belongs to the type handler.
    [Theory]
    [InlineData("")]
    [InlineData("wide")]
    [InlineData("  ")]
    public void A_non_numeric_value_is_left_to_the_type_handler(string value)
    {
        Assert.Empty(Handler.Handle(XmlHandlerTestFixtures.MakeFact(Width, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }
}