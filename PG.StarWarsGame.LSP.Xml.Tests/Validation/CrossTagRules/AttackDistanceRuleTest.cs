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
///     #101: the rule notices an object that writes either side of the comparison and names it; the
///     handler compares the EFFECTIVE values, since hardpoint lists and attack distances are routinely
///     inherited.
/// </summary>
/// <remarks>
///     Anchored on the <c>Targeting_Max_Attack_Distance</c> value when the object writes one - that is
///     where a quick fix belongs - and on the <c>HardPoints</c> value otherwise, so a variant that only
///     swaps its hardpoints is still checked.
/// </remarks>
public sealed class AttackDistanceRuleTest
{
    private const string Uri = "file:///xml/Spaceunitscorvettes.xml";

    private static IReadOnlyList<AttackDistanceFact> Produce(string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new AttackDistanceRule()]);
        return producer.Produce(xml, Uri).OfType<AttackDistanceFact>().ToList();
    }

    [Fact]
    public void An_object_writing_the_attack_distance_is_anchored_on_its_value()
    {
        const string xml =
            "<SpaceUnits>\n" +
            "<SpaceUnit Name=\"Tantive_IV\">\n" +
            "    <Targeting_Max_Attack_Distance>2000.0</Targeting_Max_Attack_Distance>\n" +
            "    <HardPoints>HP_A</HardPoints>\n" +
            "</SpaceUnit>\n" +
            "</SpaceUnits>";

        var fact = Assert.Single(Produce(xml));

        Assert.Equal("Tantive_IV", fact.ObjectId);
        Assert.True(fact.AnchoredOnAttackDistance);
        Assert.Equal(2, fact.Line);
        Assert.Equal(35, fact.Column);
        Assert.Equal(6, fact.Length);
    }

    [Fact]
    public void A_variant_that_only_swaps_its_hardpoints_is_anchored_on_the_list()
    {
        const string xml =
            "<SpaceUnits>\n" +
            "<SpaceUnit Name=\"Tantive_IV_Variant\">\n" +
            "    <HardPoints>HP_A</HardPoints>\n" +
            "</SpaceUnit>\n" +
            "</SpaceUnits>";

        var fact = Assert.Single(Produce(xml));

        Assert.False(fact.AnchoredOnAttackDistance);
        Assert.Equal(2, fact.Line);
        Assert.Equal(16, fact.Column);
    }

    [Fact]
    public void An_object_writing_neither_emits_nothing()
    {
        Assert.Empty(Produce("<X><SpaceUnit Name=\"A\"><Max_Speed>2</Max_Speed></SpaceUnit></X>"));
    }

    [Fact]
    public void An_unnamed_object_emits_nothing()
    {
        Assert.Empty(Produce("<X><SpaceUnit><HardPoints>HP_A</HardPoints></SpaceUnit></X>"));
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