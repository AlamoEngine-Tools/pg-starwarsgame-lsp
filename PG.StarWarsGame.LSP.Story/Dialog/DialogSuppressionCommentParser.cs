// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     Finds suppression directives in a dialog script's <c>#</c> comments and resolves each to the
///     lines it covers.
///     <para>
///         The grammar is <see cref="SuppressionDirectiveParser" />, shared with XML and Lua. Dialog
///         scripts are line-oriented rather than nested, so the scopes anchor to what structure the
///         format does have: the next command line, or the enclosing <c>[CHAPTER n]</c> section.
///     </para>
/// </summary>
public static class DialogSuppressionCommentParser
{
    public static SuppressionScan Parse(string text, StoryDialogDocument document)
    {
        var lines = text.Split('\n');
        var ranges = new List<SuppressionRange>();
        var problems = new List<SuppressionCommentProblem>();

        for (var line = 0; line < lines.Length; line++)
        {
            var trimmed = lines[line].TrimStart();
            if (!trimmed.StartsWith('#')) continue;

            var result = SuppressionDirectiveParser.Parse(trimmed[1..]);
            if (!result.IsDirective) continue;

            problems.AddRange(result.Problems.Select(p => p.At(line)));

            if (result.Directive is not { } directive) continue;

            var (start, end) = directive.Scope switch
            {
                SuppressionScope.File => SuppressionSpans.File,
                SuppressionScope.Object => SuppressionSpans.ForTarget(ChapterSpan(document, lines, line), line),
                _ => SuppressionSpans.ForTarget(NextCommandLine(lines, line), line)
            };

            foreach (var matcher in directive.Matchers)
                ranges.Add(new SuppressionRange(
                    matcher, directive.Scope, start, end, line, directive.Reason));
        }

        return new SuppressionScan(ranges, problems);
    }

    /// <summary>
    ///     The next line carrying an actual command. Blank lines and further comments are skipped,
    ///     so a directive still reaches its command when the two are separated by a note.
    /// </summary>
    private static (int Start, int End)? NextCommandLine(string[] lines, int directiveLine)
    {
        for (var i = directiveLine + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            return (i, i);
        }

        return null;
    }

    /// <summary>
    ///     Header line of the <c>[CHAPTER n]</c> section containing <paramref name="line" />, or
    ///     null when it sits before the first header.
    ///     <para>
    ///         Public because the quick fix has to write <c>-object</c> directives into the same
    ///         chapter this resolves them against: if the two disagreed, the fix would insert a
    ///         directive that does not cover the diagnostic it was offered on.
    ///     </para>
    /// </summary>
    public static int? ChapterHeaderLine(StoryDialogDocument document, int line)
    {
        var header = document.Chapters
            .Where(c => c.HeaderLine >= 0 && c.HeaderLine <= line)
            .Select(c => (int?)c.HeaderLine)
            .Max();

        return header;
    }

    /// <summary>
    ///     Span of the <c>[CHAPTER n]</c> section containing the directive: its header through the
    ///     line before the next header, or the end of the file.
    /// </summary>
    private static (int Start, int End)? ChapterSpan(
        StoryDialogDocument document, string[] lines, int directiveLine)
    {
        if (ChapterHeaderLine(document, directiveLine) is not { } start) return null;

        var next = document.Chapters
            .Where(c => c.HeaderLine > start)
            .Select(c => (int?)c.HeaderLine)
            .Min();

        var end = next is { } n ? n - 1 : lines.Length - 1;
        return (start, Math.Max(start, end));
    }
}
