// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     Hardcoded-set tokens as facts (#307): every token the set does not accept as written, with
///     whether the set knows it as deprecated or not at all. The publisher used to walk the document
///     for these itself and build the diagnostics by hand.
/// </summary>
public sealed class HardcodedSetValueFactProducerTest
{
    private const string Uri = "file:///units.xml";

    private static HardcodedSetValueFactProducer Build(
        XmlTagDefinition tag, HardcodedReferenceSet? set, FakeFileTypeRegistry? registry = null,
        string? declaringType = null)
    {
        var builder = new SchemaProviderBuilder();
        var wiredTag = set is null ? tag : tag with { HardcodedSet = set };
        if (set is not null) builder.WithHardcodedSet(set);
        builder.WithTag(wiredTag);
        if (declaringType is not null) builder.WithTags(declaringType, wiredTag);
        return new HardcodedSetValueFactProducer(builder.Build(), registry ?? new FakeFileTypeRegistry(),
            new FileHelper(new MockFileSystem()));
    }

    private static HardcodedReferenceSet BehaviorModuleSet(params HardcodedReferenceSetValue[] values)
    {
        return new HardcodedReferenceSet { Name = "BehaviorModule", Values = values };
    }

    private static HardcodedReferenceSetValue Value(string name, params string[] groups)
    {
        return new HardcodedReferenceSetValue { Name = name, Groups = groups };
    }

    private static HardcodedReferenceSetValue DeprecatedValue(string name, params string[] groups)
    {
        return new HardcodedReferenceSetValue
        {
            Name = name, Groups = groups,
            Notes = [new SchemaNote(SchemaNoteKind.Deprecated, new Dictionary<string, string>())]
        };
    }

    private static XmlTagDefinition BehaviorTag(string tagName, params string[] valueGroups)
    {
        return new XmlTagDefinition
        {
            Tag = tagName,
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.HardcodedSet,
            ValueGroups = valueGroups
        };
    }

    private static string Document(string tag, string value)
    {
        return $"<GameObjectFiles><GameObject><{tag}>{value}</{tag}></GameObject></GameObjectFiles>";
    }

    // -- the tokens --------------------------------------------------------------------------

    [Fact]
    public void UnknownToken_IsAFactTheSetDoesNotKnow_OnTheTokenItself()
    {
        var sut = Build(BehaviorTag("Behavior"), BehaviorModuleSet(Value("GenericTransport")));
        var xml = Document("Behavior", "GenericTransport, TYPO_MODULE");

        var fact = Assert.IsType<HardcodedSetValueFact>(Assert.Single(sut.Produce(xml, Uri)));

        Assert.Equal("TYPO_MODULE", fact.Token);
        Assert.Equal("BehaviorModule", fact.SetName);
        Assert.False(fact.Deprecated);
        Assert.Equal(0, fact.Line);
        Assert.Equal(xml.IndexOf("TYPO_MODULE", StringComparison.Ordinal), fact.Column);
        Assert.Equal("TYPO_MODULE".Length, fact.Length);
    }

