// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     Line-based parser for story-dialog .txt scripts: <c>[CHAPTER n]</c> section headers,
///     <c>#</c> comment lines, one command per line. Purely structural - command semantics
///     (arity, argument types, references) are validated downstream against the
///     <c>StoryDialogCommand</c> schema enum.
/// </summary>
public static partial class StoryDialogParser
{
    [GeneratedRegex(@"^\[\s*CHAPTER\s+(\d+)\s*\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterHeader();

    public static StoryDialogDocument Parse(string text)
    {
        var chapters = new List<StoryDialogChapter>();
        var problems = new List<StoryDialogParseProblem>();
        var seenIndices = new HashSet<int>();
        List<StoryDialogCommand>? currentCommands = null;

        var lines = text.Split('\n');
        for (var lineNo = 0; lineNo < lines.Length; lineNo++)
        {
            var line = lines[lineNo].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith('['))
            {
                var match = ChapterHeader().Match(trimmed);
                if (!match.Success)
                {
                    problems.Add(new StoryDialogParseProblem(lineNo, indent, line.Length,
                        $"Malformed chapter header '{trimmed.TrimEnd()}': expected '[CHAPTER <number>]'."));
                    // Recover into an anonymous chapter so the commands below still get
                    // command-level validation without cascading "before first chapter" noise.
                    currentCommands = OpenChapter(chapters, StoryDialogChapter.AnonymousIndex, lineNo);
                    continue;
                }

                var index = int.Parse(match.Groups[1].Value);
                if (!seenIndices.Add(index))
                {
                    problems.Add(new StoryDialogParseProblem(lineNo, indent, line.Length,
                        $"Chapter {index} is already defined in this file."));
                    currentCommands = OpenChapter(chapters, StoryDialogChapter.AnonymousIndex, lineNo);
                    continue;
                }

                currentCommands = OpenChapter(chapters, index, lineNo);
                continue;
            }

            var tokens = Tokenize(line, lineNo);
            if (tokens.Count == 0) continue;

            if (currentCommands is null)
            {
                problems.Add(new StoryDialogParseProblem(lineNo, indent, line.Length,
                    $"'{tokens[0].Text}' appears before the first [CHAPTER <number>] header."));
                continue;
            }

            currentCommands.Add(ToCommand(tokens));
        }

        return new StoryDialogDocument(chapters, problems);
    }

    private static List<StoryDialogCommand> OpenChapter(List<StoryDialogChapter> chapters, int index, int headerLine)
    {
        var commands = new List<StoryDialogCommand>();
        chapters.Add(new StoryDialogChapter(index, headerLine, commands));
        return commands;
    }

    private static StoryDialogCommand ToCommand(List<StoryDialogToken> tokens)
    {
        var first = tokens[0];
        var name = first.Text.ToUpperInvariant();

        // Vanilla writes the doc's WAIT_SPEECH as two tokens: "WAIT SPEECH".
        if (name == "WAIT" && tokens.Count >= 2 &&
            tokens[1].Text.Equals("SPEECH", StringComparison.OrdinalIgnoreCase))
        {
            var second = tokens[1];
            var rawName = $"{first.Text} {second.Text}";
            return new StoryDialogCommand("WAIT_SPEECH", rawName, first.Line, first.Column,
                tokens.Skip(2).ToList());
        }

        return new StoryDialogCommand(name, first.Text, first.Line, first.Column,
            tokens.Skip(1).ToList());
    }

    private static List<StoryDialogToken> Tokenize(string line, int lineNo)
    {
        var tokens = new List<StoryDialogToken>();
        var i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            tokens.Add(new StoryDialogToken(line[start..i], lineNo, start));
        }

        return tokens;
    }
}