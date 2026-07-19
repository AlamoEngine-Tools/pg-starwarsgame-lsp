// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Writer;

namespace PG.StarWarsGame.LSP.Story.Tests.Writer;

/// <summary>
///     Round-trip fixtures: every test applies the writer's edits to the input and asserts the
///     EXACT resulting text — the byte-identical-remainder guarantee is the whole point.
/// </summary>
public sealed class StoryXmlWriterTest
{
    private const string Uri = "file:///ws/data/xml/story_test.xml";

    // Vanilla-style fixture: tabs, comments, blank lines, one tag per line.
    private const string Fixture =
        "<?xml version=\"1.0\" ?>\n" +
        "<Story>\n" +
        "\n" +
        "\t<!-- opening beat -->\n" +
        "\t<Event Name=\"Start\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t\t<Event_Param1>10</Event_Param1>\n" +
        "\t</Event>\n" +
        "\n" +
        "\t<Event Name=\"Next\">\n" +
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
        "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
        "\t\t<Reward_Param1>Start</Reward_Param1>\n" +
        "\t\t<Prereq>Start</Prereq>\n" +
        "\t\t<Prereq>AltA AltB</Prereq>\n" +
        "\t\t<Branch>Act1</Branch>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private static StoryThread Parse(string text)
    {
        return StoryThreadParser.Parse(text, Uri);
    }

    private static StoryEvent Event(string text, string name)
    {
        return Parse(text).Events.Single(e => e.Name == name);
    }

    private static string Apply(string text, IReadOnlyList<StoryTextEdit> edits)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                lineStarts.Add(i + 1);

        int Offset(int line, int column)
        {
            return line >= lineStarts.Count ? text.Length : Math.Min(lineStarts[line] + column, text.Length);
        }

        var result = text;
        foreach (var edit in edits
                     .OrderByDescending(e => Offset(e.Range.StartLine, e.Range.StartColumn)))
        {
            var start = Offset(edit.Range.StartLine, edit.Range.StartColumn);
            var end = Offset(edit.Range.EndLine, edit.Range.EndColumn);
            result = result[..start] + edit.NewText + result[end..];
        }

