// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Tests.Model;

public sealed class StoryThreadParserTest
{
    private const string Uri = "file:///ws/data/xml/story_test.xml";

    private static StoryThread Parse(string xml)
    {
        return StoryThreadParser.Parse(xml, Uri);
    }

    // ── Event anatomy ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_VanillaShapedEvent_MapsAllSemanticFields()
    {
        var thread = Parse("""
                           <Story>
                             <Event Name="Rebel_ActI_Mission_One_10">
                               <Event_Type>STORY_DEPLOY</Event_Type>
                               <Event_Param1>Droids_Team</Event_Param1>
                               <Event_Param2>Wayland</Event_Param2>
                               <Reward_Type>LINK_TACTICAL</Reward_Type>
                               <Reward_Param1>Kuat</Reward_Param1>
                               <Reward_Param7>Story_Plots_Rebel_ActI_M01_SPACE.xml</Reward_Param7>
                               <Prereq>Rebel_ActI_Reveal_00 Rebel_ActI_Reveal_01</Prereq>
                               <Prereq>Rebel_ActI_Alternate</Prereq>
                               <Story_Dialog>Dialog_r_m01</Story_Dialog>
                               <Story_Chapter>2</Story_Chapter>
                               <Branch>MissionOne</Branch>
                               <Perpetual>True</Perpetual>
                             </Event>
                           </Story>
                           """);

        Assert.Empty(thread.Problems);
        var e = Assert.Single(thread.Events);
        Assert.Equal("Rebel_ActI_Mission_One_10", e.Name);
        Assert.Equal("STORY_DEPLOY", e.EventType);
        Assert.Equal("LINK_TACTICAL", e.RewardType);
        Assert.Equal([(0, "Droids_Team"), (1, "Wayland")],
            e.EventParams.Select(p => (p.Position, p.RawValue)));
        Assert.Equal([(0, "Kuat"), (6, "Story_Plots_Rebel_ActI_M01_SPACE.xml")],
            e.RewardParams.Select(p => (p.Position, p.RawValue)));
        Assert.Equal("MissionOne", e.Branch);
        Assert.True(e.Perpetual);
        Assert.Equal("Dialog_r_m01", e.StoryDialog);
        Assert.Equal(2, e.StoryChapter);
    }

    [Fact]
    public void Parse_PrereqLines_AreOrGroupsOfAndTokens()
    {
        var thread = Parse("""
                           <Story>
                             <Event Name="E">
                               <Event_Type>STORY_TRIGGER</Event_Type>
                               <Prereq>A B C</Prereq>
                               <Prereq>D</Prereq>
                             </Event>
                           </Story>
                           """);

        var e = Assert.Single(thread.Events);
        Assert.Equal(2, e.PrereqGroups.Count);
        Assert.Equal(["A", "B", "C"], e.PrereqGroups[0].Tokens.Select(t => t.Text));
        Assert.Equal(["D"], e.PrereqGroups[1].Tokens.Select(t => t.Text));
    }

    [Fact]
    public void Parse_PrereqTokens_CarryColumnAccurateRanges()
    {
        var thread = Parse("<Story>\n<Event Name=\"E\">\n<Prereq>Alpha Beta</Prereq>\n</Event>\n</Story>");

        var group = Assert.Single(Assert.Single(thread.Events).PrereqGroups);
        var beta = group.Tokens[1];
        Assert.Equal("Beta", beta.Text);
        Assert.Equal(2, beta.Range.StartLine);
        Assert.Equal("<Prereq>Alpha ".Length, beta.Range.StartColumn);
        Assert.Equal("<Prereq>Alpha Beta".Length, beta.Range.EndColumn);
    }

    [Fact]
    public void Parse_EventName_CarriesRange()
    {
        var thread = Parse("<Story>\n<Event Name=\"My_Event\">\n</Event>\n</Story>");

        var e = Assert.Single(thread.Events);
        Assert.Equal(1, e.NameRange.StartLine);
        Assert.Equal("<Event Name=\"".Length, e.NameRange.StartColumn);
        Assert.Equal("<Event Name=\"My_Event".Length, e.NameRange.EndColumn);
    }

    [Fact]
    public void Parse_Tags_PreserveDocumentOrderAndOriginalCasing()
    {
        var thread = Parse("""
                           <Story>
                             <Event Name="E">
                               <Reward_Type>CREDITS</Reward_Type>
                               <Event_Type>STORY_TRIGGER</Event_Type>
                             </Event>
                           </Story>
                           """);

        var e = Assert.Single(thread.Events);
        Assert.Equal(["Reward_Type", "Event_Type"], e.Tags.Select(t => t.Name));
        Assert.Equal("CREDITS", e.Tags[0].Value);
    }

    [Fact]
    public void Parse_ValuesWithSurroundingWhitespace_AreTrimmed()
    {
        var thread = Parse("<Story><Event Name=\"E\"><Event_Type> STORY_TRIGGER </Event_Type></Event></Story>");

        Assert.Equal("STORY_TRIGGER", Assert.Single(thread.Events).EventType);
    }

    [Fact]
    public void Parse_PerpetualVariants_ParseCaseInsensitively()
    {
        var thread = Parse("""
                           <Story>
                             <Event Name="A"><Perpetual>true</Perpetual></Event>
                             <Event Name="B"><Perpetual>Yes</Perpetual></Event>
                             <Event Name="C"><Perpetual>False</Perpetual></Event>
                             <Event Name="D"/>
                           </Story>
                           """);

        Assert.Equal([true, true, false, false], thread.Events.Select(e => e.Perpetual));
    }

    // ── Problems ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EventWithoutName_ProducesProblemAndIsSkipped()
    {
        var thread = Parse("<Story>\n<Event>\n<Event_Type>STORY_TRIGGER</Event_Type>\n</Event>\n</Story>");

        Assert.Empty(thread.Events);
        var problem = Assert.Single(thread.Problems);
        Assert.Equal(1, problem.Range.StartLine);
        Assert.Contains("Name", problem.Message);
    }

    [Fact]
    public void Parse_DuplicateEventNames_AreBothKept()
    {
        // Duplicate detection is a graph-level diagnostic; the parser stays faithful to the file.
        var thread = Parse("""
                           <Story>
                             <Event Name="Twice"><Event_Type>STORY_TRIGGER</Event_Type></Event>
                             <Event Name="Twice"><Event_Type>STORY_ELAPSED</Event_Type></Event>
                           </Story>
                           """);

        Assert.Equal(2, thread.Events.Count);
    }

    [Fact]
    public void Parse_CommentsInsideEvents_AreIgnored()
    {
        var thread = Parse("""
                           <Story>
                             <!-- setup -->
                             <Event Name="E">
                               <!-- trigger -->
                               <Event_Type>STORY_TRIGGER</Event_Type>
                             </Event>
                           </Story>
                           """);

        var e = Assert.Single(thread.Events);
        Assert.Equal("STORY_TRIGGER", e.EventType);
        Assert.Equal(["Event_Type"], e.Tags.Select(t => t.Name));
    }

    [Fact]
    public void Parse_EmptyDocument_YieldsEmptyThread()
    {
        var thread = Parse("<Story/>");

        Assert.Empty(thread.Events);
        Assert.Empty(thread.Problems);
        Assert.Equal(Uri, thread.DocumentUri);
    }
}