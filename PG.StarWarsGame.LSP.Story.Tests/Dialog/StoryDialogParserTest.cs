// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Story.Dialog;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

public sealed class StoryDialogParserTest
{
    private static StoryDialogDocument Parse(string text)
    {
        return StoryDialogParser.Parse(text);
    }

    [Fact]
    public void Parse_VanillaShapedFile_YieldsChaptersAndCommands()
    {
        var doc = Parse("""
                        [CHAPTER 0]

                        # Initial dialog box display
                        TITLE TEXT_STORY_TRANSMISSION_17
                        TEXTCOLOR 255 255 255 255
                        TEXT TEXT_STORY_TANI_00

                        WAIT SPEECH

                        [CHAPTER 1]
                        SFX Button_Press
                        """);

        Assert.Empty(doc.Problems);
        Assert.Equal(2, doc.Chapters.Count);

        var chapter0 = doc.Chapters[0];
        Assert.Equal(0, chapter0.Index);
        Assert.Equal(0, chapter0.HeaderLine);
        Assert.Equal(["TITLE", "TEXTCOLOR", "TEXT", "WAIT_SPEECH"],
            chapter0.Commands.Select(c => c.Name));
        Assert.Equal(["255", "255", "255", "255"], chapter0.Commands[1].Args.Select(a => a.Text));

        var chapter1 = doc.Chapters[1];
        Assert.Equal(1, chapter1.Index);
        var sfx = Assert.Single(chapter1.Commands);
        Assert.Equal(["Button_Press"], sfx.Args.Select(a => a.Text));
    }

    [Fact]
    public void Parse_WaitSpeech_TwoTokens_IsOneCommandWithoutArgs()
    {
        var doc = Parse("[CHAPTER 0]\nWAIT SPEECH");

        var cmd = Assert.Single(doc.Chapters[0].Commands);
        Assert.Equal("WAIT_SPEECH", cmd.Name);
        Assert.Equal("WAIT SPEECH", cmd.RawName);
        Assert.Empty(cmd.Args);
    }

    [Fact]
    public void Parse_WaitSpeech_SingleTokenDocForm_IsAccepted()
    {
        var doc = Parse("[CHAPTER 0]\nWAIT_SPEECH");

        var cmd = Assert.Single(doc.Chapters[0].Commands);
        Assert.Equal("WAIT_SPEECH", cmd.Name);
        Assert.Empty(cmd.Args);
    }

    [Fact]
    public void Parse_WaitWithNumericArg_StaysPlainWait()
    {
        var doc = Parse("[CHAPTER 0]\nWAIT 500");

        var cmd = Assert.Single(doc.Chapters[0].Commands);
        Assert.Equal("WAIT", cmd.Name);
        Assert.Equal(["500"], cmd.Args.Select(a => a.Text));
    }

    [Fact]
    public void Parse_CommandsAndArgs_CarryRawLinePositions()
    {
        var doc = Parse("[CHAPTER 0]\n  TEXT TEXT_KEY_A");

        var cmd = Assert.Single(doc.Chapters[0].Commands);
        Assert.Equal(1, cmd.Line);
        Assert.Equal(2, cmd.Column);
        var arg = Assert.Single(cmd.Args);
        Assert.Equal(1, arg.Line);
        Assert.Equal("  TEXT ".Length, arg.Column);
    }

    [Fact]
    public void Parse_CommandNames_AreCaseInsensitivelyNormalized()
    {
        var doc = Parse("[chapter 2]\ntext TEXT_KEY\nwait speech");

        Assert.Equal(2, doc.Chapters[0].Index);
        Assert.Equal(["TEXT", "WAIT_SPEECH"], doc.Chapters[0].Commands.Select(c => c.Name));
        Assert.Equal("text", doc.Chapters[0].Commands[0].RawName);
    }

    [Fact]
    public void Parse_CommentAndBlankLines_AreIgnored()
    {
        var doc = Parse("[CHAPTER 0]\n\n# a comment TEXT NOT_A_COMMAND\n   \nSFX S");

        Assert.Empty(doc.Problems);
        Assert.Equal(["SFX"], doc.Chapters[0].Commands.Select(c => c.Name));
    }

    [Fact]
    public void Parse_MalformedChapterHeader_ProducesProblem()
    {
        var doc = Parse("[CHAPTER X]\nTEXT TEXT_KEY");

        var problem = Assert.Single(doc.Problems);
        Assert.Equal(0, problem.Line);
        Assert.Contains("chapter header", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_CommandBeforeFirstChapter_ProducesProblem()
    {
        var doc = Parse("TEXT TEXT_KEY\n[CHAPTER 0]");

        var problem = Assert.Single(doc.Problems);
        Assert.Equal(0, problem.Line);
        Assert.Contains("[CHAPTER", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(doc.Chapters.SelectMany(c => c.Commands));
    }

    [Fact]
    public void Parse_DuplicateChapterIndex_ProducesProblemOnSecondHeader()
    {
        var doc = Parse("[CHAPTER 0]\n[CHAPTER 0]");

        var problem = Assert.Single(doc.Problems);
        Assert.Equal(1, problem.Line);
        Assert.Contains("already defined", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasChapter_ReflectsDefinedIndices()
    {
        var doc = Parse("[CHAPTER 0]\n[CHAPTER 2]");

        Assert.True(doc.HasChapter(0));
        Assert.False(doc.HasChapter(1));
        Assert.True(doc.HasChapter(2));
    }

    [Fact]
    public void Parse_CarriageReturns_DoNotLeakIntoTokens()
    {
        var doc = Parse("[CHAPTER 0]\r\nTEXT TEXT_KEY\r\n");

        var cmd = Assert.Single(doc.Chapters[0].Commands);
        Assert.Equal("TEXT", cmd.Name);
        Assert.Equal("TEXT_KEY", cmd.Args[0].Text);
    }
}