    [Fact]
    public void KnownTokens_ProduceNoFact()
    {
        var sut = Build(BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport"), Value("SlaveCraftBehavior")));

        Assert.Empty(sut.Produce(Document("Behavior", "GenericTransport SlaveCraftBehavior"), Uri));
    }

    [Fact]
    public void DeprecatedToken_IsAFactTheSetKnowsAsDeprecated()
    {
        var sut = Build(BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport"), DeprecatedValue("OLD_MODULE")));

        var fact = Assert.IsType<HardcodedSetValueFact>(
            Assert.Single(sut.Produce(Document("Behavior", "GenericTransport, OLD_MODULE"), Uri)));

        Assert.Equal("OLD_MODULE", fact.Token);
        Assert.True(fact.Deprecated);
    }

    // -- value groups ------------------------------------------------------------------------

    [Fact]
    public void ValueGroupOnTheTag_RulesOutTokensOfOtherGroups()
    {
        var sut = Build(BehaviorTag("SpaceBehavior", "space"),
            BehaviorModuleSet(Value("SpaceFighter", "space"), Value("InfantryUnit", "land")));

        var fact = Assert.IsType<HardcodedSetValueFact>(
            Assert.Single(sut.Produce(Document("SpaceBehavior", "InfantryUnit"), Uri)));

        Assert.Equal("InfantryUnit", fact.Token);
        Assert.False(fact.Deprecated);
    }

    [Fact]
    public void TokenWithoutGroups_IsValidForEveryValueGroup()
    {
        var sut = Build(BehaviorTag("SpaceBehavior", "space"), BehaviorModuleSet(Value("GenericTransport")));

        Assert.Empty(sut.Produce(Document("SpaceBehavior", "GenericTransport"), Uri));
    }

    [Fact]
    public void TokenOfAMatchingGroup_IsValid()
    {
        var sut = Build(BehaviorTag("SpaceBehavior", "space"),
            BehaviorModuleSet(Value("SpaceFighter", "space"), Value("InfantryUnit", "land")));

        Assert.Empty(sut.Produce(Document("SpaceBehavior", "SpaceFighter"), Uri));
    }

    [Fact]
    public void TagWithSeveralGroups_AcceptsATokenOfAnyOfThem()
    {
        var sut = Build(BehaviorTag("Behavior", "land", "space"),
            BehaviorModuleSet(Value("LandUnit", "land"), Value("SpaceUnit", "space")));

        Assert.Empty(sut.Produce(Document("Behavior", "LandUnit SpaceUnit"), Uri));
    }

    [Fact]
    public void TagWithSeveralGroups_RulesOutATokenOfNone()
    {
        var sut = Build(BehaviorTag("LandBehavior", "land", "space"),
            BehaviorModuleSet(Value("AirUnit", "air"), Value("LandUnit", "land")));

        var fact = Assert.IsType<HardcodedSetValueFact>(
            Assert.Single(sut.Produce(Document("LandBehavior", "AirUnit"), Uri)));

        Assert.Equal("AirUnit", fact.Token);
    }

    // -- where it applies --------------------------------------------------------------------

    [Fact]
    public void AppliesToNothing_WhenTheSchemaHasNoHardcodedSets()
    {
        var sut = Build(BehaviorTag("Behavior"), null);

        Assert.False(sut.AppliesTo(Uri));
        Assert.Empty(sut.Produce(Document("Behavior", "SOME_UNKNOWN_MODULE"), Uri));
    }

    [Fact]
    public void AppliesToAnUnregisteredFile_SinceNothingSaysWhatItHolds()
    {
        var sut = Build(BehaviorTag("Behavior"), BehaviorModuleSet(Value("GenericTransport")));

        Assert.True(sut.AppliesTo(Uri));
    }

    [Fact]
    public void DoesNotApplyToAFile_WhoseTypesDeclareNoHardcodedSetTag()
    {
        // The file type "CommandBarComponent" declares no hardcoded-set tag, even though a global
        // "Behavior" tag exists: the walk is skipped and an invalid value there says nothing.
        var registry = new FakeFileTypeRegistry().With(Uri, "CommandBarComponent");
        var sut = Build(BehaviorTag("Behavior"), BehaviorModuleSet(Value("GenericTransport")), registry);

        Assert.False(sut.AppliesTo(Uri));
    }

    [Fact]
    public void AppliesToAFile_WhoseTypeDeclaresAHardcodedSetTag()
    {
        var registry = new FakeFileTypeRegistry().With(Uri, "GameObject");
        var sut = Build(BehaviorTag("Behavior"), BehaviorModuleSet(Value("GenericTransport")), registry, "GameObject");

        Assert.True(sut.AppliesTo(Uri));
        Assert.Equal("hardcodedRefs", sut.Name);
    }
}