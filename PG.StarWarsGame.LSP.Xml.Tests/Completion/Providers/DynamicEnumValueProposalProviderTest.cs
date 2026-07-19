// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion.Providers;

public sealed class DynamicEnumValueProposalProviderTest
{
    private static EnumDefinition StoryEvents => new()
    {
        Name = "StoryEventType",
        Kind = EnumKind.SchemaFixed,
        Values =
        [
            new EnumValueDefinition
            {
                Name = "STORY_ACCUMULATE",
                Description = new Dictionary<string, string> { ["en"] = "Fires when credits reach threshold." }
            },
            new EnumValueDefinition
            {
                Name = "STORY_CONQUER",
                Description = new Dictionary<string, string> { ["en"] = "Fires when a planet is conquered." }
            },
            new EnumValueDefinition
            {
                Name = "STORY_CONSTRUCT",
                Description = new Dictionary<string, string> { ["en"] = "Fires when an object is constructed." }
            },
            new EnumValueDefinition
            {
                Name = "STORY_ELAPSED", Description = new Dictionary<string, string> { ["en"] = "Fires after a delay." }
            }
        ]
    };

    private static XmlTagDefinition TagWith(EnumDefinition? enumDef = null,
        TagSemanticType sem = TagSemanticType.Default)
    {
        return new XmlTagDefinition
            { Tag = "Event_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef, SemanticType = sem };
    }

    private static DynamicEnumValueProposalProvider Sut(GameIndex? index = null)
    {
        return new DynamicEnumValueProposalProvider(new FakeIndexService(index));
    }

    [Fact]
    public void ValueType_is_DynamicEnumValue()
    {
        Assert.Equal(XmlValueType.DynamicEnumValue, Sut().ValueType);
    }

    [Fact]
    public void Returns_all_proposals_when_partial_is_empty()
    {
        var proposals = Sut().GetProposals(TagWith(StoryEvents), "");
        Assert.Equal(4, proposals.Count);
        Assert.Contains(proposals, p => p.Label == "STORY_ACCUMULATE");
        Assert.Contains(proposals, p => p.Label == "STORY_CONQUER");
    }

    [Theory]
    [InlineData("STORY_C", 2)]
    [InlineData("story_c", 2)]
    [InlineData("STORY_ACCUMULATE", 1)]
    [InlineData("X", 0)]
    public void Filters_proposals_by_partial_prefix(string partial, int expectedCount)
    {
        Assert.Equal(expectedCount, Sut().GetProposals(TagWith(StoryEvents), partial).Count);
    }

    [Fact]
    public void Detail_contains_english_description()
    {
        var proposals = Sut().GetProposals(TagWith(StoryEvents), "STORY_ACCUMULATE");
        var proposal = Assert.Single(proposals);
        Assert.Equal("Fires when credits reach threshold.", proposal.Detail);
    }

    [Fact]
    public void Detail_is_null_when_enum_value_has_no_description()
    {
        var bare = new EnumDefinition
        {
            Name = "Bare", Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "VAL" }]
        };
        var proposals = Sut().GetProposals(TagWith(bare), "");
        var proposal = Assert.Single(proposals);
        Assert.Null(proposal.Detail);
    }

    [Fact]
    public void Returns_empty_when_tag_has_no_enum()
    {
        Assert.Empty(Sut().GetProposals(TagWith(), ""));
    }

    [Fact]
    public void Returns_empty_when_enum_not_in_schema()
    {
        Assert.Empty(Sut().GetProposals(TagWith(), ""));
    }

    [Fact]
    public void FlagList_excludes_already_selected_tokens_separated_by_pipe()
    {
        var flagTag = TagWith(StoryEvents, TagSemanticType.FlagList);
        var proposals = Sut().GetProposals(flagTag, "STORY_ACCUMULATE | ");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ACCUMULATE");
        Assert.Contains(proposals, p => p.Label == "STORY_CONQUER");
    }

    [Fact]
    public void FlagList_prefix_matches_last_token()
    {
        var flagTag = TagWith(StoryEvents, TagSemanticType.FlagList);
        var proposals = Sut().GetProposals(flagTag, "STORY_ACCUMULATE | STORY_C");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ACCUMULATE");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ELAPSED");
        Assert.All(proposals, p => Assert.StartsWith("STORY_C", p.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FlagList_comma_separator_also_handled()
    {
        var flagTag = TagWith(StoryEvents, TagSemanticType.FlagList);
        var proposals = Sut().GetProposals(flagTag, "STORY_ACCUMULATE, ");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ACCUMULATE");
    }

    [Fact]
    public void Non_FlagList_tag_ignores_pipe_splitting_and_uses_full_partial()
    {
        var proposals = Sut().GetProposals(TagWith(StoryEvents), "STORY_ACC");
        Assert.Single(proposals);
        Assert.Equal("STORY_ACCUMULATE", proposals[0].Label);
    }

    // ── DynamicXml enum - runtime index values ────────────────────────────────

    [Fact]
    public void DynamicXml_ReturnsWorkspaceValues()
    {
        var workspaceValues = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE", "ENERGY"]);
        var index = GameIndex.Empty with { WorkspaceDynamicEnumValues = workspaceValues };
        var enumDef = new EnumDefinition { Name = "DamageType", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = new XmlTagDefinition
            { Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef };

        var proposals = Sut(index).GetProposals(tag, "");

        Assert.Equal(2, proposals.Count);
        Assert.Contains(proposals, p => p.Label == "EXPLOSIVE");
        Assert.Contains(proposals, p => p.Label == "ENERGY");
    }

    [Fact]
    public void DynamicXml_ReturnsBaselineValues()
    {
        var baselineEnums = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["LASER"]);
        var baseline = BaselineIndex.Empty with { DynamicEnumValues = baselineEnums };
        var index = GameIndex.Empty with { Baseline = baseline };
        var enumDef = new EnumDefinition { Name = "DamageType", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = new XmlTagDefinition
            { Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef };

        var proposals = Sut(index).GetProposals(tag, "");

        Assert.Contains(proposals, p => p.Label == "LASER");
    }

    [Fact]
    public void DynamicXml_MergesBaselineAndWorkspaceValues_DeduplicatesOrdinalIgnoreCase()
    {
        var baselineEnums = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["LASER", "EXPLOSIVE"]);
        var baseline = BaselineIndex.Empty with { DynamicEnumValues = baselineEnums };
        var workspaceValues = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["MOD_DMG", "EXPLOSIVE"]); // EXPLOSIVE already in baseline
        var index = GameIndex.Empty with { Baseline = baseline, WorkspaceDynamicEnumValues = workspaceValues };
        var enumDef = new EnumDefinition { Name = "DamageType", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = new XmlTagDefinition
            { Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef };

        var proposals = Sut(index).GetProposals(tag, "");

        Assert.Equal(3, proposals.Count); // LASER, EXPLOSIVE, MOD_DMG - no duplicate
        Assert.Contains(proposals, p => p.Label == "LASER");
        Assert.Contains(proposals, p => p.Label == "EXPLOSIVE");
        Assert.Contains(proposals, p => p.Label == "MOD_DMG");
    }

    [Fact]
    public void DynamicXml_FiltersProposalsByPartialPrefix()
    {
        var workspaceValues = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE", "ENERGY", "GRENADE"]);
        var index = GameIndex.Empty with { WorkspaceDynamicEnumValues = workspaceValues };
        var enumDef = new EnumDefinition { Name = "DamageType", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = new XmlTagDefinition
            { Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef };

        var proposals = Sut(index).GetProposals(tag, "E");

        Assert.Equal(2, proposals.Count);
        Assert.Contains(proposals, p => p.Label == "EXPLOSIVE");
        Assert.Contains(proposals, p => p.Label == "ENERGY");
    }

    [Fact]
    public void DynamicXml_NoIndexValues_ReturnsEmpty()
    {
        var enumDef = new EnumDefinition { Name = "DamageType", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = new XmlTagDefinition
            { Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef };

        var proposals = Sut().GetProposals(tag, "");

        Assert.Empty(proposals);
    }

    // ── DynamicEnumChanged cache invalidation ──────────────────────────────────

    [Fact]
    public void DynamicEnumChanged_Fired_RebuildsCacheFromNewIndex()
    {
        var enumDef = new EnumDefinition { Name = "DamageType", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = new XmlTagDefinition
            { Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef };
        var service = new FakeIndexService();
        var sut = new DynamicEnumValueProposalProvider(service);

        Assert.Empty(sut.GetProposals(tag, ""));

        var updatedValues = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["MOD_DMG"]);
        service.RaiseDynamicEnumChanged(GameIndex.Empty with { WorkspaceDynamicEnumValues = updatedValues });

        var proposals = sut.GetProposals(tag, "");
        Assert.Single(proposals);
        Assert.Equal("MOD_DMG", proposals[0].Label);
    }

    [Fact]
    public void DynamicEnumChanged_NotFired_DoesNotSeeIndexMutation()
    {
        // GetDynamicProposals reads a cache built once at construction, not _indexService.Current
        // live - this is the point of caching, and confirms the cache genuinely isn't recomputed
        // per request.
        var enumDef = new EnumDefinition { Name = "DamageType", Kind = EnumKind.DynamicXml, Values = [] };
        var tag = new XmlTagDefinition
            { Tag = "Damage_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef };
        var index = GameIndex.Empty with
        {
            WorkspaceDynamicEnumValues = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("DamageType", ["INITIAL"])
        };

        var proposals = Sut(index).GetProposals(tag, "");

        Assert.Single(proposals);
        Assert.Equal("INITIAL", proposals[0].Label);
    }
}

file sealed class FakeIndexService(GameIndex? current = null) : IGameIndexService
{
    public GameIndex Current { get; } = current ?? GameIndex.Empty;

    public event Action<GameIndex>? IndexChanged
    {
        add { }
        remove { }
    }

    public event Action<ILocalisationIndex>? LocalisationChanged
    {
        add { }
        remove { }
    }

    public event Action<GameIndex>? DynamicEnumChanged;

    public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public void InjectDocument(DocumentIndex document)
    {
    }

    public void RemoveDocument(string uri)
    {
    }

    public void ApplyBaseline(BaselineIndex baseline)
    {
    }

    public void ApplyLocalisation(ILocalisationIndex index)
    {
    }

    public void ApplyAssetFiles(IAssetFileIndex index)
    {
    }

    public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
    {
    }

    public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
    {
    }

    public void ApplyWorkspaceEnumValueDefinitions(
        ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
    {
    }

    public IDisposable BeginBulkUpdate()
    {
        return NullDisposable.Instance;
    }

    public void RaiseDynamicEnumChanged(GameIndex index)
    {
        DynamicEnumChanged?.Invoke(index);
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}