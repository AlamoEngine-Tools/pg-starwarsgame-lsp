// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion.Providers;

public sealed class HardcodedSetCompletionProviderTest
{
    private static readonly HardcodedSetCompletionProvider Provider = new();

    private static HardcodedReferenceSet Set(params (string name, string[] groups)[] entries)
    {
        return new HardcodedReferenceSet
        {
            Name = "TestSet",
            Values = entries.Select(e => new HardcodedReferenceSetValue
            {
                Name = e.name,
                Groups = e.groups
            }).ToList()
        };
    }

    private static XmlTagDefinition HardcodedTag(HardcodedReferenceSet? set, params string[] valueGroups)
    {
        return new XmlTagDefinition
        {
            Tag = "T",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.HardcodedSet,
            HardcodedSet = set,
            ValueGroups = valueGroups
        };
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_HardcodedSet_WithSet_True()
    {
        var tag = HardcodedTag(Set(("A", [])));
        Assert.True(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_HardcodedSet_NullSet_False()
    {
        var tag = HardcodedTag(null);
        Assert.False(Provider.CanHandle(tag));
    }

    [Fact]
    public void CanHandle_XmlObject_False()
    {
        var tag = new XmlTagDefinition
            { Tag = "T", ValueType = XmlValueType.NameReference, ReferenceKind = ReferenceKind.XmlObject };
        Assert.False(Provider.CanHandle(tag));
    }

    // ── GetProposals — no ValueGroup ──────────────────────────────────────────

    [Fact]
    public void GetProposals_NoValueGroup_ReturnsAllValues()
    {
        var tag = HardcodedTag(Set(("A", []), ("B", []), ("C", [])));
        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, p => p.Label == "A");
        Assert.Contains(result, p => p.Label == "B");
        Assert.Contains(result, p => p.Label == "C");
    }

    // ── GetProposals — ValueGroup filtering ───────────────────────────────────

    [Fact]
    public void GetProposals_ValueGroup_FiltersToGroup()
    {
        var tag = HardcodedTag(Set(("SpaceFighter", ["space"]), ("InfantryUnit", ["land"])), "space");
        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("SpaceFighter", result[0].Label);
    }

    [Fact]
    public void GetProposals_ValueGroup_NoGroupsOnValue_AlwaysIncluded()
    {
        var tag = HardcodedTag(Set(("GenericTransport", []), ("InfantryUnit", ["land"])), "space");
        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("GenericTransport", result[0].Label);
    }

    [Fact]
    public void GetProposals_ValueGroup_MatchIsCaseInsensitive()
    {
        var tag = HardcodedTag(Set(("SpaceFighter", ["Space"])), "space");
        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Single(result);
    }

    // ── GetProposals — multiple ValueGroups ──────────────────────────────────

    [Fact]
    public void GetProposals_MultipleValueGroups_MatchesValuesInAnyGroup()
    {
        var tag = HardcodedTag(
            Set(("LandUnit", ["land"]), ("SpaceUnit", ["space"]), ("AirUnit", ["air"])),
            "land", "space");

        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Label == "LandUnit");
        Assert.Contains(result, p => p.Label == "SpaceUnit");
    }

    [Fact]
    public void GetProposals_MultipleValueGroups_FirstGroupMatchesRankFirst()
    {
        var tag = HardcodedTag(
            Set(("SpaceUnit", ["space"]), ("LandUnit", ["land"]), ("Universal", [])),
            "land", "space");

        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Equal(3, result.Count);
        Assert.Equal("LandUnit", result[0].Label); // matches first group "land"
        Assert.Equal("SpaceUnit", result[1].Label); // matches second group "space"
        Assert.Equal("Universal", result[2].Label); // empty groups — always last
    }

    [Fact]
    public void GetProposals_MultipleValueGroups_EmptyGroupsValueRanksAfterAllGroupMatches()
    {
        var tag = HardcodedTag(
            Set(("Universal", []), ("Specific", ["land"])),
            "land", "space");

        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Equal(2, result.Count);
        Assert.Equal("Specific", result[0].Label);
        Assert.Equal("Universal", result[1].Label);
    }

    // ── GetProposals — partialValue filtering ─────────────────────────────────

    [Fact]
    public void GetProposals_FiltersByPartialValue()
    {
        var tag = HardcodedTag(Set(("A", []), ("B", [])));
        var result = Provider.GetProposals(tag, "A", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("A", result[0].Label);
    }

    [Fact]
    public void GetProposals_EmptyPartial_ReturnsAll()
    {
        var tag = HardcodedTag(Set(("A", []), ("B", [])));
        var result = Provider.GetProposals(tag, "", GameIndex.Empty);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetProposals_PartialMatchCaseInsensitive()
    {
        var tag = HardcodedTag(Set(("GenericTransport", [])));
        var result = Provider.GetProposals(tag, "generic", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("GenericTransport", result[0].Label);
    }
}