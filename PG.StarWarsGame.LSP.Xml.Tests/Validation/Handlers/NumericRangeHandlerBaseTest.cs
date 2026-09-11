// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The shared range check, exercised through the concrete bounds the engine states.
/// </summary>
/// <remarks>
///     The cases that matter are the boundary values themselves: whether zero passes, whether -1.0
///     passes, whether 1.0 passes. Every range the engine spells out differs from its neighbours
///     only in those answers, so a bug in inclusivity would otherwise pass every test that used
///     values comfortably inside the interval.
/// </remarks>
public sealed class NumericRangeHandlerBaseTest
{
    private static readonly NonNegativeValueHandler NonNegative = new();
    private static readonly PositiveValueHandler Positive = new();
    private static readonly BonusPercentageHandler Bonus = new();
    private static readonly AngleDegreesHalfTurnHandler HalfTurn = new();
    private static readonly AngleDegreesFullTurnHandler FullTurn = new();
    private static readonly NegativeFractionHandler NegativeFraction = new();
    private static readonly FractionBelowOneHandler FractionBelowOne = new();
    private static readonly BelowOneHandler BelowOne = new();

    public static TheoryData<string, string, bool> Boundaries =>
        new()
        {
            // handler id, value, should the value be accepted
            { "non-negative-value", "0", true },
            { "non-negative-value", "-0.001", false },
            { "positive-value", "0", false },
            { "positive-value", "0.001", true },
            { "bonus-percentage", "-1", false },
            { "bonus-percentage", "-0.999", true },
            { "bonus-percentage", "1000", true },
            { "angle-degrees-half-turn", "0", true },
            { "angle-degrees-half-turn", "180", true },
            { "angle-degrees-half-turn", "180.1", false },
            { "angle-degrees-half-turn", "-1", false },
            { "angle-degrees-full-turn", "360", true },
            { "angle-degrees-full-turn", "361", false },
            { "negative-fraction", "-1", true },
            { "negative-fraction", "0", true },
            { "negative-fraction", "-1.1", false },
            { "negative-fraction", "0.1", false },
            { "fraction-below-one", "0", true },
            { "fraction-below-one", "0.999", true },
            { "fraction-below-one", "1", false },
            { "fraction-below-one", "-0.1", false },
            { "below-one", "0.999", true },
            { "below-one", "1", false },
            // No lower bound stated, so a negative is not this rule's business.
            { "below-one", "-500", true },
        };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Bounds_are_inclusive_exactly_where_the_engine_says(string id, string value, bool accepted)
    {
        var handler = ById(id);
        var results = handler.Handle(
            XmlHandlerTestFixtures.MakeFact(XmlHandlerTestFixtures.MakeTag("T", XmlValueType.Float), value),
            XmlHandlerTestFixtures.EmptyCtx).ToList();

        if (accepted)
            Assert.Empty(results);
        else
            Assert.Equal(XmlDiagnosticSeverity.Warning, Assert.Single(results).Severity);
    }

    [Theory]
    [InlineData("non-negative-value")]
    [InlineData("angle-degrees-half-turn")]
    [InlineData("below-one")]
    public void Non_numbers_are_left_to_the_type_handler(string id)
    {
        foreach (var junk in new[] { "", "abc", "  " })
            Assert.Empty(ById(id).Handle(
                XmlHandlerTestFixtures.MakeFact(XmlHandlerTestFixtures.MakeTag("T", XmlValueType.Float), junk),
                XmlHandlerTestFixtures.EmptyCtx));
    }

    // The schema refers to these by name; renaming one silently disables every tag using it.
    [Fact]
    public void Validation_ids_are_stable()
    {
        Assert.Equal(
            new[]
            {
                "angle-degrees-full-turn", "angle-degrees-half-turn", "below-one", "bonus-percentage",
                "fraction-below-one", "negative-fraction", "non-negative-value", "positive-value",
            },
            All().Select(h => h.ValidationId).OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<NumericRangeHandlerBase> All()
    {
        return [NonNegative, Positive, Bonus, HalfTurn, FullTurn, NegativeFraction, FractionBelowOne, BelowOne];
    }

    private static NumericRangeHandlerBase ById(string id)
    {
        return All().Single(h => h.ValidationId == id);
    }
}