        return result;
    }

    // ── SetTagValue ──────────────────────────────────────────────────────────

    [Fact]
    public void SetTagValue_ReplacesExistingValue_RestByteIdentical()
    {
        var edits = StoryXmlWriter.SetTagValue(Fixture, Event(Fixture, "Next"), "Branch", "Act2");

        Assert.Equal(Fixture.Replace("<Branch>Act1</Branch>", "<Branch>Act2</Branch>"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void SetTagValue_NewTag_InsertsAtCanonicalPosition()
    {
        // Event_Filter ranks between Event_Param and Reward_Type — it must land after the params,
        // not at the end of the block.
        var edits = StoryXmlWriter.SetTagValue(Fixture, Event(Fixture, "Start"), "Event_Filter", "NONE");

        Assert.Equal(Fixture.Replace(
                "\t\t<Event_Param1>10</Event_Param1>\n",
                "\t\t<Event_Param1>10</Event_Param1>\n\t\t<Event_Filter>NONE</Event_Filter>\n"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void SetTagValue_NullValue_RemovesTheTagLine()
    {
        var edits = StoryXmlWriter.SetTagValue(Fixture, Event(Fixture, "Next"), "Branch", null);

        Assert.Equal(Fixture.Replace("\t\t<Branch>Act1</Branch>\n", ""), Apply(Fixture, edits));
    }

    [Fact]
    public void SetTagValue_MissingTagWithNullValue_NoEdits()
    {
        Assert.Empty(StoryXmlWriter.SetTagValue(Fixture, Event(Fixture, "Start"), "Branch", null));
    }

    [Fact]
    public void SetTagValue_EscapesXmlCharacters()
    {
        var edits = StoryXmlWriter.SetTagValue(Fixture, Event(Fixture, "Next"), "Branch", "A&B");

        Assert.Contains("<Branch>A&amp;B</Branch>", Apply(Fixture, edits));
    }

    // ── SetParams ────────────────────────────────────────────────────────────

    [Fact]
    public void SetParams_ReplaceAndInsert_KeepsNumericOrder()
    {
        var edits = StoryXmlWriter.SetParams(Fixture, Event(Fixture, "Start"), "Event_Param",
            [(0, "20"), (1, "Alpha"), (2, "Beta")]);

        Assert.Equal(Fixture
                .Replace("<Event_Param1>10</Event_Param1>", "<Event_Param1>20</Event_Param1>")
                .Replace(
                    "\t\t<Event_Param1>20</Event_Param1>\n",
                    "\t\t<Event_Param1>20</Event_Param1>\n" +
                    "\t\t<Event_Param2>Alpha</Event_Param2>\n" +
                    "\t\t<Event_Param3>Beta</Event_Param3>\n"),
            Apply(Fixture, edits)
                .Replace("<Event_Param1>10</Event_Param1>", "<Event_Param1>20</Event_Param1>"));
    }

    [Fact]
    public void SetParams_NullValue_RemovesSlot()
    {
        var edits = StoryXmlWriter.SetParams(Fixture, Event(Fixture, "Start"), "Event_Param", [(0, null)]);

        Assert.Equal(Fixture.Replace("\t\t<Event_Param1>10</Event_Param1>\n", ""), Apply(Fixture, edits));
    }

    // ── CreateEvent / DeleteEvent ────────────────────────────────────────────

    [Fact]
    public void CreateEvent_AppendsAfterLastEvent_WithDetectedIndentation()
    {
        var edits = StoryXmlWriter.CreateEvent(Fixture, Parse(Fixture), "Fresh", "STORY_TRIGGER", null);

        Assert.Equal(Fixture.Replace(
                "\t</Event>\n</Story>\n",
                "\t</Event>\n" +
                "\n" +
                "\t<Event Name=\"Fresh\">\n" +
                "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
                "\t</Event>\n" +
                "</Story>\n"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void CreateEvent_EmptyStoryFile_InsertsBeforeRootClose()
    {
        const string empty = "<?xml version=\"1.0\" ?>\n<Story>\n</Story>\n";

        var edits = StoryXmlWriter.CreateEvent(empty, Parse(empty), "First", "STORY_ELAPSED", "TRIGGER_EVENT");

        Assert.Equal(
            "<?xml version=\"1.0\" ?>\n<Story>\n" +
            "\n" +
            "\t<Event Name=\"First\">\n" +
            "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
            "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
            "\t</Event>\n" +
            "</Story>\n",
            Apply(empty, edits));
    }

    [Fact]
    public void DeleteEvent_RemovesTheWholeBlock_CommentStays()
    {
        var edits = StoryXmlWriter.DeleteEvent(Fixture, Event(Fixture, "Start"));

        var expected = Fixture.Replace(
            "\t<Event Name=\"Start\">\n" +
            "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
            "\t\t<Event_Param1>10</Event_Param1>\n" +
            "\t</Event>\n", "");
        Assert.Equal(expected, Apply(Fixture, edits));
        Assert.Contains("<!-- opening beat -->", Apply(Fixture, edits));
    }

    // ── Prereqs ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddPrereq_IntoExistingGroup_AppendsToTheAndLine()
    {
        var edits = StoryXmlWriter.AddPrereq(Fixture, Event(Fixture, "Next"), 1, "AltC");

        Assert.Equal(Fixture.Replace("<Prereq>AltA AltB</Prereq>", "<Prereq>AltA AltB AltC</Prereq>"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void AddPrereq_NewOrLine_AfterTheLastPrereq()
    {
        var edits = StoryXmlWriter.AddPrereq(Fixture, Event(Fixture, "Next"), null, "AltNew");

        Assert.Equal(Fixture.Replace(
                "\t\t<Prereq>AltA AltB</Prereq>\n",
                "\t\t<Prereq>AltA AltB</Prereq>\n\t\t<Prereq>AltNew</Prereq>\n"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void AddPrereq_FirstPrereq_LandsAtCanonicalPosition()
    {
        // "Start" has Event_Type + Event_Param1 — the new Prereq belongs after the params.
        var edits = StoryXmlWriter.AddPrereq(Fixture, Event(Fixture, "Start"), null, "Next");

        Assert.Equal(Fixture.Replace(
                "\t\t<Event_Param1>10</Event_Param1>\n",
                "\t\t<Event_Param1>10</Event_Param1>\n\t\t<Prereq>Next</Prereq>\n"),
            Apply(Fixture, edits));
    }

    // ── AddPrereqGroup — atomic multi-token new AND-line, for the AND-junction authoring gesture ─

    [Fact]
    public void AddPrereqGroup_MultipleTokens_JoinedIntoOneLine()
    {
        var edits = StoryXmlWriter.AddPrereqGroup(Fixture, Event(Fixture, "Next"), ["AltC", "AltD"]);

        Assert.Equal(Fixture.Replace(
                "\t\t<Prereq>AltA AltB</Prereq>\n",
                "\t\t<Prereq>AltA AltB</Prereq>\n\t\t<Prereq>AltC AltD</Prereq>\n"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void AddPrereqGroup_SingleToken_SameAsPlainAddPrereq()
    {
        var edits = StoryXmlWriter.AddPrereqGroup(Fixture, Event(Fixture, "Next"), ["AltNew"]);

        Assert.Equal(Fixture.Replace(
                "\t\t<Prereq>AltA AltB</Prereq>\n",
                "\t\t<Prereq>AltA AltB</Prereq>\n\t\t<Prereq>AltNew</Prereq>\n"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void AddPrereqGroup_FirstPrereq_LandsAtCanonicalPositionAndEscapes()
    {
        // "Start" has no existing Prereq — same insertion-point rule as AddPrereq — and the joined
        // value goes through XML escaping exactly once (not per-token then again on insert).
        var edits = StoryXmlWriter.AddPrereqGroup(Fixture, Event(Fixture, "Start"), ["Next", "A&B"]);

        Assert.Equal(Fixture.Replace(
                "\t\t<Event_Param1>10</Event_Param1>\n",
                "\t\t<Event_Param1>10</Event_Param1>\n\t\t<Prereq>Next A&amp;B</Prereq>\n"),
            Apply(Fixture, edits));
    }

    // ── ClearTypeBlock — atomic trigger/reward removal (immutable-type UI rule) ─

    [Fact]
    public void ClearTypeBlock_Event_RemovesTypeAndParamLines()
    {
        var edits = StoryXmlWriter.ClearTypeBlock(Fixture, Event(Fixture, "Start"), "Event");

        Assert.Equal(Fixture.Replace(
                "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n\t\t<Event_Param1>10</Event_Param1>\n", ""),
            Apply(Fixture, edits));
    }

    [Fact]
    public void ClearTypeBlock_Reward_LeavesTriggerAndPrereqsAlone()
    {
        var edits = StoryXmlWriter.ClearTypeBlock(Fixture, Event(Fixture, "Next"), "Reward");

        Assert.Equal(Fixture.Replace(
                "\t\t<Reward_Type>TRIGGER_EVENT</Reward_Type>\n\t\t<Reward_Param1>Start</Reward_Param1>\n", ""),
            Apply(Fixture, edits));
    }

    [Fact]
    public void ClearTypeBlock_Event_AlsoRemovesEventFilter()
    {
        const string withFilter =
            "<Story>\n" +
            "\t<Event Name=\"F\">\n" +
            "\t\t<Event_Type>STORY_WIN_BATTLES</Event_Type>\n" +
            "\t\t<Event_Param1>3</Event_Param1>\n" +
            "\t\t<Event_Filter>SPACE</Event_Filter>\n" +
            "\t\t<Reward_Type>CREDITS</Reward_Type>\n" +
            "\t\t<Reward_Param1>1000</Reward_Param1>\n" +
            "\t</Event>\n" +
            "</Story>\n";

        var edits = StoryXmlWriter.ClearTypeBlock(withFilter, Event(withFilter, "F"), "Event");

        Assert.Equal(withFilter.Replace(
                "\t\t<Event_Type>STORY_WIN_BATTLES</Event_Type>\n\t\t<Event_Param1>3</Event_Param1>\n" +
                "\t\t<Event_Filter>SPACE</Event_Filter>\n", ""),
            Apply(withFilter, edits));
    }

    [Fact]
    public void ClearTypeBlock_NoTypePresent_ProducesNoEdits()
    {
        const string bare = "<Story>\n\t<Event Name=\"B\">\n\t\t<Branch>Act1</Branch>\n\t</Event>\n</Story>\n";

        Assert.Empty(StoryXmlWriter.ClearTypeBlock(bare, Event(bare, "B"), "Reward"));
    }

    // ── AddPrereqAlternatives — one new OR-line per token, for the OR-junction authoring gesture ─

    [Fact]
    public void AddPrereqAlternatives_EachTokenGetsItsOwnLine_InOneMergedEdit()
    {
        var edits = StoryXmlWriter.AddPrereqAlternatives(Fixture, Event(Fixture, "Next"), ["AltC", "AltD"]);

        // One merged insert — N sequential addPrereq commands would race the detached applyEdits.
        Assert.Single(edits);
        Assert.Equal(Fixture.Replace(
                "\t\t<Prereq>AltA AltB</Prereq>\n",
                "\t\t<Prereq>AltA AltB</Prereq>\n\t\t<Prereq>AltC</Prereq>\n\t\t<Prereq>AltD</Prereq>\n"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void AddPrereqAlternatives_FirstPrereqs_LandAtCanonicalPositionAndEscape()
    {
        // "Start" has no existing Prereq — the lines land after the params, each escaped once.
        var edits = StoryXmlWriter.AddPrereqAlternatives(Fixture, Event(Fixture, "Start"), ["Next", "A&B"]);

        Assert.Equal(Fixture.Replace(
                "\t\t<Event_Param1>10</Event_Param1>\n",
                "\t\t<Event_Param1>10</Event_Param1>\n\t\t<Prereq>Next</Prereq>\n\t\t<Prereq>A&amp;B</Prereq>\n"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void RemovePrereq_MiddleToken_SwallowsSeparator()
    {
        var edits = StoryXmlWriter.RemovePrereq(Fixture, Event(Fixture, "Next"), 1, "AltB");

        Assert.Equal(Fixture.Replace("<Prereq>AltA AltB</Prereq>", "<Prereq>AltA</Prereq>"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void RemovePrereq_FirstOfTwoTokens_SwallowsSeparator()
    {
        var edits = StoryXmlWriter.RemovePrereq(Fixture, Event(Fixture, "Next"), 1, "AltA");

        Assert.Equal(Fixture.Replace("<Prereq>AltA AltB</Prereq>", "<Prereq>AltB</Prereq>"),
            Apply(Fixture, edits));
    }

    [Fact]
    public void RemovePrereq_LastToken_RemovesTheLine()
    {
        var edits = StoryXmlWriter.RemovePrereq(Fixture, Event(Fixture, "Next"), 0, "Start");

        Assert.Equal(Fixture.Replace("\t\t<Prereq>Start</Prereq>\n", ""), Apply(Fixture, edits));
    }

    [Fact]
    public void RemovePrereq_UnknownToken_NoEdits()
    {
        Assert.Empty(StoryXmlWriter.RemovePrereq(Fixture, Event(Fixture, "Next"), 0, "Ghost"));
    }

    [Fact]
    public void RemovePrereq_NoGroupIndex_RemovesTheTokenFromEveryLine()
    {
        // The edge-removal gesture doesn't know AND-line indices — the token goes everywhere.
        const string text =
            "<Story>\n" +
            "\t<Event Name=\"Multi\">\n" +
            "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
            "\t\t<Prereq>Start</Prereq>\n" +
            "\t\t<Prereq>Start AltA</Prereq>\n" +
            "\t</Event>\n" +
            "</Story>\n";

        var edits = StoryXmlWriter.RemovePrereq(text, Event(text, "Multi"), null, "Start");

        Assert.Equal(text
                .Replace("\t\t<Prereq>Start</Prereq>\n", "")
                .Replace("<Prereq>Start AltA</Prereq>", "<Prereq>AltA</Prereq>"),
            Apply(text, edits));
    }

    // ── CRLF fidelity ────────────────────────────────────────────────────────

    [Fact]
    public void Writer_CrlfDocument_EmitsCrlf()
    {
        var crlf = Fixture.Replace("\n", "\r\n");

        var edits = StoryXmlWriter.SetTagValue(crlf, Event(crlf, "Start"), "Event_Filter", "NONE");

        Assert.Equal(crlf.Replace(
                "\t\t<Event_Param1>10</Event_Param1>\r\n",
                "\t\t<Event_Param1>10</Event_Param1>\r\n\t\t<Event_Filter>NONE</Event_Filter>\r\n"),
            Apply(crlf, edits));
    }
}