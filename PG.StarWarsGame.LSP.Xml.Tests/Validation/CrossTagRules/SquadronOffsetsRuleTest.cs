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

public sealed class SquadronOffsetsRuleTest
{
    private const string Uri = "file:///squadrons/Squadrons.xml";

    private static XmlDocumentFactProducer BuildProducer(IXmlCrossTagRule? rule = null)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            rule is null ? [] : [rule]);
    }

    [Fact]
    public void One_Squadron_Units_with_correct_offset_count_emits_no_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "</Obj></Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        Assert.Empty(facts.OfType<SquadronOffsetsMismatchFact>());
    }

    [Fact]
    public void Two_Squadron_Units_merged_5_units_and_correct_offsets_emits_no_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "<Squadron_Units>C, D, E</Squadron_Units>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "</Obj></Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        Assert.Empty(facts.OfType<SquadronOffsetsMismatchFact>());
    }

    [Fact]
    public void Two_Squadron_Units_merged_5_units_but_only_2_offsets_emits_mismatch_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "<Squadron_Units>C, D, E</Squadron_Units>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "</Obj></Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        var fact = Assert.Single(facts.OfType<SquadronOffsetsMismatchFact>());
        Assert.Equal(5, fact.TotalUnits);
        Assert.Equal(2, fact.TotalOffsets);
        Assert.Equal(Uri, fact.DocumentUri);
    }

    [Fact]
    public void One_Squadron_Units_with_zero_offsets_emits_mismatch_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B, C</Squadron_Units>" +
                           "</Obj></Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        var fact = Assert.Single(facts.OfType<SquadronOffsetsMismatchFact>());
        Assert.Equal(3, fact.TotalUnits);
        Assert.Equal(0, fact.TotalOffsets);
    }

    [Fact]
    public void More_offsets_than_units_emits_mismatch_fact()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "</Obj></Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        var fact = Assert.Single(facts.OfType<SquadronOffsetsMismatchFact>());
        Assert.Equal(2, fact.TotalUnits);
        Assert.Equal(5, fact.TotalOffsets);
    }

    [Fact]
    public void Object_without_Squadron_Units_emits_no_fact()
    {
        const string xml = "<Root><Obj><Max_Speed>5.0</Max_Speed></Obj></Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        Assert.Empty(facts.OfType<SquadronOffsetsMismatchFact>());
    }

    [Fact]
    public void No_rules_registered_emits_no_facts()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "</Obj></Root>";
        var facts = BuildProducer().Produce(xml, Uri);
        Assert.Empty(facts.OfType<SquadronOffsetsMismatchFact>());
    }

    [Fact]
    public void Mismatch_fact_carries_UnitTagLocations_and_OffsetTagLocations()
    {
        const string xml = "<Root><Obj>" +
                           "<Squadron_Units>A, B</Squadron_Units>" +
                           "<Squadron_Offsets>0,0,0</Squadron_Offsets>" +
                           "</Obj></Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        var fact = Assert.Single(facts.OfType<SquadronOffsetsMismatchFact>());
        Assert.Single(fact.UnitTagLocations);
        Assert.Single(fact.OffsetTagLocations);
    }

    [Fact]
    public void Multiple_mismatched_objects_each_emit_one_fact()
    {
        const string xml = "<Root>" +
                           "<Obj1><Squadron_Units>A</Squadron_Units></Obj1>" +
                           "<Obj2><Squadron_Units>B, C</Squadron_Units></Obj2>" +
                           "</Root>";
        var facts = BuildProducer(new SquadronOffsetsRule()).Produce(xml, Uri);
        Assert.Equal(2, facts.OfType<SquadronOffsetsMismatchFact>().Count());
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