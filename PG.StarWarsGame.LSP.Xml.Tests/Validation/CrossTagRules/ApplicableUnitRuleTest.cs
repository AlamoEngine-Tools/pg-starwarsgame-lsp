// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     An ability that applies to nothing, because it names neither a unit category nor a unit type.
/// </summary>
/// <remarks>
///     <para>
///         One rule shape, four messages, nine owning classes - which is why the rules take a SET of
///         elements rather than one each. The engine tests
///         <c>categories == 0 &amp;&amp; Get_Count(types) &lt; 1</c>, so either tag satisfies it, and
///         the four messages differ only in how they describe the consequence.
///     </para>
///     <para>
///         Found by the re-harvest rather than the original one: all four are labelled
///         <c>Warning</c>, and this one wraps across two literals as well.
///     </para>
///     <para>
///         Measured on the shipped corpus: 352 objects across the seven owning types that appear in
///         vanilla, and not one of them leaves both tags empty.
///     </para>
/// </remarks>
public sealed class ApplicableUnitRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    public static TheoryData<string, string> Owners => new()
    {
        { "Combat_Bonus_Ability", "affects no units at all" },
        { "Reduce_Production_Price_Ability", "affects no units at all" },
        { "Absorb_Blaster_Ability", "cannot absorb fire from any source" },
        { "Earthquake_Attack_Ability", "cannot activate" },
        { "Concentrate_Fire_Attack_Ability", "cannot activate" },
        { "Redirect_Blaster_Ability", "cannot block or redirect fire" },
    };

    [Theory]
    [MemberData(nameof(Owners))]
    public void An_ability_naming_neither_is_reported(string element, string consequence)
    {
        var d = Assert.Single(Diagnose($"<{element} Name='A'><Damage_Amount>1</Damage_Amount></{element}>"));

        Assert.Contains("Applicable_Unit_Categories", d.Message, StringComparison.Ordinal);
        Assert.Contains("Applicable_Unit_Types", d.Message, StringComparison.Ordinal);
        // Each message group keeps the engine's own description of what is lost.
        Assert.Contains(consequence, d.Message, StringComparison.Ordinal);
    }

    /// <summary>Either tag on its own satisfies the engine - it is an OR, not a pair.</summary>
    [Theory]
    [InlineData("<Applicable_Unit_Categories>Infantry</Applicable_Unit_Categories>")]
    [InlineData("<Applicable_Unit_Types>Rebel_Trooper</Applicable_Unit_Types>")]
    [InlineData("<Applicable_Unit_Categories>Infantry</Applicable_Unit_Categories>" +
                "<Applicable_Unit_Types>Rebel_Trooper</Applicable_Unit_Types>")]
    public void Either_tag_alone_is_enough(string body)
    {
        Assert.Empty(Diagnose($"<Combat_Bonus_Ability Name='A'>{body}</Combat_Bonus_Ability>"));
    }

    /// <summary>
    ///     A list tag is satisfied by CONTENT, not by being written - an empty one leaves the engine
    ///     with the same nothing it had before.
    /// </summary>
    [Fact]
    public void A_present_but_empty_tag_does_not_count()
    {
        const string body = "<Applicable_Unit_Categories>   </Applicable_Unit_Categories>" +
                            "<Applicable_Unit_Types></Applicable_Unit_Types>";

        Assert.Single(Diagnose($"<Combat_Bonus_Ability Name='A'>{body}</Combat_Bonus_Ability>"));
    }

    /// <summary>
    ///     These tags are lists, so "set" cannot mean Yes. The boolean pairs must keep their own
    ///     reading of what counts as set.
    /// </summary>
    [Fact]
    public void A_list_value_is_not_read_as_a_boolean()
    {
        // "No" is a perfectly good unit category name as far as this rule is concerned: it is
        // content, and content is what the engine counts.
        Assert.Empty(Diagnose(
            "<Combat_Bonus_Ability Name='A'><Applicable_Unit_Categories>No</Applicable_Unit_Categories>"
            + "</Combat_Bonus_Ability>"));
    }

    /// <summary>Regression: the boolean either/or pairs still read their halves as booleans.</summary>
    [Fact]
    public void The_boolean_pairs_are_unchanged()
    {
        var producer = Producer(new NeutralizeHeroTargetsRule());
        var facts = producer.Produce(
            "<Root><Neutralize_Hero_Ability Name='N'>" +
            "<Can_Neutralize_Minor_Heroes>No</Can_Neutralize_Minor_Heroes>" +
            "</Neutralize_Hero_Ability></Root>", Uri);

        var d = Assert.Single(facts.OfType<EitherOrRequirementFact>()
            .SelectMany(f => new EitherOrRequirementHandler().Handle(f, XmlHandlerTestFixtures.EmptyCtx)));

        Assert.Contains("are both off", d.Message, StringComparison.Ordinal);
        Assert.Contains("set one of them to Yes", d.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<XmlDiagnosticResult> Diagnose(string body)
    {
        var facts = CrossTagRuleSets.ApplicableUnits()
            .SelectMany(rule => Producer(rule).Produce($"<Root>{body}</Root>", Uri))
            .OfType<EitherOrRequirementFact>()
            .ToList();

        return facts
            .SelectMany(f => new EitherOrRequirementHandler().Handle(f, XmlHandlerTestFixtures.EmptyCtx))
            .ToList();
    }

    private static XmlDocumentFactProducer Producer(IXmlCrossTagRule rule)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [rule]);
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}
