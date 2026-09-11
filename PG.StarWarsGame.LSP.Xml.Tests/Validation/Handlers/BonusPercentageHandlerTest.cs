// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The bonus-percentage family: a multiplier the engine adds to 1.0, so -1.0 cancels the stat
///     entirely and anything below it inverts the sign.
/// </summary>
/// <remarks>
///     <para>
///         Nine tags carry this rule, stated as <c>Error: (%s) Health_Bonus_Percentage cannot be
///         -1.0 or less.</c> (<c>01552038</c>) and, for one, <c>cannot be less than -1.0</c>. The
///         bound is exclusive in the first wording: -1.0 itself is rejected.
///     </para>
///     <para>
///         Note that <c>Defense_Bonus_Percentage</c> is NOT in this family - the engine says it
///         "cannot be 1.0 or greater", an upper bound, which is a different rule and left for the
///         moment.
///     </para>
/// </remarks>
public sealed class BonusPercentageHandlerTest
{
    private static readonly BonusPercentageHandler Sut = new();

    private static readonly XmlTagDefinition Bonus =
        XmlHandlerTestFixtures.MakeTag("Health_Bonus_Percentage", XmlValueType.Float);

    [Fact]
    public void ValidationId_is_stable()
    {
        Assert.Equal("bonus-percentage", Sut.ValidationId);
    }

    // A negative bonus above -1.0 is a legitimate penalty, and vanilla uses them.
    [Theory]
    [InlineData("-0.5")]
    [InlineData("-0.999")]
    [InlineData("0")]
    [InlineData("0.25")]
    [InlineData("3.0")]
    public void Values_above_minus_one_are_accepted(string value)
    {
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Bonus, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    // The bound is exclusive: -1.0 itself is what the engine rejects.
    [Theory]
    [InlineData("-1")]
    [InlineData("-1.0")]
    [InlineData("-1.5")]
    [InlineData("-2")]
    public void Minus_one_and_below_are_rejected(string value)
    {
        var d = Assert.Single(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Bonus, value),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Health_Bonus_Percentage", d.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Non_numbers_are_left_to_the_type_handler(string value)
    {
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Bonus, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }
}
