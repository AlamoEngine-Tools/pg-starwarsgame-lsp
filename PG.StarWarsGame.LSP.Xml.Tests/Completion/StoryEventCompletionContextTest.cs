// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

public sealed class StoryEventCompletionContextTest
{
    private static HtmlDocument Parse(string xml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(xml);
        return doc;
    }

    // ── EventType ────────────────────────────────────────────────────────────

    [Fact]
    public void Read_CursorInsideEvent_WithEventType_ReturnsEventType()
    {
        var xml = "<StoryParser><Event><Event_Type>STORY_FLAG</Event_Type>\n<cursor/>\n</Event></StoryParser>";
        var doc = Parse(xml);

        var ctx = StoryEventCompletionContextReader.Read(doc, 1, 0);

        Assert.Equal("STORY_FLAG", ctx.EventType);
    }

    [Fact]
    public void Read_CursorInsideEvent_WithoutEventType_ReturnsNullEventType()
    {
        var xml = "<StoryParser><Event>\n<cursor/>\n</Event></StoryParser>";
        var doc = Parse(xml);

        var ctx = StoryEventCompletionContextReader.Read(doc, 1, 0);

        Assert.Null(ctx.EventType);
    }

    // ── RewardType ───────────────────────────────────────────────────────────

    [Fact]
    public void Read_CursorInsideEvent_WithRewardType_ReturnsRewardType()
    {
        var xml = "<StoryParser><Event><Reward_Type>CREDITS</Reward_Type>\n<cursor/>\n</Event></StoryParser>";
        var doc = Parse(xml);

        var ctx = StoryEventCompletionContextReader.Read(doc, 1, 0);

        Assert.Equal("CREDITS", ctx.RewardType);
    }

    [Fact]
    public void Read_CursorInsideEvent_BothTypesPresent_ReturnsBoth()
    {
        var xml = "<StoryParser><Event>" +
                  "<Event_Type>STORY_FLAG</Event_Type>" +
                  "<Reward_Type>CREDITS</Reward_Type>" +
                  "\n<cursor/>\n" +
                  "</Event></StoryParser>";
        var doc = Parse(xml);

        var ctx = StoryEventCompletionContextReader.Read(doc, 1, 0);

        Assert.Equal("STORY_FLAG", ctx.EventType);
        Assert.Equal("CREDITS", ctx.RewardType);
    }

    // ── Multi-event documents ─────────────────────────────────────────────────

    [Fact]
    public void Read_MultipleEvents_CursorInSecond_ReturnsSecondEventTypes()
    {
        var xml =
            "<StoryParser>" +
            "<Event><Event_Type>FIRST_EVENT</Event_Type></Event>\n" +
            "<Event><Event_Type>SECOND_EVENT</Event_Type>\n<cursor/>\n</Event>" +
            "</StoryParser>";
        var doc = Parse(xml);

        // Line 1 is the start of the second event; line 2 is where cursor is
        var ctx = StoryEventCompletionContextReader.Read(doc, 2, 0);

        Assert.Equal("SECOND_EVENT", ctx.EventType);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Read_CursorBeforeAnyEvent_ReturnsBothNull()
    {
        var xml = "<StoryParser>\n<cursor/>\n<Event><Event_Type>STORY_FLAG</Event_Type></Event></StoryParser>";
        var doc = Parse(xml);

        var ctx = StoryEventCompletionContextReader.Read(doc, 1, 0);

        Assert.Null(ctx.EventType);
        Assert.Null(ctx.RewardType);
    }

    [Fact]
    public void Read_EmptyDocument_ReturnsBothNull()
    {
        var doc = Parse("<StoryParser></StoryParser>");

        var ctx = StoryEventCompletionContextReader.Read(doc, 0, 5);

        Assert.Null(ctx.EventType);
        Assert.Null(ctx.RewardType);
    }
}