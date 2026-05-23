// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
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

    private static XmlTagDefinition TagWith(EnumDefinition? enumDef = null, TagSemanticType sem = TagSemanticType.Default)
    {
        return new XmlTagDefinition
            { Tag = "Event_Type", ValueType = XmlValueType.DynamicEnumValue, Enum = enumDef, SemanticType = sem };
    }

    [Fact]
    public void ValueType_is_DynamicEnumValue()
    {
        var sut = new DynamicEnumValueProposalProvider();
        Assert.Equal(XmlValueType.DynamicEnumValue, sut.ValueType);
    }

    [Fact]
    public void Returns_all_proposals_when_partial_is_empty()
    {
        var sut = new DynamicEnumValueProposalProvider();
        var proposals = sut.GetProposals(TagWith(StoryEvents), "");
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
        var sut = new DynamicEnumValueProposalProvider();
        var proposals = sut.GetProposals(TagWith(StoryEvents), partial);
        Assert.Equal(expectedCount, proposals.Count);
    }

    [Fact]
    public void Detail_contains_english_description()
    {
        var sut = new DynamicEnumValueProposalProvider();
        var proposals = sut.GetProposals(TagWith(StoryEvents), "STORY_ACCUMULATE");
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
        var sut = new DynamicEnumValueProposalProvider();
        var proposals = sut.GetProposals(TagWith(bare), "");
        var proposal = Assert.Single(proposals);
        Assert.Null(proposal.Detail);
    }

    [Fact]
    public void Returns_empty_when_tag_has_no_enum()
    {
        var sut = new DynamicEnumValueProposalProvider();
        var proposals = sut.GetProposals(TagWith(null), "");
        Assert.Empty(proposals);
    }

    [Fact]
    public void Returns_empty_when_enum_not_in_schema()
    {
        var sut = new DynamicEnumValueProposalProvider();
        var proposals = sut.GetProposals(TagWith(null), "");
        Assert.Empty(proposals);
    }

    [Fact]
    public void FlagList_excludes_already_selected_tokens_separated_by_pipe()
    {
        var sut = new DynamicEnumValueProposalProvider();
        var flagTag = TagWith(StoryEvents, TagSemanticType.FlagList);
        // "STORY_ACCUMULATE" is already typed; only the last empty partial is active
        var proposals = sut.GetProposals(flagTag, "STORY_ACCUMULATE | ");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ACCUMULATE");
        Assert.Contains(proposals, p => p.Label == "STORY_CONQUER");
    }

    [Fact]
    public void FlagList_prefix_matches_last_token()
    {
        var sut = new DynamicEnumValueProposalProvider();
        var flagTag = TagWith(StoryEvents, TagSemanticType.FlagList);
        var proposals = sut.GetProposals(flagTag, "STORY_ACCUMULATE | STORY_C");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ACCUMULATE");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ELAPSED");
        Assert.All(proposals, p => Assert.StartsWith("STORY_C", p.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FlagList_comma_separator_also_handled()
    {
        var sut = new DynamicEnumValueProposalProvider();
        var flagTag = TagWith(StoryEvents, TagSemanticType.FlagList);
        var proposals = sut.GetProposals(flagTag, "STORY_ACCUMULATE, ");
        Assert.DoesNotContain(proposals, p => p.Label == "STORY_ACCUMULATE");
    }

    [Fact]
    public void Non_FlagList_tag_ignores_pipe_splitting_and_uses_full_partial()
    {
        var sut = new DynamicEnumValueProposalProvider();
        // Not a FlagList — partial with pipe should still work as a plain prefix filter
        var proposals = sut.GetProposals(TagWith(StoryEvents), "STORY_ACC");
        Assert.Single(proposals);
        Assert.Equal("STORY_ACCUMULATE", proposals[0].Label);
    }
}
