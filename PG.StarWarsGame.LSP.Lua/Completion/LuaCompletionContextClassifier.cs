// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaCompletionContextClassifier
{
    // The one context this server completes: a string argument. Everything else is the analyzer's.
    public static StringArgContext? Classify(string text, int line, int character)
    {
        var textLines = text.Split('\n');
        if (line >= textLines.Length)
            return null;

        var lineText = textLines[line].TrimEnd('\r');
        return TryStringArgContext(lineText, Math.Min(character, lineText.Length));
    }

    // Detects whether the cursor is inside a string literal that is an argument
    // to a function call (e.g., require("...") or Find_Player("...")).
    private static StringArgContext? TryStringArgContext(string line, int col)
    {
        var bound = Math.Min(col, line.Length);

        // Walk backward to find the opening quote of the string we may be inside
        var i = bound - 1;
        while (i >= 0)
        {
            var ch = line[i];
            if (ch is '"' or '\'') break;
            if (ch is ')' or ';') return null;
            i--;
        }

        if (i < 0) return null;
        var quotePos = i;

        // Walk backward from the opening quote counting commas and tracking depth
        i = quotePos - 1;
        var paramIndex = 0;
        var depth = 0;

        while (i >= 0)
        {
            var ch = line[i];
            if (ch is ')' or ']' or '}')
            {
                depth++;
                i--;
                continue;
            }

            if (ch is '(' or '[' or '{')
            {
                if (depth > 0)
                {
                    depth--;
                    i--;
                    continue;
                }

                break;
            }

            if (ch == ',' && depth == 0) paramIndex++;
            if (ch is ';' or '\n') return null;
            i--;
        }

        if (i < 0) return null;
        var parenPos = i;

        // Extract function name immediately before the open paren
        i = parenPos - 1;
        while (i >= 0 && char.IsWhiteSpace(line[i])) i--;
        var nameEnd = i;
        if (nameEnd < 0) return null;

        while (i >= 0 && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i--;
        var nameStart = i + 1;

        if (nameStart > nameEnd) return null;
        return new StringArgContext(line[nameStart..(nameEnd + 1)], paramIndex);
    }
}