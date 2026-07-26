// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Schema;

/// <summary>
///     The string-level half of the campaign story-attachment syntax lives in Core so both the
///     story chain scanner (Story) and the campaign diagnostics rule (Xml) read the same faction
///     and tag knowledge - the project dependency runs Story -> Xml, so neither can own it.
/// </summary>
public sealed class StoryNameTagSyntaxTest
{
    [Theory]
    [InlineData("Rebel_Story_Name")]
    [InlineData("Empire_Story_Name")]
    [InlineData("Underworld_Story_Name")]
    [InlineData("rebel_story_name")]
    public void FactionSpecificTags_are_recognised(string tagName)
    {
        Assert.True(StoryNameTagSyntax.IsStoryNameTag(tagName));
        Assert.True(StoryNameTagSyntax.IsFactionSpecificTag(tagName));
        Assert.False(StoryNameTagSyntax.IsGenericTag(tagName));
    }

    [Fact]
    public void GenericTag_is_recognised()
    {
        Assert.True(StoryNameTagSyntax.IsStoryNameTag("Story_Name"));
        Assert.True(StoryNameTagSyntax.IsGenericTag("story_name"));
        Assert.False(StoryNameTagSyntax.IsFactionSpecificTag("Story_Name"));
    }

    [Fact]
    public void UnrelatedTag_is_not_a_story_name_tag()
    {
        Assert.False(StoryNameTagSyntax.IsStoryNameTag("Text_ID"));
    }

    [Theory]
    [InlineData("Rebel", true)]
    [InlineData("empire", true)]
    [InlineData("Underworld", true)]
    [InlineData("Hutts", false)]
    public void MajorFactions_are_the_ones_with_a_dedicated_tag(string faction, bool expected)
    {
        Assert.Equal(expected, StoryNameTagSyntax.IsMajorFaction(faction));
    }

    [Fact]
    public void FactionTagFor_builds_the_dedicated_tag_name()
    {
        Assert.Equal("Rebel_Story_Name", StoryNameTagSyntax.FactionTagFor("Rebel"));
    }

    [Fact]
    public void ReadPairs_of_a_faction_tag_yields_the_whole_value_as_the_plot_file()
    {
        var pairs = StoryNameTagSyntax.ReadPairs("rebel_story_name", " Story_Plots_Rebel.xml ").ToList();

        var pair = Assert.Single(pairs);
        Assert.Equal("Rebel", pair.Faction);
        Assert.Equal("Story_Plots_Rebel.xml", pair.PlotFile);
    }

    [Fact]
    public void ReadPairs_of_the_generic_tag_splits_the_flat_tuple_list()
    {
        var pairs = StoryNameTagSyntax
            .ReadPairs("story_name", "Rebel, Story_Plots_R.xml, Hutts, Conquests\\Story_Plots_H.xml")
            .ToList();

        Assert.Equal(2, pairs.Count);
        Assert.Equal(("Rebel", "Story_Plots_R.xml"), pairs[0]);
        Assert.Equal(("Hutts", "Conquests\\Story_Plots_H.xml"), pairs[1]);
    }

    [Fact]
    public void ReadPairs_ignores_a_dangling_token()
    {
        // The real data ships a trailing comma; a faction with no plot file cannot attach anything.
        var pairs = StoryNameTagSyntax.ReadPairs("story_name", "Rebel, Story_Plots_R.xml, Hutts").ToList();

        Assert.Single(pairs);
    }

    [Fact]
    public void ReadPairs_of_an_empty_value_yields_nothing()
    {
        Assert.Empty(StoryNameTagSyntax.ReadPairs("rebel_story_name", "   "));
        Assert.Empty(StoryNameTagSyntax.ReadPairs("story_name", ""));
    }
}
