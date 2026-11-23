// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Story;
using static PG.StarWarsGame.LSP.Server.Tests.Story.StoryCommandTestFixtures;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     What a save through the story graph does to a thread file somebody wrote by hand (#339 FR-3):
///     the file differs in the lines the edit is about and in nothing else.
///     <para>
///         The thread here holds what the graph does not model - comments, a tag no schema knows, a
///         reward written before its trigger, odd spacing inside a tag, single quotes, an entity, a
///         blank line, a stray root tag - in both line endings. Each case stages one command, saves,
///         and compares the saved text with the original line by line.
///     </para>
/// </summary>
public sealed class StoryBatchRoundTripTest
{
    private static readonly string[] ThreadLines =
    [
        "<?xml version='1.0' ?>",
        "<!-- The opening act. Hand-written; keep the order. -->",
        "<Story>",
        "",
        "  <!-- reward before trigger, on purpose -->",
        "  <Event   Name=\"Start\" >",
        "    <Custom_Tool_Tag attr='1'>kept &amp; untouched</Custom_Tool_Tag>",
        "    <Reward_Param1>500</Reward_Param1>",
        "    <Reward_Type>CREDITS</Reward_Type>",
        "    <Event_Type>STORY_ELAPSED</Event_Type><!-- trailing -->",
        "    <Event_Param1>10</Event_Param1>",
        "  </Event>",
        "",
        "\t<Event Name=\"Next\">",
        "\t\t<Prereq>Start</Prereq>",
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>",
        "\t\t<Story_Dialog>OLD_DIALOG</Story_Dialog>",
        "\t</Event>",
        "  <Stray_Root_Tag/>",
        "</Story>",
        "<!-- after the root -->"
    ];

    private static string Thread(string lineEnding)
    {
        return string.Join(lineEnding, ThreadLines) + lineEnding;
    }

    private static async Task<string> SaveAsync(string original, params StoryCommandDto[] commands)
    {
        var result = await Composer(threadText: original)
            .ComposeAsync(Subject, Staged(commands), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var change = Assert.Single(result.Edit!.DocumentChanges!, c => c.IsTextDocumentEdit);
        return change.TextDocumentEdit!.Edits.Single().NewText;
    }

    /// <summary>The lines only the original holds and the lines only the saved text holds, endings included.</summary>
    private static (List<string> Removed, List<string> Added) Diff(string original, string saved)
    {
        var before = Lines(original);
        var after = Lines(saved);

        // Longest common subsequence over whole lines: what it leaves out on either side is the change.
        var common = new int[before.Count + 1, after.Count + 1];
        for (var i = before.Count - 1; i >= 0; i--)
        for (var j = after.Count - 1; j >= 0; j--)
            common[i, j] = before[i] == after[j]
                ? common[i + 1, j + 1] + 1
                : Math.Max(common[i + 1, j], common[i, j + 1]);

        var removed = new List<string>();
        var added = new List<string>();
        int a = 0, b = 0;
        while (a < before.Count && b < after.Count)
            if (before[a] == after[b])
            {
                a++;
                b++;
            }
            else if (common[a + 1, b] >= common[a, b + 1])
            {
                removed.Add(before[a++]);
            }
            else
            {
                added.Add(after[b++]);
            }

        removed.AddRange(before.Skip(a));
        added.AddRange(after.Skip(b));
        return (removed, added);
    }

    /// <summary>The text cut after every line feed, so a line carries its own ending.</summary>
    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var feed = text.IndexOf('\n', start);
            var end = feed < 0 ? text.Length : feed + 1;
            lines.Add(text[start..end]);
            start = end;
        }

        return lines;
    }

    private static string Describe(IEnumerable<string> lines)
    {
        return string.Join(" | ", lines.Select(l => l.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")));
    }

    public static TheoryData<string> LineEndings => ["\n", "\r\n"];

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task SetPerpetual_AddsItsLine_AndTouchesNoOther(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original, Cmd("setPerpetual", eventName: "Start", flag: true));

        var (removed, added) = Diff(original, saved);
        Assert.True(removed.Count == 0, $"Lost: {Describe(removed)}");
        var line = Assert.Single(added);
        Assert.Contains("<Perpetual>Yes</Perpetual>", line);
        Assert.EndsWith(lineEnding, line);
        Assert.False(lineEnding == "\n" && line.EndsWith("\r\n", StringComparison.Ordinal),
            $"A file of line feeds gained a carriage return: {Describe(added)}");
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task SetBranch_AddsItsLine_AndTouchesNoOther(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original, Cmd("setBranch", eventName: "Start", value: "Act1"));

        var (removed, added) = Diff(original, saved);
        Assert.True(removed.Count == 0, $"Lost: {Describe(removed)}");
        var line = Assert.Single(added);
        Assert.Contains("<Branch>Act1</Branch>", line);
        Assert.EndsWith(lineEnding, line);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task SetDialog_ChangesTheValue_AndKeepsTheLinesIndentAndEnding(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original, Cmd("setDialog", eventName: "Next", value: "NEW_DIALOG"));

        var (removed, added) = Diff(original, saved);
        Assert.Equal($"\t\t<Story_Dialog>OLD_DIALOG</Story_Dialog>{lineEnding}", Assert.Single(removed));
        Assert.Equal($"\t\t<Story_Dialog>NEW_DIALOG</Story_Dialog>{lineEnding}", Assert.Single(added));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task SetEventType_ChangesTheValue_AndKeepsTheCommentBesideIt(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original, Cmd("setEventType", eventName: "Start", value: "STORY_FLAGS"));

        var (removed, added) = Diff(original, saved);
        Assert.Equal($"    <Event_Type>STORY_ELAPSED</Event_Type><!-- trailing -->{lineEnding}",
            Assert.Single(removed));
        Assert.Equal($"    <Event_Type>STORY_FLAGS</Event_Type><!-- trailing -->{lineEnding}",
            Assert.Single(added));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task RenameEvent_ChangesTheNameWhereItIsWritten_AndKeepsTheSpacingAroundIt(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original, Cmd("renameEvent", eventName: "Start", newName: "Opening"));

        var (removed, added) = Diff(original, saved);
        Assert.Equal(
            [$"  <Event   Name=\"Start\" >{lineEnding}", $"\t\t<Prereq>Start</Prereq>{lineEnding}"],
            removed);
        Assert.Equal(
            [$"  <Event   Name=\"Opening\" >{lineEnding}", $"\t\t<Prereq>Opening</Prereq>{lineEnding}"],
            added);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task DeleteEvent_RemovesTheEventsOwnLines_AndTouchesNoOther(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original, Cmd("deleteEvent", eventName: "Next"));

        var (removed, added) = Diff(original, saved);
        Assert.True(added.Count == 0, $"Gained: {Describe(added)}");
        Assert.Equal(
            ThreadLines.Skip(13).Take(5).Select(l => l + lineEnding).ToList(),
            removed.Where(l => l.Trim().Length > 0).ToList());
        Assert.Contains($"  <Stray_Root_Tag/>{lineEnding}", saved);
        Assert.Contains($"  <!-- reward before trigger, on purpose -->{lineEnding}", saved);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task CreateEvent_AddsItsLines_AndTouchesNoOther(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original,
            Cmd("createEvent", newName: "Fresh", eventType: "STORY_TRIGGER"));

        var (removed, added) = Diff(original, saved);
        Assert.True(removed.Count == 0, $"Lost: {Describe(removed)}");
        Assert.Contains(added, l => l.Contains("<Event Name=\"Fresh\">", StringComparison.Ordinal));
        Assert.All(added, l => Assert.EndsWith(lineEnding, l));
        Assert.DoesNotContain(added,
            l => lineEnding == "\n" && l.EndsWith("\r\n", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task ABatchOfEdits_LeavesEveryUnmodelledLineWhereItWas(string lineEnding)
    {
        var original = Thread(lineEnding);

        var saved = await SaveAsync(original,
            Cmd("setPerpetual", eventName: "Start", flag: true),
            Cmd("setDialog", eventName: "Next", value: "NEW_DIALOG"),
            Cmd("renameEvent", eventName: "Start", newName: "Opening"));

        var unmodelled = new[] { 0, 1, 3, 4, 6, 12, 18, 20 }.Select(i => ThreadLines[i] + lineEnding).ToList();
        var kept = Lines(saved).Where(unmodelled.Contains).ToList();
        Assert.Equal(unmodelled, kept);
    }
}