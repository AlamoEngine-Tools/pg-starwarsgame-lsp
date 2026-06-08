// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion.Providers;

public sealed class LocalisationKeyCompletionProviderTest
{
    private static readonly LocalisationKeyCompletionProvider Provider = new();

    private static readonly XmlTagDefinition LocKeyTag = new()
    {
        Tag = "Text_ID",
        ValueType = XmlValueType.NameReference,
        ReferenceKind = ReferenceKind.LocalisationKey
    };

    private static readonly XmlTagDefinition NonLocKeyTag = new()
    {
        Tag = "Name",
        ValueType = XmlValueType.NameReference,
        ReferenceKind = ReferenceKind.XmlObject
    };

    private static GameIndex IndexWithKeys(params string[] keys)
    {
        return GameIndex.Empty with { Localisation = new StubLocIndex(keys) };
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_LocalisationKeyReferenceKind_True()
    {
        Assert.True(Provider.CanHandle(LocKeyTag));
    }

    [Fact]
    public void CanHandle_XmlObjectReferenceKind_False()
    {
        Assert.False(Provider.CanHandle(NonLocKeyTag));
    }

    [Fact]
    public void CanHandle_NoReferenceKind_False()
    {
        var tag = new XmlTagDefinition { Tag = "T", ValueType = XmlValueType.Float };
        Assert.False(Provider.CanHandle(tag));
    }

    // ── GetProposals — all keys ──────────────────────────────────────────────

    [Fact]
    public void GetProposals_EmptyPartial_ReturnsAllKeys()
    {
        var index = IndexWithKeys("TEXT_UNIT_NAME", "TEXT_FACTION_NAME");

        var result = Provider.GetProposals(LocKeyTag, "", index);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Label == "TEXT_UNIT_NAME");
        Assert.Contains(result, p => p.Label == "TEXT_FACTION_NAME");
    }

    [Fact]
    public void GetProposals_EmptyIndex_ReturnsEmpty()
    {
        var result = Provider.GetProposals(LocKeyTag, "", GameIndex.Empty);
        Assert.Empty(result);
    }

    // ── GetProposals — prefix filtering ─────────────────────────────────────

    [Fact]
    public void GetProposals_PartialPrefix_FiltersResults()
    {
        var index = IndexWithKeys("TEXT_UNIT_NAME", "TEXT_FACTION_NAME", "TOOLTIP_UNIT");

        var result = Provider.GetProposals(LocKeyTag, "TEXT_U", index);

        Assert.Single(result);
        Assert.Equal("TEXT_UNIT_NAME", result[0].Label);
    }

    [Fact]
    public void GetProposals_PrefixMatchIsCaseInsensitive()
    {
        var index = IndexWithKeys("TEXT_UNIT_NAME");

        var result = Provider.GetProposals(LocKeyTag, "text_u", index);

        Assert.Single(result);
        Assert.Equal("TEXT_UNIT_NAME", result[0].Label);
    }

    [Fact]
    public void GetProposals_NonMatchingPrefix_ReturnsEmpty()
    {
        var index = IndexWithKeys("TEXT_UNIT_NAME");

        var result = Provider.GetProposals(LocKeyTag, "TOOLTIP_", index);

        Assert.Empty(result);
    }
}

file sealed class StubLocIndex : ILocalisationIndex
{
    private readonly HashSet<string> _keys;

    public StubLocIndex(IEnumerable<string> keys)
    {
        _keys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    public bool ContainsKey(string key)
    {
        return _keys.Contains(key);
    }

    public IEnumerable<string> Keys => _keys;

    public string? GetValue(string key)
    {
        return null;
    }
}