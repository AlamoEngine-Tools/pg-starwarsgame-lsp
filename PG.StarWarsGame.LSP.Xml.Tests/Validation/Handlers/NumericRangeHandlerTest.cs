// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     Two range rules the engine states about dozens of tags, and which nothing enforced.
/// </summary>
/// <remarks>
///     <para>
///         Harvested from the engine's own formatted diagnostics: 27 tags it says "cannot be less
///         than zero" and 35 it says "must be greater than zero" (or "cannot be less than or equal
///         to zero", the same rule worded differently). <c>DamageNonzeroHandler</c> already did the
///         second check, but its message names the AI and unit building, so it fits <c>Damage</c>
///         and nothing else.
///     </para>
///     <para>
///         Opt-in by <c>validationId</c> rather than by value type, so no <c>XmlValueType</c>
///         member is invented and a tag keeps whatever numeric type the engine actually parses.
///     </para>
/// </remarks>
public sealed class NumericRangeHandlerTest
{
    private static readonly NonNegativeValueHandler NonNegative = new();
    private static readonly PositiveValueHandler Positive = new();

    private static readonly XmlTagDefinition Amount =
        XmlHandlerTestFixtures.MakeTag("Damage_Amount", XmlValueType.Float);

    private static readonly XmlTagDefinition Radius =
        XmlHandlerTestFixtures.MakeTag("Damage_Radius", XmlValueType.Float);

    [Fact]
    public void ValidationIds_are_stable()
    {
        // The schema refers to these by name; renaming one silently disables every tag using it.
        Assert.Equal("non-negative-value", NonNegative.ValidationId);
        Assert.Equal("positive-value", Positive.ValidationId);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("0.0f")]
    [InlineData("12.5")]
    public void Non_negative_accepts_zero_and_above(string value)
    {
        Assert.Empty(NonNegative.Handle(XmlHandlerTestFixtures.MakeFact(Amount, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0.5")]
    public void Non_negative_rejects_below_zero(string value)
    {
        var d = Assert.Single(NonNegative.Handle(XmlHandlerTestFixtures.MakeFact(Amount, value),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Damage_Amount", d.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("1.0")]
    public void Positive_accepts_above_zero(string value)
    {
        Assert.Empty(Positive.Handle(XmlHandlerTestFixtures.MakeFact(Radius, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    // "greater than zero" excludes zero itself. That is the whole difference between the two.
    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-2")]
    public void Positive_rejects_zero_and_below(string value)
    {
        Assert.Single(Positive.Handle(XmlHandlerTestFixtures.MakeFact(Radius, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    // A value that is not a number belongs to the type handler; a range check has nothing to say.
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Neither_handler_complains_about_non_numbers(string value)
    {
        Assert.Empty(NonNegative.Handle(XmlHandlerTestFixtures.MakeFact(Amount, value),
            XmlHandlerTestFixtures.EmptyCtx));
        Assert.Empty(Positive.Handle(XmlHandlerTestFixtures.MakeFact(Radius, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }
}
