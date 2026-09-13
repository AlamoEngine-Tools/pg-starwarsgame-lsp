// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

/// <summary>
///     Offering the engine's OWN correction as a quick fix.
/// </summary>
/// <remarks>
///     <para>
///         Several of these rules describe a value the engine overwrites at load. The fix writes
///         what the game is going to do anyway, so the file stops disagreeing with the running
///         game - and it is titled differently from a spelling suggestion, because accepting it
///         changes nothing about behaviour where accepting a spelling fix changes everything.
///     </para>
///     <para>
///         A fix is offered ONLY where the repair was read out of the binary. Where the engine
///         merely complains - the either/or pairs, a missing <c>Attack_Animation</c>, the seven
///         Leech_Shields tags - there is nothing to offer and inventing a value would be worse
///         than silence.
///     </para>
/// </remarks>
public sealed class EngineRepairFixTest
{
    private static readonly DocumentUri TestUri = DocumentUri.From("file:///test.xml");
    private static readonly LspRange TestRange = new(new Position(0, 5), new Position(0, 10));

    /// <summary>
    ///     Nine ability classes assign the one style they accept; three assign
    ///     <c>Causes_Despawn = true</c>. In every case the repair IS the single allowed value.
    /// </summary>
    [Fact]
    public void A_single_allowed_value_is_offered_as_the_engine_repair()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Activation_Style", XmlValueType.DynamicEnumValue)
            with
            {
                AllowedValues = ["GROUND_ACTIVATED"],
            };

        var d = Assert.Single(new AllowedValuesHandler()
            .Handle(XmlHandlerTestFixtures.MakeFact(tag, "Galactic_Automatic"),
                XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal("GROUND_ACTIVATED", d.SuggestedFix);
        Assert.NotNull(d.FixTitle);
    }

    /// <summary>
    ///     With more than one legal value there is no single thing the engine substitutes, so the
    ///     diagnostic stands on its own.
    /// </summary>
    [Fact]
    public void Several_allowed_values_offer_no_fix()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Activation_Style", XmlValueType.DynamicEnumValue)
            with
            {
                AllowedValues = ["GROUND_ACTIVATED", "USER_INPUT"],
            };

        var d = Assert.Single(new AllowedValuesHandler()
            .Handle(XmlHandlerTestFixtures.MakeFact(tag, "Galactic_Automatic"),
                XmlHandlerTestFixtures.EmptyCtx));

        Assert.Null(d.SuggestedFix);
    }

    /// <summary>
    ///     The engine assigns the bound itself - <c>Duration = 1.0</c>. The fact has to name its
    ///     owner: the repair is keyed on (owner, tag), because <c>Duration_In_Secs</c> also appears
    ///     on two ability types whose validators say nothing about it.
    /// </summary>
    [Fact]
    public void A_short_duration_is_offered_the_engines_own_one_second()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Duration_In_Secs", XmlValueType.Float);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 4, tag, "0.25",
            "LeechShieldsAbility");

        var d = Assert.Single(new AtLeastOneSecondHandler().Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal("1.0", d.SuggestedFix);
    }

    /// <summary>
    ///     A range rule whose repair value has not been read out of the binary must not guess one.
    /// </summary>
    [Fact]
    public void A_range_with_no_measured_repair_offers_nothing()
    {
        var tag = XmlHandlerTestFixtures.MakeTag("Damage_Amount", XmlValueType.Float);

        var d = Assert.Single(new NonNegativeValueHandler()
            .Handle(XmlHandlerTestFixtures.MakeFact(tag, "-3"), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Null(d.SuggestedFix);
    }

    /// <summary>
    ///     The title distinguishes the two kinds of fix. A did-you-mean keeps the old wording.
    /// </summary>
    [Fact]
    public void An_engine_repair_is_titled_as_one()
    {
        var d = new Diagnostic
        {
            Range = TestRange,
            Data = JToken.FromObject(new
            {
                fix = "GROUND_ACTIVATED",
                fixTitle = "Apply the engine's own value: GROUND_ACTIVATED",
            }),
        };

        var action = Assert.Single(new FixSuggestionCodeActionProvider(new EmptyFixCache())
            .Handle(new XmlCodeActionContext(TestUri, d))).CodeAction!;

        Assert.Equal("Apply the engine's own value: GROUND_ACTIVATED", action.Title);
        Assert.Equal("GROUND_ACTIVATED", Assert.Single(action.Edit!.Changes![TestUri]).NewText);
    }

    [Fact]
    public void A_plain_suggestion_keeps_its_own_title()
    {
        var d = new Diagnostic { Range = TestRange, Data = JToken.FromObject(new { fix = "42" }) };

        var action = Assert.Single(new FixSuggestionCodeActionProvider(new EmptyFixCache())
            .Handle(new XmlCodeActionContext(TestUri, d))).CodeAction!;

        Assert.Equal("Replace with '42'", action.Title);
    }
}

file sealed class EmptyFixCache : IXmlFixCache
{
    public string? GetSuggestedFix(string uri, int startLine, int startChar)
    {
        return null;
    }
}
