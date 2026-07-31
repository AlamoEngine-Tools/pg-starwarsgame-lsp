// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Story.Dialog;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

/// <summary>
///     Dialog's half of the suppression grammar: the same directives in <c>#</c> comments, anchored
///     to what a line-oriented format has - the next command, or the enclosing chapter.
/// </summary>
public sealed class DialogSuppressionCommentParserTest
{
    private static readonly DiagnosticId Unknown = DiagnosticIds.DialogUnknownCommand;
    private static readonly DiagnosticId Arity = DiagnosticIds.DialogCommandArity;

    private static IReadOnlyList<SuppressionRange> Parse(string text)
    {
        return Scan(text).Ranges;
    }

    private static SuppressionScan Scan(string text)
    {
        return DialogSuppressionCommentParser.Parse(text, StoryDialogParser.Parse(text));
    }

    [Fact]
    public void AHashComment_CarriesADirective()
    {
        var range = Assert.Single(Parse($"[CHAPTER 0]\n# aetswg:suppress {Unknown}\nWAIT 500\n"));

        Assert.True(range.Matcher.Matches(Unknown));
    }

    [Fact]
    public void AnOrdinaryComment_ProducesNothing()
    {
        Assert.Empty(Parse("[CHAPTER 0]\n# Initial dialog box display\nWAIT 500\n"));
    }

    [Fact]
    public void NodeScope_CoversTheNextCommand()
    {
        var range = Assert.Single(Parse($"[CHAPTER 0]\n# aetswg:suppress {Unknown}\nWAIT 500\nSFX Button_Press\n"));

        Assert.True(range.Covers(Unknown, 2));
        Assert.False(range.Covers(Unknown, 3));
    }

    // A directive separated from its command by a blank line or a note still reaches it - authors
    // group these scripts with whitespace and prose throughout.
    [Fact]
    public void NodeScope_SkipsBlankLinesAndOtherComments()
    {
        var range = Assert.Single(Parse(
            $"[CHAPTER 0]\n# aetswg:suppress {Unknown}\n\n# Main Text\nWAIT 500\n"));

        Assert.True(range.Covers(Unknown, 4));
    }

    [Fact]
    public void ObjectScope_CoversTheEnclosingChapter()
    {
        var range = Assert.Single(Parse(
            $"[CHAPTER 0]\nWAIT 100\n[CHAPTER 1]\n# aetswg:suppress-object {Unknown}\nWAIT 500\n[CHAPTER 2]\nWAIT 900\n"));

        Assert.False(range.Covers(Unknown, 1)); // chapter 0
        Assert.True(range.Covers(Unknown, 2)); // chapter 1 header
        Assert.True(range.Covers(Unknown, 4)); // chapter 1 body
        Assert.False(range.Covers(Unknown, 6)); // chapter 2
    }

    [Fact]
    public void ObjectScope_InTheLastChapter_RunsToEndOfFile()
    {
        var range = Assert.Single(Parse(
            $"[CHAPTER 0]\n# aetswg:suppress-object {Unknown}\nWAIT 500\nSFX Button_Press\n"));

        Assert.True(range.Covers(Unknown, 3));
    }

    [Fact]
    public void FileScope_CoversEveryLine()
    {
        var range = Assert.Single(Parse(
            $"[CHAPTER 0]\nWAIT 100\n# aetswg:suppress-file {Unknown}\nWAIT 500\n"));

        Assert.True(range.Covers(Unknown, 0));
        Assert.True(range.Covers(Unknown, 99));
    }

    // Silencing more than was asked for is the worse failure.
    [Fact]
    public void ObjectScopeBeforeAnyChapter_CollapsesToItsOwnLine()
    {
        var range = Assert.Single(Parse($"# aetswg:suppress-object {Unknown}\n[CHAPTER 0]\nWAIT 500\n"));

        Assert.True(range.Covers(Unknown, 0));
        Assert.False(range.Covers(Unknown, 2));
    }

    [Fact]
    public void ADirectiveNamingSeveralIds_ProducesOneRangePerId()
    {
        var ranges = Parse($"[CHAPTER 0]\n# aetswg:suppress {Unknown}, {Arity}\nWAIT 500\n");

        Assert.Equal(2, ranges.Count);
        Assert.Contains(ranges, r => r.Matcher.Matches(Unknown));
        Assert.Contains(ranges, r => r.Matcher.Matches(Arity));
    }

    [Fact]
    public void AReason_IsCarriedOntoTheRange()
    {
        var range = Assert.Single(Parse(
            $"[CHAPTER 0]\n# aetswg:suppress {Unknown} reason:: engine accepts it\nWAIT 500\n"));

        Assert.Equal("engine accepts it", range.Reason);
    }

    // ── reported problems ─────────────────────────────────────────────────────

    [Fact]
    public void AMistypedId_IsReportedAgainstTheCommentsOwnLine()
    {
        var problem = Assert.Single(Scan("[CHAPTER 0]\n# aetswg:suppress nonsense\nWAIT 500\n").Problems);

        Assert.Equal(1, problem.Line);
        Assert.Equal(DiagnosticIds.SuppressionUnknownRule, problem.Id);
    }

    [Fact]
    public void OrdinaryComments_ReportNothing()
    {
        Assert.Empty(Scan("[CHAPTER 0]\n# a note about this chapter\nWAIT 500\n").Problems);
    }
}
