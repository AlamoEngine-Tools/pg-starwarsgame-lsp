// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     Scans leading trivia of any syntax node for <c>---</c>-prefixed EmmyLua doc-comment lines.
/// </summary>
internal static class LuaDocCommentScanner
{
    /// <summary>
    ///     Walks leading trivia backwards, collecting <c>---</c>-prefixed lines (prose + annotations)
    ///     with the <c>---</c> prefix stripped. Stops at a blank line or non-doc-comment trivia.
    /// </summary>
    public static IReadOnlyList<string> CollectLeadingDocLines(SyntaxNode node)
    {
        var trivia = node.GetFirstToken().LeadingTrivia;
        var lines = new List<string>();
        var seenEol = false;

        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            var text = trivia[i].ToFullString();

            if (text is "\n" or "\r\n" or "\r")
            {
                if (seenEol) break; // two consecutive EOLs = blank line → stop
                seenEol = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(text)) continue; // indentation — skip

            var trimmed = text.TrimStart();
            if (!trimmed.StartsWith("---", StringComparison.Ordinal)) break; // bare `--` → stop

            // Strip exactly the "---" prefix plus one optional space/tab
            var content = trimmed[3..];
            if (content.Length > 0 && (content[0] == ' ' || content[0] == '\t'))
                content = content[1..];
            lines.Add(content);
            seenEol = false;
        }

        lines.Reverse();
        return lines;
    }
}
