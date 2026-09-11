// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     How many entries a list carries, as distinct from what each entry looks like.
/// </summary>
/// <remarks>
///     The interesting cases are the ones where the rule must stay QUIET: a ragged list and a
///     mistyped token already draw a diagnostic from the value type's own handler, and counting
///     entries of a list that does not parse would put two complaints on one mistake.
/// </remarks>
public sealed class ListLengthHandlerBaseTest
{
    private static readonly ControlPointCurveHandler Curve = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Cost_Mod_By_Base_Level", XmlValueType.IntFloatTupleList);

    [Fact]
    public void ValidationId_is_stable()
    {
        Assert.Equal("control-point-curve", Curve.ValidationId);
    }

    // Two x,y points is four values - the shipped minimum.
    [Theory]
    [InlineData("0.0,0.0, 5.0,1.0")]
    [InlineData("0.0,0.0, 1000.0,750.0")]
    [InlineData("0,0, 1,1, 2,2")]
    public void Two_or_more_points_pass(string value)
    {
        Assert.Empty(Curve.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void One_point_is_reported_with_the_count()
    {
        var d = Assert.Single(Curve.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "0.0,0.0"),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("at least two control points", d.Message, StringComparison.Ordinal);
        Assert.Contains("has 1", d.Message, StringComparison.Ordinal);
    }

    // A ragged list is the type handler's complaint; counting half a point here would double up.
    [Theory]
    [InlineData("0.0,0.0, 5.0")]
    [InlineData("0.0")]
    public void A_ragged_list_is_left_alone(string value)
    {
        Assert.Empty(Curve.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_tag_is_an_omission_not_a_short_list(string value)
    {
        Assert.Empty(Curve.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    // The base must support an upper bound and an exact count, not only "at least N".
    [Fact]
    public void Bounds_can_limit_a_list_from_above()
    {
        var handler = new BoundedListHandler();

        Assert.Empty(handler.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "1, 2, 3"),
            XmlHandlerTestFixtures.EmptyCtx));
        Assert.Single(handler.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "1, 2, 3, 4, 5"),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    // And a rule the interval cannot express, via the predicate hook.
    [Fact]
    public void A_subclass_can_replace_the_rule_entirely()
    {
        var handler = new EvenCountListHandler();

        Assert.Empty(handler.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "1, 2"),
            XmlHandlerTestFixtures.EmptyCtx));
        Assert.Single(handler.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "1, 2, 3"),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    private sealed class BoundedListHandler : ListLengthHandlerBase
    {
        public override string ValidationId => "test-bounded";
        protected override int? MinimumEntries => 1;
        protected override int? MaximumEntries => 4;
        protected override string Expectation => "takes between one and four values";
    }

    private sealed class EvenCountListHandler : ListLengthHandlerBase
    {
        public override string ValidationId => "test-even";
        protected override int? MinimumEntries => null;
        protected override int? MaximumEntries => null;
        protected override string Expectation => "takes an even number of values";

        protected override bool IsAcceptable(int entries)
        {
            return entries % 2 == 0;
        }
    }
}
