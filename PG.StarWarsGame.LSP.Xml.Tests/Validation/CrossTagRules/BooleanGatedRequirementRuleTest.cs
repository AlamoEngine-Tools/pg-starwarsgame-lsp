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

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     The engine's "if you set A you must also set B" rules.
/// </summary>
/// <remarks>
///     <para>
///         Six of them, all read out of the 2018 binary. Five are on
///         <c>SystemSpyAbilityClass::Validate_Data</c> (<c>0101da00</c>) and pair one detail flag
///         with the summary flag it needs; the sixth is on
///         <c>GalacticSabotageAbilityClass::Validate_Data</c> (<c>00ef6c30</c>) and gates a
///         DURATION rather than another flag.
///     </para>
///     <para>
///         The engine repairs the object rather than refusing it - it turns the missing flag on, or
///         forces the duration to 10 seconds - so the ability runs with settings the author did not
///         write and nothing on screen says so.
///     </para>
/// </remarks>
public sealed class BooleanGatedRequirementRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    public static TheoryData<string, string, string> Pairs => new()
    {
        { "System_Spy_Ability", "See_Fleet_Contents", "See_Num_Fleets" },
        { "System_Spy_Ability", "See_Most_Powerful_Ship", "See_Num_Fleets" },
        { "System_Spy_Ability", "See_Ground_Company_Contents", "See_Num_Ground_Companies" },
        { "System_Spy_Ability", "See_Credit_Income_Breakdown", "See_Credit_Income" },
        { "System_Spy_Ability", "See_Political_Control_Breakdown", "See_Political_Control" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Gate_on_with_requirement_off_is_reported(string element, string gate, string required)
    {
        var body = $"<{gate}>Yes</{gate}><{required}>No</{required}>";

        var fact = Assert.Single(Gated(Run($"<{element} Name='S'>{body}</{element}>")));
        Assert.Equal(gate, fact.GateTag);
        Assert.Equal(required, fact.RequiredTag);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Gate_on_with_requirement_absent_is_reported(string element, string gate, string required)
    {
        var body = $"<{gate}>Yes</{gate}>";

        Assert.Equal(required, Assert.Single(Gated(Run($"<{element} Name='S'>{body}</{element}>"))).RequiredTag);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Gate_on_with_requirement_on_is_silent(string element, string gate, string required)
    {
        var body = $"<{gate}>Yes</{gate}><{required}>Yes</{required}>";

        Assert.Empty(Gated(Run($"<{element} Name='S'>{body}</{element}>")));
    }

    // The gate is what makes the requirement apply. With it off, the object is simply not using
    // the feature and the engine asks nothing.
    [Theory]
    [MemberData(nameof(Pairs))]
    public void Gate_off_is_silent(string element, string gate, string required)
    {
        var body = $"<{gate}>No</{gate}>";

        Assert.Empty(Gated(Run($"<{element} Name='S'>{body}</{element}>")));
    }

    // "True" and "1" are the same Yes to the engine, so they must be to the rule.
    [Theory]
    [InlineData("True")]
    [InlineData("1")]
    public void Every_affirmative_spelling_opens_the_gate(string spelling)
    {
        var body = $"<See_Fleet_Contents>{spelling}</See_Fleet_Contents>";

        Assert.Single(Gated(Run($"<System_Spy_Ability Name='S'>{body}</System_Spy_Ability>")));
    }

    /// <summary>
    ///     The sixth rule gates a duration, not a flag: zero is as unset as absent.
    /// </summary>
    /// <remarks>
    ///     <c>if (CanHaltCreditProduction == true &amp;&amp; DurationOfCreditHalt &lt;= 0.0)</c>,
    ///     then the engine forces the duration to 10.0. So writing the flag and leaving the
    ///     duration at zero gives a ten-second halt nobody asked for.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("<Duration_Of_Credit_Halt>0</Duration_Of_Credit_Halt>")]
    [InlineData("<Duration_Of_Credit_Halt>-5</Duration_Of_Credit_Halt>")]
    public void Credit_halt_without_a_positive_duration_is_reported(string duration)
    {
        var body = $"<Can_Halt_Credit_Production>Yes</Can_Halt_Credit_Production>{duration}";

        var fact = Assert.Single(Gated(Run($"<Galactic_Sabotage_Ability Name='G'>{body}</Galactic_Sabotage_Ability>")));
        Assert.Equal("Can_Halt_Credit_Production", fact.GateTag);
        Assert.Equal("Duration_Of_Credit_Halt", fact.RequiredTag);
    }

    [Fact]
    public void Credit_halt_with_a_positive_duration_is_silent()
    {
        const string body =
            "<Can_Halt_Credit_Production>Yes</Can_Halt_Credit_Production>" +
            "<Duration_Of_Credit_Halt>30.0</Duration_Of_Credit_Halt>";

        Assert.Empty(Gated(Run($"<Galactic_Sabotage_Ability Name='G'>{body}</Galactic_Sabotage_Ability>")));
    }

    /// <summary>
    ///     Scoped to the element, because the engine states each rule in one class's validator.
    /// </summary>
    /// <remarks>
    ///     Unlike <c>TagComparisonRuleBase</c>, requiring both tags is NOT enough scoping here: the
    ///     rule has to fire when the required tag is ABSENT, which is most of its value, so it
    ///     cannot use the pair's presence to decide whether it applies.
    /// </remarks>
    [Theory]
    [InlineData("Slicer_Ability", "<See_Fleet_Contents>Yes</See_Fleet_Contents>")]
    [InlineData("Black_Market_Ability", "<Can_Halt_Credit_Production>Yes</Can_Halt_Credit_Production>")]
    public void Other_ability_types_are_untouched(string element, string body)
    {
        Assert.Empty(Gated(Run($"<{element} Name='X'>{body}</{element}>")));
    }

    private static IReadOnlyList<BooleanGatedRequirementFact> Gated(IEnumerable<XmlFact> facts)
    {
        return facts.OfType<BooleanGatedRequirementFact>().ToList();
    }

    private static IReadOnlyList<XmlFact> Run(string body)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            CrossTagRuleSets.BooleanGatedRequirements());

        return producer.Produce($"<Root>{body}</Root>", Uri);
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
