// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>A whitespace-delimited token with its 0-based source position.</summary>
public sealed record StoryDialogToken(string Text, int Line, int Column);

/// <summary>
///     One command line. <see cref="Name" /> is the normalized upper-case command
///     (the vanilla two-token <c>WAIT SPEECH</c> form becomes <c>WAIT_SPEECH</c>);
///     <see cref="RawName" /> preserves the source spelling for messages and ranges.
/// </summary>
public sealed record StoryDialogCommand(
    string Name,
    string RawName,
    int Line,
    int Column,
    IReadOnlyList<StoryDialogToken> Args);

/// <summary>
///     A <c>[CHAPTER n]</c> section and the commands inside it. Sections opened by a malformed or
///     duplicate header carry <see cref="AnonymousIndex" /> - their commands still get validated,
///     but the section is not addressable via <c>Story_Chapter</c>.
/// </summary>
public sealed record StoryDialogChapter(int Index, int HeaderLine, IReadOnlyList<StoryDialogCommand> Commands)
{
    public const int AnonymousIndex = -1;
}

/// <summary>A structural defect found while parsing (0-based positions).</summary>
public sealed record StoryDialogParseProblem(int Line, int Column, int EndColumn, string Message);

/// <summary>The parsed shape of a story-dialog .txt script.</summary>
public sealed record StoryDialogDocument(
    IReadOnlyList<StoryDialogChapter> Chapters,
    IReadOnlyList<StoryDialogParseProblem> Problems)
{
    public static readonly StoryDialogDocument Empty = new([], []);

    public bool HasChapter(int index)
    {
        return Chapters.Any(c => c.Index == index);
    }
}