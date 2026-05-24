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

    private static XmlTagDefinition HardcodedTag(HardcodedReferenceSet? set, string? valueGroup = null)
    {
        return new XmlTagDefinition
        {
            Tag = "T",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.HardcodedSet,
            HardcodedSet = set,
            ValueGroup = valueGroup
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