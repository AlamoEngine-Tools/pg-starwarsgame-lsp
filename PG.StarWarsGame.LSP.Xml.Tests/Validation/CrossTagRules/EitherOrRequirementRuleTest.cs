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
///     The engine's three "you should set either A or B" pairs.
/// </summary>
/// <remarks>
///     <para>
///         Neither flag is wrong on its own - each is a perfectly ordinary No. Both off is what the
///         engine complains about, because the ability then loads and does nothing at all.
///     </para>
///     <para>
///         Attributed by the xref to each message: <c>HeroAssassinAbilityClass::Validate_Data</c>
///         (<c>0100df66</c>), <c>BaseDestructionAbilityClass::Validate_Data</c> (<c>00ff6c6b</c>)
///         and <c>NeutralizeHeroAbilityClass::Validate_Data</c> (<c>00f24027</c>). The third is
///         labelled <c>Warning</c> by the engine where the other two say <c>Error</c>, though the
///         stated consequence is word for word the same.
///     </para>
///     <para>
///         Measured on the shipped corpus: 10 live <c>Neutralize_Hero_Ability</c> objects, every one
///         of which sets at least one flag. <c>Hero_Assassin_Ability</c> has no live instance (it is
///         one of the three abandoned hero abilities) and the only <c>Base_Destruction_Ability</c>,
///         the Death Star's, sits inside a comment block - and sets <c>Destroy_Starbase</c> anyway.
///     </para>
/// </remarks>
public sealed class EitherOrRequirementRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    public static TheoryData<string, string, string, string> Pairs => new()
    {
        {
            "Hero_Assassin_Ability", "HeroAssassinAbility",
            "Can_Assassinate_Minor_Heroes", "Can_Assassinate_Major_Heroes"
        },
        {
            "Base_Destruction_Ability", "BaseDestructionAbility",
            "Destroy_Starbase", "Destroy_Land_Base"
        },
        {
            "Neutralize_Hero_Ability", "NeutralizeHeroAbility",
            "Can_Neutralize_Minor_Heroes", "Can_Neutralize_Major_Heroes"
        }
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Both_flags_off_is_reported(string element, string owningType, string first, string second)
    {
        var body = $"<{first}>No</{first}><{second}>No</{second}>";

        var fact = Assert.Single(Reported(Run($"<{element} Name='A'>{body}</{element}>")));
        Assert.Equal(owningType, fact.OwningType);
        Assert.Equal(first, fact.FirstTag);
        Assert.Equal(second, fact.SecondTag);
    }

    /// <summary>An absent boolean parses as false, so writing neither tag is both-off.</summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void Both_flags_absent_is_reported(string element, string owningType, string first, string second)
    {
        Assert.Single(Reported(Run($"<{element} Name='A'></{element}>")));
        Assert.NotEmpty(owningType + first + second);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Either_flag_on_is_silent(string element, string owningType, string first, string second)
    {
        Assert.Empty(Reported(Run($"<{element} Name='A'><{first}>Yes</{first}></{element}>")));
        Assert.Empty(Reported(Run($"<{element} Name='A'><{second}>Yes</{second}></{element}>")));
        Assert.NotEmpty(owningType);
    }

    /// <summary>
    ///     The engine reads a boolean by MEANING, so the affirmative spellings are interchangeable.
    /// </summary>
    [Theory]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("yes")]
    public void Any_affirmative_spelling_counts_as_on(string affirmative)
    {
        Assert.Empty(Reported(Run(
            $"<Neutralize_Hero_Ability Name='N'><Can_Neutralize_Minor_Heroes>{affirmative}" +
            "</Can_Neutralize_Minor_Heroes></Neutralize_Hero_Ability>")));
    }

    /// <summary>
    ///     An unrecognised spelling reads as false to the engine, so the pair is still both-off.
    ///     The typo itself is BooleanValueHandler's to report; this rule reports the consequence.
    /// </summary>
    [Fact]
    public void An_unrecognised_spelling_reads_as_off()
    {
        Assert.Single(Reported(Run(
            "<Neutralize_Hero_Ability Name='N'><Can_Neutralize_Major_Heroes>Maybe" +
            "</Can_Neutralize_Major_Heroes></Neutralize_Hero_Ability>")));
    }

    /// <summary>One report for the pair, not one per flag - the pair is the defect.</summary>
    [Fact]
    public void The_pair_is_reported_once()
    {
        Assert.Single(Reported(Run(
            "<Hero_Assassin_Ability Name='A'><Can_Assassinate_Minor_Heroes>No</Can_Assassinate_Minor_Heroes>" +
            "<Can_Assassinate_Major_Heroes>No</Can_Assassinate_Major_Heroes></Hero_Assassin_Ability>")));
    }

    /// <summary>
    ///     Scoping: these flag names belong to one ability class each, and an element that carries
    ///     neither is none of this rule's business.
    /// </summary>
    [Theory]
    [InlineData("Super_Laser_Ability")]
    [InlineData("Leech_Shields_Ability")]
    public void Other_ability_types_are_untouched(string element)
    {
        Assert.Empty(Reported(Run($"<{element} Name='X'></{element}>")));
    }

    private static IReadOnlyList<EitherOrRequirementFact> Reported(IEnumerable<XmlFact> facts)
    {
        return facts.OfType<EitherOrRequirementFact>().ToList();
    }

    private static IReadOnlyList<XmlFact> Run(string body)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            CrossTagRuleSets.EitherOrRequirements());

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