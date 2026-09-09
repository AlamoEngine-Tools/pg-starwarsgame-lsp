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
///     The two absorb tags work together: healed = (projectile damage * percentage) + amount. Either
///     may be zero, but if BOTH are, the ability absorbs nothing however it is triggered (#106).
/// </summary>
public sealed class DamageAbsorbRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    private static XmlDocumentFactProducer BuildProducer(IXmlCrossTagRule? rule = null)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            rule is null ? [] : [rule]);
    }

    private static IReadOnlyList<XmlFact> Run(string body)
    {
        return BuildProducer(new DamageAbsorbRule()).Produce($"<Root><Obj>{body}</Obj></Root>", Uri);
    }

    [Fact]
    public void Both_zero_emits_fact()
    {
        var facts = Run("<Damage_Absorb_Percentage>0</Damage_Absorb_Percentage>" +
                        "<Damage_Absorb_Amount>0</Damage_Absorb_Amount>");
        Assert.Single(facts.OfType<DamageAbsorbsNothingFact>());
    }

    // Absent counts as zero - the formula reads a missing term as 0, so "percentage 0 and no
    // amount" absorbs exactly as much as "0 and 0", which is nothing.
    [Fact]
    public void Zero_with_the_other_absent_emits_fact()
    {
        Assert.Single(Run("<Damage_Absorb_Percentage>0</Damage_Absorb_Percentage>")
            .OfType<DamageAbsorbsNothingFact>());
        Assert.Single(Run("<Damage_Absorb_Amount>0.0</Damage_Absorb_Amount>")
            .OfType<DamageAbsorbsNothingFact>());
    }

    [Theory]
    [InlineData("<Damage_Absorb_Percentage>0.2</Damage_Absorb_Percentage><Damage_Absorb_Amount>1</Damage_Absorb_Amount>")]
    [InlineData("<Damage_Absorb_Percentage>0.1</Damage_Absorb_Percentage><Damage_Absorb_Amount>0</Damage_Absorb_Amount>")]
    [InlineData("<Damage_Absorb_Percentage>0</Damage_Absorb_Percentage><Damage_Absorb_Amount>.8</Damage_Absorb_Amount>")]
    [InlineData("<Damage_Absorb_Amount>1</Damage_Absorb_Amount>")]
    public void One_non_zero_is_enough(string body)
    {
        Assert.Empty(Run(body).OfType<DamageAbsorbsNothingFact>());
    }

    // An object that never mentions absorb is not configuring it, so there is nothing to report.
    [Fact]
    public void Neither_tag_present_emits_no_fact()
    {
        Assert.Empty(Run("<Max_Speed>10</Max_Speed>").OfType<DamageAbsorbsNothingFact>());
    }

    // A non-numeric value is the Float validator's business. Reporting it here as well would put
    // two diagnostics on one typo and guess at a meaning the value does not have.
    [Fact]
    public void Unparseable_value_is_left_to_the_type_check()
    {
        Assert.Empty(Run("<Damage_Absorb_Percentage>lots</Damage_Absorb_Percentage>" +
                         "<Damage_Absorb_Amount>0</Damage_Absorb_Amount>")
            .OfType<DamageAbsorbsNothingFact>());
    }

    [Fact]
    public void Rule_not_registered_emits_no_fact()
    {
        var facts = BuildProducer().Produce(
            "<Root><Obj><Damage_Absorb_Percentage>0</Damage_Absorb_Percentage></Obj></Root>", Uri);
        Assert.Empty(facts.OfType<DamageAbsorbsNothingFact>());
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
