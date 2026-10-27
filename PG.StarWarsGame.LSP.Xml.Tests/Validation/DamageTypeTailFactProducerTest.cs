// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     The damage type tail as facts (#307): what used to be diagnostics the publisher built by hand
///     is now a producer whose facts a handler turns into the same two diagnostics.
/// </summary>
public sealed class DamageTypeTailFactProducerTest
{
    private const string Uri = "file:///gameconstants.xml";

    // The 20 values the engine hardcodes at fixed positions - must appear as the exact tail of
    // <Damage_Types>, in this exact order, or the game crashes at runtime.
    private static readonly string RequiredTail = string.Join(" ", DamageTypeTailFactProducer.RequiredTail);

    private static readonly DamageTypeTailFactProducer Sut = new();

    private static string GameConstantsWith(string damageTypesInner)
    {
        return $"<GameConstants><Damage_Types>{damageTypesInner}</Damage_Types></GameConstants>";
    }

    [Fact]
    public void AppliesToEveryDocument_SinceOnlyItsTextSaysWhetherItDeclaresTheList()
    {
        Assert.True(Sut.AppliesTo("file:///units.xml"));
        Assert.Equal("damageTypeOrder", Sut.Name);
    }

    [Fact]
    public void ExactRequiredTail_EmitsNoFacts()
    {
        var facts = Sut.Produce(GameConstantsWith($"MOD_DAMAGE_A MOD_DAMAGE_B {RequiredTail}"), Uri);

        Assert.Empty(facts);
    }

    [Fact]
    public void TailValueOutOfOrder_NamesTheEntryAndTheOneTheEngineReadsThere()
    {
        // Swap the first two required entries.
        var badTail = RequiredTail.Replace(
            "Damage_Normal Damage_Force_Whirlwind", "Damage_Force_Whirlwind Damage_Normal");

        var facts = Sut.Produce(GameConstantsWith(badTail), Uri).OfType<DamageTypeOutOfOrderFact>().ToList();

        Assert.Equal(2, facts.Count);
        Assert.Equal("Damage_Force_Whirlwind", facts[0].Token);
        Assert.Equal("Damage_Normal", facts[0].Expected);
        Assert.Equal("Damage_Normal", facts[1].Token);
        Assert.Equal("Damage_Force_Whirlwind", facts[1].Expected);
        Assert.All(facts, f => Assert.Equal(20, f.Required));
    }

    [Fact]
    public void OutOfOrderFact_SpansTheEntryAsWritten()
    {
        var badTail = RequiredTail.Replace(
            "Damage_Normal Damage_Force_Whirlwind", "Damage_Force_Whirlwind Damage_Normal");
        var xml = GameConstantsWith(badTail);

        var fact = Sut.Produce(xml, Uri).OfType<DamageTypeOutOfOrderFact>().First();

        Assert.Equal(0, fact.Line);
        Assert.Equal(xml.IndexOf("Damage_Force_Whirlwind", StringComparison.Ordinal), fact.Column);
        Assert.Equal("Damage_Force_Whirlwind".Length, fact.Length);
    }

    [Fact]
    public void MissingRequiredValue_IsReportedAtTheEntryThatTookItsPlace()
    {
        var badTail = RequiredTail.Replace("Damage_Drain_Life", "Damage_Something_Else");

        var fact = Assert.Single(Sut.Produce(GameConstantsWith(badTail), Uri).OfType<DamageTypeOutOfOrderFact>());

        Assert.Equal("Damage_Something_Else", fact.Token);
        Assert.Equal("Damage_Drain_Life", fact.Expected);
    }

    [Fact]
    public void ModValueAppendedAfterRequiredTail_ShiftsTheWholeWindow()
    {
        // The required tail must be at the VERY end - appending anything after it shifts the window.
        var facts = Sut.Produce(GameConstantsWith($"{RequiredTail} MOD_DAMAGE_AFTER"), Uri);

        Assert.NotEmpty(facts);
        Assert.All(facts, f => Assert.IsType<DamageTypeOutOfOrderFact>(f));
    }

    [Fact]
    public void TooFewTokens_EmitsOneIncompleteFactOnTheLastEntry()
    {
        var xml = GameConstantsWith("Damage_Normal Damage_Fire");

        var fact = Assert.IsType<DamageTypesIncompleteFact>(Assert.Single(Sut.Produce(xml, Uri)));

        Assert.Equal(2, fact.Count);
        Assert.Equal(20, fact.Required);
        Assert.Equal(xml.IndexOf("Damage_Fire", StringComparison.Ordinal), fact.Column);
        Assert.Equal("Damage_Fire".Length, fact.Length);
    }

    [Fact]
    public void EmptyList_EmitsOneIncompleteFactOnTheElement()
    {
        var fact = Assert.IsType<DamageTypesIncompleteFact>(Assert.Single(Sut.Produce(GameConstantsWith(""), Uri)));

        Assert.Equal(0, fact.Count);
        Assert.Equal(0, fact.Length);
    }

    [Fact]
    public void NoDamageTypesElement_EmitsNothing()
    {
        Assert.Empty(Sut.Produce("<GameObjectFiles><Unit Name=\"X\"/></GameObjectFiles>", "file:///units.xml"));
    }

    [Fact]
    public void BoundaryCommentBetweenModAndHardcodedValues_DoesNotAffectPositions()
    {
        // "ABOVE this point" boundary comment is a real convention in the shipped file - the tail
        // check must still work with it present, since a mod's own gameconstants.xml often retains it.
        var xml = GameConstantsWith($"MOD_DAMAGE_A <!-- Do not add anything ABOVE this point --> {RequiredTail}");

        Assert.Empty(Sut.Produce(xml, Uri));
    }
}